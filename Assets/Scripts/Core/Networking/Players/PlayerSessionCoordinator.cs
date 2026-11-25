using System;
using System.Threading;
using Core.Networking.Core;
using Core.Networking.Fusion;
using Core.Resources;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace Core.Networking.Players
{
    public sealed class PlayerSessionCoordinator : IRunnerScopeInstaller
    {
        private readonly NetworkService _network;
        private readonly LocalPlayerIdentity _identity;
        private readonly SceneRef _scene;
        private readonly Func<PlayerAvatar, CancellationToken, UniTask<IDisposable>> _initialize;
        private readonly TimeSpan _readyTimeout;
        public PlayerSessionContext Current { get; private set; }

        public PlayerSessionCoordinator(NetworkService network, LocalPlayerIdentity identity, SceneRef scene,
            Func<PlayerAvatar, CancellationToken, UniTask<IDisposable>> initialize, TimeSpan? readyTimeout = null)
        {
            _network = network;
            _identity = identity;
            _scene = scene;
            _initialize = initialize;
            _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(45);
        }

        public IDisposable Install(NetworkRunner runner, RunnerGeneration generation)
        {
            var resources = new ResourceScope();
            try
            {
                var context = runner.gameObject.AddComponent<PlayerSessionContext>();
                resources.Own(new ActionLease(() =>
                {
                    if (ReferenceEquals(Current, context))
                    {
                        Current = null;
                    }
                    if (context)
                    {
                        UnityEngine.Object.Destroy(context);
                    }
                }));
                var players = resources.Own(new PlayerService(runner, generation, _network));
                context.Configure(players, generation);
                resources.Own(context.Bootstrap);
                resources.Own(context.Spawning);
                Current = context;
                return resources;
            }
            catch
            {
                resources.Dispose();
                throw;
            }
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
                var result = await context.Spawning.EnsureSpawnAsync(context.Director, runner.LocalPlayer, _identity.Id, timeout.Token);
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException($"Avatar spawn failed: {result.Failure}");
                }
                await UniTask.WaitUntil(() => context.Players.TryGet(runner.LocalPlayer, out _), cancellationToken: timeout.Token);
                context.Players.TryGet(runner.LocalPlayer, out var avatar);
                await context.Bootstrap.EnsureReadyAsync(avatar, _initialize, timeout.Token);
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
