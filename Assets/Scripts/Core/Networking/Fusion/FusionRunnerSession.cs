using Core.Infra.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Networking.Core;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace Core.Networking.Fusion
{
    internal sealed class FusionRunnerSession
    {
        private readonly GameObject _gameObject;
        private readonly NetworkService _service;
        private bool _callbacksRegistered;
        private readonly IRunnerScopeInstaller _installer;
        private readonly ShutdownBarrier _shutdown;

        public NetworkRunner Runner { get; }
        public FusionSceneManager SceneManager { get; }
        public NetworkSceneLoader Scenes { get; }
        public RunnerGeneration Generation { get; }
        public IPlayerInputSource Input { get; set; }
        public bool IsStopping => _shutdown.IsStarted;
        public bool IsCleaned => _shutdown.IsCleaned;
        public const int ShutdownTimeoutSeconds = 20;

        public FusionRunnerSession(NetworkService service, RunnerGeneration generation, IRunnerScopeInstaller installer)
        {
            Generation = generation;
            _service = service;
            _installer = installer;
            _gameObject = new GameObject($"NetworkRunner G{generation}");
            UnityEngine.Object.DontDestroyOnLoad(_gameObject);
            Runner = _gameObject.AddComponent<NetworkRunner>();
            SceneManager = _gameObject.AddComponent<FusionSceneManager>();
            var sceneEvents = new Broadcaster();
            SceneManager.SetBroadcaster(sceneEvents);
            Scenes = new NetworkSceneLoader(Runner, SceneManager, generation, sceneEvents);
            _gameObject.AddComponent<NetworkServiceLink>().SetService(service);
            _shutdown = new ShutdownBarrier(
                async () =>
                {
                    if (Runner)
                    {
                        await Runner.Shutdown(destroyGameObject: false);
                    }

                    if (SceneManager)
                    {
                        await SceneManager.UnloadOwnedScenesAsync();
                    }
                },
                deadlineToken =>
                {
                    var delayTimeSpan = TimeSpan.FromSeconds(ShutdownTimeoutSeconds);
                    return UniTask.Delay(delayTimeSpan, ignoreTimeScale: true, cancellationToken: deadlineToken).AsTask();
                },
                ReleaseAfterShutdown,
                exception =>
                {
                    var message = $"[NetworkService] G{Generation} late shutdown failed: {exception.GetType().Name}";
                    Debug.LogError(message);
                });
        }

        public void Install() => _installer.Install(Runner, Generation);

        public void ActivateCallbacks()
        {
            if (_callbacksRegistered)
            {
                throw new InvalidOperationException("Callbacks already registered");
            }

            Runner.AddCallbacks(_service);
            _callbacksRegistered = true;
            TrackOwnershipAsync().Forget(Debug.LogException);
        }

        public UniTask PrepareRoomAsync(CreateSessionRequest creation, CancellationToken token) =>
            _installer.PrepareRoomAsync(Runner, Generation, creation, token);

        public UniTask PrepareLeaveAsync() => _installer.PrepareLeaveAsync(Runner, Generation);

        private async UniTask TrackOwnershipAsync()
        {
            while (Runner && !IsStopping)
            {
                await UniTask.NextFrame();
                _service.UpdateSession();
            }
        }

        public Task ShutdownAsync()
        {
            Scenes.Dispose();
            return _shutdown.StopAsync();
        }

        private void ReleaseAfterShutdown()
        {
            if (IsCleaned)
            {
                return;
            }

            if (ShouldRemoveRunnerCallbacks())
            {
                Runner.RemoveCallbacks(_service);
            }

            Input = null;
            try
            {
                _installer.Release(Runner, Generation);
            }
            finally
            {
                if (_gameObject)
                {
                    UnityEngine.Object.Destroy(_gameObject);
                }
            }
        }

        public async UniTask StopOnOwnerDestroyedAsync()
        {
            try
            {
                await ShutdownAsync();
            }
            catch (Exception exception)
            {
                var message = $"[NetworkService] G{Generation} owner destroyed during shutdown: {exception.GetType().Name}";
                Debug.LogWarning(message);
            }
        }

        private bool ShouldRemoveRunnerCallbacks()
        {
            return Runner && _callbacksRegistered;
        }
    }
}
