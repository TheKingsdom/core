using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Infra.Module;
using Core.Input;
using Core.Networking.Core;
using Core.Networking.Fusion;
using Core.Networking.Players;
using Core.Services.Asset;
using Cysharp.Threading.Tasks;
using Fusion;
using Game.Bootstrap;
using Game.Windows;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Session
{
    public sealed class SessionModule : BaseModule, ISessionModule, ISessionModuleInternal, IGameStarted,
        IWindowChanged, INetworkSnapshotChanged, IPlayerInputSource
    {
        private readonly NetworkService _network;
        private readonly LocalInputService _input;
        private readonly IAssetService _assets;
        private readonly IWindowsModule _windows;
        private readonly SessionRecord _record;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly SceneRef _scene;
        private PlayerSessionCoordinator _players;
        private CancellationTokenSource _operation;
        private IInstance<GameSessionView> _gameView;
        private PlayerAvatar _inputPlayer;
        private NetworkRunner _inputRunner;
        private bool _disposed;

        private GameSessionView GameView => _gameView?.Value;

        public SessionModule(NetworkService network, LocalInputService input, IAssetService assets, IWindowsModule windows,
            SessionRecord record)
        {
            _network = network;
            _input = input;
            _assets = assets;
            _windows = windows;
            _record = record;

            var index = SceneUtility.GetBuildIndexByScenePath(record.ScenePath);
            if (index < 0 || string.IsNullOrWhiteSpace(record.GameViewId))
            {
                throw new InvalidOperationException("Set a session build scene and a game view asset ID in SessionRecord");
            }

            _scene = SceneRef.FromIndex(index);
        }

        public override void Start()
        {
            _players = new PlayerSessionCoordinator(_network, _scene, BindLocalPlayerAsync, UnbindLocalPlayer,
                broadcaster: Broadcaster);
            _network.InitialScene = _scene;
            _network.Configure(_players);
            HandlePauseAsync(_lifetime.Token).SuppressCancellationThrow().Forget(Debug.LogException);
        }

        public override void Dispose()
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
                    UnbindLocalPlayer(_inputPlayer);
                    ReleaseGameView();
                }
                finally
                {
                    _network.LeaveSessionAsync(LeaveMode.KeepReconnect).Forget(Debug.LogException);
                    _lifetime.Dispose();
                }
            }
        }

        async UniTask IGameStarted.OnGameStarted()
        {
            if (!string.IsNullOrWhiteSpace(_record.StartupRoom))
            {
                await JoinAsync(new SessionLocator(_record.StartupRoom, _record.StartupRegion));
            }
            else if (_record.ResumeOnStartup && _network.Reconnect.Current?.IsComplete == true)
            {
                await ReconnectAsync();
            }
        }

        public UniTask<OperationResult> CreateAsync(CreateSessionRequest request, CancellationToken token = default)
        {
            return EnterAsync(inner => _network.CreateSessionAsync(request, inner), token);
        }

        public UniTask<OperationResult> JoinAsync(SessionLocator target, CancellationToken token = default)
        {
            return EnterAsync(inner => _network.JoinSessionAsync(target, inner), token);
        }

        public UniTask<OperationResult> JoinRandomAsync(CancellationToken token = default)
        {
            return EnterAsync(_network.JoinRandomSessionAsync, token);
        }

        public UniTask<OperationResult> ReconnectAsync(CancellationToken token = default)
        {
            var target = _network.Reconnect.Current;
            return EnterAsync(async inner =>
            {
                if (target?.IsComplete != true)
                {
                    return AdmissionResult(OperationStatus.Rejected, NetworkErrorCode.InvalidRequest, "No reconnect target");
                }

                var result = await _network.LeaveSessionAsync(LeaveMode.KeepReconnect, inner);
                return result.IsSuccess ? await _network.JoinSessionAsync(target, inner) : result;
            }, token);
        }

        public async UniTask<OperationResult> LeaveAsync(LeaveMode mode = LeaveMode.ForgetSession)
        {
            await UniTask.SwitchToMainThread();
            if (_disposed)
            {
                return AdmissionResult(OperationStatus.Rejected, NetworkErrorCode.Disposed, "Session module disposed");
            }

            try
            {
                _operation?.Cancel();
                ReleaseGameView();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }

            try
            {
                _windows.ClosePause();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }

            return await _network.LeaveSessionAsync(mode);
        }

        bool IPlayerInputSource.TryCollect(out NetworkInputData data)
        {
            data = new NetworkInputData
            {
                MoveInput = _input.Move,
                Paused = !_input.GameplayEnabled
            };
            return _inputPlayer;
        }

        void IWindowChanged.OnWindowChanged(bool hasOpenWindow) => UpdateGameplayGate(hasOpenWindow);

        void INetworkSnapshotChanged.OnNetworkSnapshotChanged(NetworkSnapshot snapshot)
        {
            if (snapshot.Phase != ConnectionPhase.InRoom && GameView)
            {
                try
                {
                    ReleaseGameView();
                }
                finally
                {
                    _windows.ClosePause();
                }
            }

            Broadcaster.Broadcast(snapshot);
        }

        private async UniTask<OperationResult> EnterAsync(
            Func<CancellationToken, UniTask<OperationResult>> operation, CancellationToken token)
        {
            await UniTask.SwitchToMainThread();
            if (_disposed || _operation != null)
            {
                return _disposed
                    ? AdmissionResult(OperationStatus.Rejected, NetworkErrorCode.Disposed, "Session module disposed")
                    : AdmissionResult(OperationStatus.Busy);
            }

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, token);
            _operation = cancellation;
            RunnerGeneration? enteredGeneration = null;
            var loadingOpened = false;
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await _windows.OpenLoadingAsync(cancellation.Cancel, cancellation.Token);
                loadingOpened = true;
                var result = await operation(cancellation.Token);
                if (!result.IsSuccess)
                {
                    return result;
                }

                enteredGeneration = result.Generation;
                await ShowGameViewAsync(result.Generation, cancellation.Token);
                return result;
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                if (enteredGeneration is { } generation)
                {
                    await LeaveAfterFailedEntryAsync(generation);
                }

                return AdmissionResult(OperationStatus.Cancelled);
            }
            catch (Exception error)
            {
                await UniTask.SwitchToMainThread();
                if (enteredGeneration is { } generation)
                {
                    await LeaveAfterFailedEntryAsync(generation);
                }

                Debug.LogException(error);
                return AdmissionResult(OperationStatus.Failed, NetworkErrorCode.Unexpected, error.Message);
            }
            finally
            {
                try
                {
                    if (loadingOpened && !_disposed)
                    {
                        _windows.CloseLoading();
                    }
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
                finally
                {
                    _operation = null;
                }
            }
        }

        private async UniTask LeaveAfterFailedEntryAsync(RunnerGeneration generation)
        {
            try
            {
                ReleaseGameView();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            finally
            {
                if (_network.Generation == generation)
                {
                    await _network.LeaveSessionAsync(LeaveMode.KeepReconnect);
                }
            }
        }

        private async UniTask ShowGameViewAsync(RunnerGeneration generation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ReleaseGameView();
            var id = _record.GameViewId;
            var pending = _assets.Instantiate<GameSessionView>(id).AsTask();
            GameSessionView view;
            try
            {
                view = await pending.AsUniTask().AttachExternalCancellation(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                DestroyPendingGameViewAsync(pending, id).SuppressCancellationThrow().Forget(Debug.LogException);
                throw;
            }

            await UniTask.SwitchToMainThread();
            _gameView = new DefaultInstance<GameSessionView>(id, view);
            token.ThrowIfCancellationRequested();

            var player = _players.Current ? _players.Current.Bootstrap.LocalPlayer : null;
            if (!player || !player.IsReady || _network.Snapshot.Phase != ConnectionPhase.InRoom ||
                _network.Snapshot.Generation != generation ||
                !_network.SceneLoader.TryGetRegistration(_scene, out var registration))
            {
                throw new InvalidOperationException("The local player or the session scene is unavailable");
            }

            view.Inject(new SessionViewData(registration.Scene, player, _network.Snapshot));
            view.Init(this, _assets);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(GameView, view))
            {
                throw new InvalidOperationException("The session ended while the game view was initializing");
            }

            view.gameObject.SetActive(true);
            token.ThrowIfCancellationRequested();
            UpdateGameplayGate(_windows.HasOpenWindow);
        }

        private async UniTask DestroyPendingGameViewAsync(Task<GameSessionView> pending, string id)
        {
            var view = await pending.AsUniTask();
            await UniTask.SwitchToMainThread();
            _assets.Destroy(new DefaultInstance<GameSessionView>(id, view));
        }

        private void ReleaseGameView()
        {
            var instance = _gameView;
            _gameView = null;
            if (instance == null)
            {
                return;
            }

            try
            {
                if (instance.Value)
                {
                    instance.Value.Dispose();
                }
            }
            finally
            {
                try
                {
                    if (instance.Value)
                    {
                        instance.Value.gameObject.SetActive(false);
                    }

                    _assets.Destroy(instance);
                }
                finally
                {
                    UpdateGameplayGate(_windows.HasOpenWindow);
                }
            }
        }

        private UniTask BindLocalPlayerAsync(PlayerAvatar avatar, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var runner = avatar.Runner;
            _input.BindPlayer(avatar);
            try
            {
                _network.RegisterInputSource(runner, this);
                _inputPlayer = avatar;
                _inputRunner = runner;
                return UniTask.CompletedTask;
            }
            catch
            {
                _input.UnbindPlayer(avatar);
                throw;
            }
        }

        private void UnbindLocalPlayer(PlayerAvatar avatar)
        {
            if (ReferenceEquals(avatar, null) || !ReferenceEquals(_inputPlayer, avatar))
            {
                return;
            }

            var runner = _inputRunner;
            _inputPlayer = null;
            _inputRunner = null;
            try
            {
                _network.UnregisterInputSource(runner, this);
            }
            finally
            {
                _input.UnbindPlayer(avatar);
            }
        }

        private async UniTask HandlePauseAsync(CancellationToken token)
        {
            while (true)
            {
                await UniTask.NextFrame(cancellationToken: token);
                if (GameView && !_windows.HasOpenWindow && _input.WasPressed(GameInputAction.Pause))
                {
                    try
                    {
                        await _windows.OpenPauseAsync(
                            () => LeaveAsync().Forget(Debug.LogException),
                            () => LeaveAsync(LeaveMode.KeepReconnect).Forget(Debug.LogException),
                            token);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        Debug.LogException(error);
                    }
                }
            }
        }

        private void UpdateGameplayGate(bool hasOpenWindow)
        {
            if (_inputPlayer)
            {
                _input.SetGameplayBlocked(_inputPlayer, !GameView || hasOpenWindow);
            }
        }

        private OperationResult AdmissionResult(OperationStatus status, NetworkErrorCode code = default,
            string reason = null)
        {
            var error = reason == null ? null : new NetworkError(NetworkErrorStage.Admission, code, reason);
            return new OperationResult(status, default, _network.Generation, error);
        }
    }
}
