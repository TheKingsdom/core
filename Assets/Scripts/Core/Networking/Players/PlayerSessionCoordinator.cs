using System;
using System.Threading;
using Core.Networking.Core;
using Core.Infra.Messaging;
using Core.Networking.Fusion;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace Core.Networking.Players
{
    public sealed class PlayerSessionCoordinator : IRunnerScopeInstaller
    {
        private readonly NetworkService _network;
        private readonly SceneRef _scene;
        private readonly Func<PlayerAvatar, CancellationToken, UniTask> _initialize;
        private readonly Action<PlayerAvatar> _release;
        private readonly IBroadcaster _broadcaster;
        private readonly TimeSpan _readyTimeout;
        public PlayerSessionContext Current { get; private set; }

        public PlayerSessionCoordinator(NetworkService network, SceneRef scene,
            Func<PlayerAvatar, CancellationToken, UniTask> initialize = null, Action<PlayerAvatar> release = null,
            TimeSpan? readyTimeout = null, IBroadcaster broadcaster = null)
        {
            _network = network;
            _scene = scene;
            _initialize = initialize;
            _release = release;
            _broadcaster = broadcaster;
            _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(45);
        }

        public void Install(NetworkRunner runner, RunnerGeneration generation)
        {
            var context = runner.gameObject.AddComponent<PlayerSessionContext>();
            Current = context;
            context.Configure(new PlayerService(runner, generation, _network, _broadcaster), generation);
        }

        public void Release(NetworkRunner runner, RunnerGeneration generation)
        {
            var context = Current;
            if (ReferenceEquals(context, null) || context.Generation != generation)
            {
                return;
            }

            Current = null;
            context.Release();
        }

        public async UniTask PrepareRoomAsync(NetworkRunner runner, RunnerGeneration generation,
            CreateSessionRequest creation, CancellationToken cancellationToken)
        {
            var context = Current;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var timer = timeout.CancelAfterSlim(_readyTimeout, DelayType.Realtime);
            try
            {
                await _network.SceneLoader.EnsureSceneRegisteredAsync(generation, _scene, timeout.Token);
                await UniTask.WaitUntil(() => context && context.Director, cancellationToken: timeout.Token);
                var result = await context.Spawning.EnsureSpawnAsync(context.Director, runner.LocalPlayer,
                    _network.LocalPlayerId, timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                if (result.Failure == SpawnFailure.Timeout)
                {
                    throw new TimeoutException("Avatar spawn deadline exceeded");
                }

                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException($"Avatar spawn failed: {result.Failure}");
                }
                await UniTask.WaitUntil(() => context.Players.TryGet(runner.LocalPlayer, out _), cancellationToken: timeout.Token);
                context.Players.TryGet(runner.LocalPlayer, out var avatar);
                await context.Bootstrap.EnsureReadyAsync(avatar, _initialize, _release, timeout.Token);
            }
            catch (OperationCanceledException error) when (timeout.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Local player readiness deadline exceeded", error);
            }
        }

        public UniTask PrepareLeaveAsync(NetworkRunner runner, RunnerGeneration generation)
        {
            var context = Current;
            if (context && context.Generation == generation)
            {
                context.Bootstrap.Dispose();
            }
            return UniTask.CompletedTask;
        }
    }
}
