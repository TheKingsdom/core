using System;
using Core.Infra.Bootstrap;
using Core.Input;
using Core.Networking.Fusion;
using Core.Networking.Core;
using Core.Resources;
using Core.UI;
using Core.UI.Windows;
using Cysharp.Threading.Tasks;
using Fusion;
using Game.Session;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using Log = Core.Infra.Logging.Log;

namespace Game.Bootstrap
{
    public class GameClient : Client
    {
        [SerializeField] private string _sessionScene;
        [SerializeField] private Transform _viewAnchor;
        [SerializeField] private LoadingWindow _loadingWindow;
        [SerializeField] private GameSessionView _gameView;
        [SerializeField] private PauseWindow _pauseWindow;
        [SerializeField] private InputSystemUIInputModule _uiInput;
        [SerializeField] private string _startupRoom;
        [SerializeField] private string _startupRegion;
        [SerializeField] private bool _resumeOnStartup;

        private readonly ResourceScope _services = new();
        private LocalPlayerIdentity _identity;
        private LocalInputService _input;
        private WindowsService _windows;

        public NetworkService Network { get; private set; }
        public SessionModule Session { get; private set; }

        protected override void CreateServices()
        {
            base.CreateServices();

            if (string.IsNullOrWhiteSpace(_sessionScene))
            {
                return;
            }

            _identity = new LocalPlayerIdentity();
            var actions = InputActionsFactory.Create();
            try
            {
                _input = _services.Own(new LocalInputService(actions));
            }
            finally
            {
                Destroy(actions);
            }

            Network = _services.Own(new NetworkService(new LobbyDirectory(),
                new ReconnectStore(TimeSpan.FromMinutes(10)), _identity));
            _windows = _services.Own(new WindowsService(Assets, _input));
            if (_uiInput)
            {
                _services.Own(new UiInputBinding(_uiInput, _input));
            }
        }

        protected override void DisposeServices()
        {
            _services.Dispose();
            base.DisposeServices();
        }

        protected override void CreateModules()
        {
            base.CreateModules();

            Modules.BindAsync<GameStarted, IGameStarted>((module, _) => module.OnGameStarted());
            if (Network == null)
            {
                return;
            }

            var index = SceneUtility.GetBuildIndexByScenePath(_sessionScene);
            if (index < 0 || !_loadingWindow || !_gameView)
            {
                throw new InvalidOperationException("Configure a session build scene, a loading window and a game view");
            }

            var target = string.IsNullOrWhiteSpace(_startupRoom) ? null : new SessionLocator(_startupRoom, _startupRegion);
            Session = AddModule(new SessionModule(Network, _identity, _input, _windows, Assets,
                SceneRef.FromIndex(index), _viewAnchor, _loadingWindow, _gameView, _pauseWindow, target,
                _resumeOnStartup));
        }

        protected override async UniTask StartGame()
        {
            await base.StartGame();

            Log.Info("Game started");
            await Broadcaster.BroadcastAsync(new GameStarted());
        }
    }
}
