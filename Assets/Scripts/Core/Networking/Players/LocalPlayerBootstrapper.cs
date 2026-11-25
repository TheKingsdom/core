using System;
using System.Threading;
using Core.Resources;
using Cysharp.Threading.Tasks;

namespace Core.Networking.Players
{
    public sealed class LocalPlayerBootstrapper : IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ResourceScope _resources = new();
        public PlayerAvatar LocalPlayer { get; private set; }

        public async UniTask EnsureReadyAsync(PlayerAvatar avatar,
            Func<PlayerAvatar, CancellationToken, UniTask<IDisposable>> initialize, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            var resource = await initialize(avatar, linked.Token);
            _resources.Own(resource);
            linked.Token.ThrowIfCancellationRequested();
            if (!avatar || avatar.Player != avatar.Runner.LocalPlayer)
            {
                throw new OperationCanceledException("Local avatar ownership changed");
            }
            avatar.RPC_SetReady();
            await UniTask.WaitUntil(() => avatar && avatar.IsReady, cancellationToken: linked.Token);
            LocalPlayer = avatar;
        }

        public void Dispose()
        {
            if (_resources.IsDisposed)
            {
                return;
            }
            try
            {
                _lifetime.Cancel();
            }
            finally
            {
                try
                {
                    _resources.Dispose();
                }
                finally
                {
                    LocalPlayer = null;
                    _lifetime.Dispose();
                }
            }
        }
    }
}
