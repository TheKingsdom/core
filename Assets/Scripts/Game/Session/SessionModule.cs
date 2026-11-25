using System;
using System.Threading;
using Core.Infra.Module;
using Core.Input;
using Core.Networking.Core;
using Core.Networking.Fusion;
using Core.Networking.Players;
using Core.Resources;
using Core.Services.Asset;
using Core.UI.Windows;
using Cysharp.Threading.Tasks;
using Fusion;
using Game.Bootstrap;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Session
{
    public sealed class SessionModule : BaseModule, IGameStarted, IPlayerInputSource
    {
        private readonly NetworkService _network;
        private readonly PlayerSessionCoordinator _players;
        private readonly LocalInputService _input;
        private readonly WindowsService _windows;
        private readonly IAssetService _assets;
        private readonly SceneRef _scene;
        private readonly Transform _anchor;
        private readonly LoadingWindow _loadingWindow;
        private readonly PauseWindow _pauseWindow;
        private readonly GameSessionView _gameViewPrefab;
        private readonly SessionLocator _startupTarget;
        private readonly bool _resumeOnStartup;
        private readonly Func<PlayerAvatar, CancellationToken, UniTask<IDisposable>> _initializePlayer;
        private readonly CancellationTokenSource _lifetime = new();
        private CancellationTokenSource _operation;
        private GameSessionView _gameView;
        private bool _disposed;

        public GameSessionView GameView => _gameView;

        public SessionModule(NetworkService network, LocalPlayerIdentity identity, LocalInputService input,
            WindowsService windows, IAssetService assets, SceneRef scene, Transform anchor,
            LoadingWindow loadingWindow, GameSessionView gameView, PauseWindow pauseWindow = null,
            SessionLocator startupTarget = null, bool resumeOnStartup = false,
            Func<PlayerAvatar, CancellationToken, UniTask<IDisposable>> initializePlayer = null)
        {
            _network = network;
            _input = input;
            _windows = windows;
            _assets = assets;
            _scene = scene;
            _anchor = anchor;
            _loadingWindow = loadingWindow;
            _gameViewPrefab = gameView;
            _pauseWindow = pauseWindow;
            _startupTarget = startupTarget;
            _resumeOnStartup = resumeOnStartup;
            _players = new PlayerSessionCoordinator(network, identity, scene, BindLocalPlayerAsync);
            _initializePlayer = initializePlayer;
        }

        public override void Start()
        {
            _network.InitialScene = _scene;
            _network.Configure(_players);
            _network.Broadcaster.Subscribe<NetworkSnapshot>(OnNetworkSnapshot);
            _windows.Broadcaster.Subscribe<CurrentWindowChanged>(OnWindowChanged);
            HandleInputAsync(_lifetime.Token).SuppressCancellationThrow().Forget(Debug.LogException);
        }

        public async UniTask OnGameStarted()
        {
            if (_startupTarget != null)
            {
                await JoinAsync(_startupTarget);
            }
            else if (_resumeOnStartup && _network.Reconnect.Current?.IsComplete == true)
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
            if (_disposed)
            {
                return AdmissionResult(OperationStatus.Rejected, NetworkErrorCode.Disposed, "Session module disposed");
            }

            _operation?.Cancel();
            try
            {
                ReleaseGameView();
                _windows.CloseViews(this);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            return await _network.LeaveSessionAsync(mode);
        }

        public bool TryCollect(out NetworkInputData data)
        {
            data = new NetworkInputData
            {
                MoveInput = _input.Move,
                Paused = !_input.GameplayEnabled
            };
            return _players.Current && _players.Current.Bootstrap.LocalPlayer;
        }

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _network.Broadcaster.Unsubscribe<NetworkSnapshot>(OnNetworkSnapshot);
            _windows.Broadcaster.Unsubscribe<CurrentWindowChanged>(OnWindowChanged);
            try
            {
                _lifetime.Cancel();
                try
                {
                    ReleaseGameView();
                }
                finally
                {
                    _windows.ReleaseViews(this);
                }
            }
            finally
            {
                _network.LeaveSessionAsync(LeaveMode.KeepReconnect).Forget(Debug.LogException);
                _lifetime.Dispose();
            }
        }

        private async UniTask<OperationResult> EnterAsync(
            Func<CancellationToken, UniTask<OperationResult>> operation, CancellationToken token)
        {
            if (_disposed || _operation != null)
            {
                return _disposed
                    ? AdmissionResult(OperationStatus.Rejected, NetworkErrorCode.Disposed, "Session module disposed")
                    : AdmissionResult(OperationStatus.Busy);
            }

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, token);
            _operation = cancellation;
            var enteredRoom = false;
            WindowBase loading = null;
            try
            {
                if (cancellation.IsCancellationRequested)
                {
                    return AdmissionResult(OperationStatus.Cancelled);
                }

                loading = _windows.Open(this, _loadingWindow, _anchor, cancellation.Cancel);
                var result = await operation(cancellation.Token);
                enteredRoom = result.IsSuccess;
                if (!enteredRoom)
                {
                    return result;
                }

                cancellation.Token.ThrowIfCancellationRequested();
                ShowGameView();
                cancellation.Token.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException)
            {
                if (enteredRoom)
                {
                    await LeaveAfterFailedEntryAsync();
                }
                return AdmissionResult(OperationStatus.Cancelled);
            }
            catch (Exception error)
            {
                if (enteredRoom)
                {
                    await LeaveAfterFailedEntryAsync();
                }
                Debug.LogException(error);
                return AdmissionResult(OperationStatus.Failed, NetworkErrorCode.Unexpected, error.Message);
            }
            finally
            {
                try
                {
                    if (loading && !_disposed)
                    {
                        _windows.Close(loading);
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

        private async UniTask LeaveAfterFailedEntryAsync()
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
                await _network.LeaveSessionAsync(LeaveMode.KeepReconnect);
            }
        }

        private void ShowGameView()
        {
            var player = _players.Current ? _players.Current.Bootstrap.LocalPlayer : null;
            if (!player || !player.IsReady || _network.Snapshot.Phase != ConnectionPhase.InRoom ||
                !_network.SceneLoader.TryGetRegistration(_scene, out var registration))
            {
                throw new InvalidOperationException("The local player or the session scene is unavailable");
            }

            ReleaseGameView();
            _gameView = Object.Instantiate(_gameViewPrefab, _anchor);
            _gameView.Data = new SessionViewData(registration.Scene, player, _network.Snapshot);
            _gameView.Init(this, _assets);
            _gameView.gameObject.SetActive(true);
            UpdateGameplayGate();
        }

        private void ReleaseGameView()
        {
            var gameView = _gameView;
            _gameView = null;
            if (gameView)
            {
                try
                {
                    gameView.Dispose();
                }
                finally
                {
                    gameView.Data = null;
                    gameView.gameObject.SetActive(false);
                    Object.Destroy(gameView.gameObject);
                    UpdateGameplayGate();
                }
            }
        }

        private async UniTask<IDisposable> BindLocalPlayerAsync(PlayerAvatar avatar, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var scope = new ResourceScope();
            try
            {
                scope.Own(_input.BindPlayer(avatar));
                scope.Own(_network.RegisterInputSource(avatar.Runner, this));
                if (_initializePlayer != null)
                {
                    scope.Own(await _initializePlayer(avatar, token));
                }
                token.ThrowIfCancellationRequested();
                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        private async UniTask HandleInputAsync(CancellationToken token)
        {
            while (true)
            {
                await UniTask.NextFrame(cancellationToken: token);
                if (_pauseWindow && _gameView && !_windows.CurrentWindow && _input.WasPressed(GameInputAction.Pause))
                {
                    var model = new PauseWindow.Model(
                        () => LeaveAsync().Forget(Debug.LogException),
                        () => LeaveAsync(LeaveMode.KeepReconnect).Forget(Debug.LogException));
                    _windows.Open(this, _pauseWindow, _anchor, model, isDisplacing: false);
                }
                else
                {
                    _windows.HandleCancel();
                }
            }
        }

        private void OnWindowChanged(CurrentWindowChanged _) => UpdateGameplayGate();

        private void OnNetworkSnapshot(NetworkSnapshot snapshot)
        {
            if (snapshot.Phase != ConnectionPhase.InRoom && _gameView)
            {
                ReleaseGameView();
                if (_windows.CurrentWindow is PauseWindow)
                {
                    _windows.CloseViews(this);
                }
            }
            Broadcaster.Broadcast(snapshot);
        }

        private void UpdateGameplayGate()
        {
            var player = _players.Current ? _players.Current.Bootstrap.LocalPlayer : null;
            if (player)
            {
                _input.SetGameplayBlocked(player, !_gameView || _windows.CurrentWindow);
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
