using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Core.Networking.Players
{
    public sealed class LocalPlayerBootstrapper : IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();
        private PlayerAvatar _avatar;
        private Action<PlayerAvatar> _release;
        private bool _initializing;
        private bool _disposed;

        public PlayerAvatar LocalPlayer { get; private set; }

        public async UniTask EnsureReadyAsync(PlayerAvatar avatar,
            Func<PlayerAvatar, CancellationToken, UniTask> initialize, Action<PlayerAvatar> release, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            _avatar = avatar;
            _release = release;
            _initializing = true;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (initialize != null)
                {
                    await initialize(avatar, linked.Token);
                }

                linked.Token.ThrowIfCancellationRequested();
                if (!avatar || avatar.Player != avatar.Runner.LocalPlayer)
                {
                    throw new OperationCanceledException("Local avatar ownership changed");
                }

                avatar.RPC_SetReady();
                await UniTask.WaitUntil(() => avatar && avatar.IsReady, cancellationToken: linked.Token);
                LocalPlayer = avatar;
            }
            catch
            {
                Release();
                throw;
            }
            finally
            {
                _initializing = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _lifetime.Cancel();
            }
            finally
            {
                try
                {
                    if (!_initializing)
                    {
                        Release();
                    }
                }
                finally
                {
                    _lifetime.Dispose();
                }
            }
        }

        private void Release()
        {
            var release = _release;
            var avatar = _avatar;
            _release = null;
            _avatar = null;
            LocalPlayer = null;
            release?.Invoke(avatar);
        }
    }
}
