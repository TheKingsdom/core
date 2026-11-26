using System;
using Core.Infra.Bootstrap;
using Core.Input;
using Core.Networking.Fusion;
using Cysharp.Threading.Tasks;
using Game.Session;
using Game.Windows;
using Log = Core.Infra.Logging.Log;

namespace Game.Bootstrap
{
    public class GameClient : Client
    {
        private LocalInputService _input;
        private NetworkService _network;

        public ISessionModule Session { get; private set; }

        protected override void CreateServices()
        {
            base.CreateServices();

            var actions = InputActionsFactory.Create();
            try
            {
                _input = new LocalInputService(actions, Broadcaster);
            }
            finally
            {
                Destroy(actions);
            }

            _network = new NetworkService(new LobbyDirectory(Broadcaster),
                CreateReconnectStore(), new LocalPlayerIdentity(), Broadcaster);
            Broadcaster.Subscribe<NetworkSnapshot>(PublishNetworkSnapshotChanged);
        }

        protected virtual ReconnectStore CreateReconnectStore() => new(TimeSpan.FromMinutes(10));

        protected override void DisposeServices()
        {
            Broadcaster.Unsubscribe<NetworkSnapshot>(PublishNetworkSnapshotChanged);
            try
            {
                _network?.Dispose();
            }
            finally
            {
                _input?.Dispose();
                base.DisposeServices();
            }
        }

        protected override void CreateModules()
        {
            base.CreateModules();

            Modules.BindAsync<GameStarted, IGameStarted>((module, _) => module.OnGameStarted());
            Modules.Bind<WindowChanged, IWindowChanged>((module, message) => module.OnWindowChanged(message.HasOpenWindow));
            Modules.Bind<NetworkSnapshotChanged, INetworkSnapshotChanged>(
                (module, message) => module.OnNetworkSnapshotChanged(message.Snapshot));

            var session = Records.GetRecord<SessionRecord>();
            if (string.IsNullOrWhiteSpace(session.ScenePath))
            {
                return;
            }

            var windows = Records.GetRecord<WindowsRecord>();
            if (string.IsNullOrWhiteSpace(windows.LoadingWindowId))
            {
                throw new InvalidOperationException("Set the loading window asset ID in WindowsRecord");
            }

            IWindowsModule windowsModule = new WindowsModule(Assets, _input, windows, Broadcaster);
            var sessionModule = new SessionModule(_network, _input, Assets, windowsModule, session);
            AddModule(windowsModule);
            Session = AddModule(sessionModule);
        }

        private void PublishNetworkSnapshotChanged(NetworkSnapshot snapshot)
        {
            Broadcaster.Broadcast(new NetworkSnapshotChanged(snapshot));
        }

        protected override async UniTask StartGame()
        {
            await base.StartGame();

            Log.Info("Game started");
            await Broadcaster.BroadcastAsync(new GameStarted());
        }
    }
}
