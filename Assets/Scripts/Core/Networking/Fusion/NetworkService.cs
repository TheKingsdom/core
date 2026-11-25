using System;
using System.Collections.Generic;
using Core.Networking.Core;
using Core.Infra.Messaging;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;


namespace Core.Networking.Fusion
{
    public partial class NetworkService : IDisposable, INetworkRunnerCallbacks
    {
        public SceneRef? InitialScene { get; set; }
        private const int EmptyRoomTtlMilliseconds = 60_000;
        private NetworkRunner _runner => _session?.Runner;
        private string _connectedRegion;
        private readonly LocalPlayerIdentity _identity;
        public ReconnectStore Reconnect { get; }
        public LobbyDirectory Directory { get; }
        public IBroadcaster Broadcaster { get; } = new Broadcaster();
        public ConnectionState ConnectionState => Snapshot.Phase switch
        {
            ConnectionPhase.ConnectingLobby or ConnectionPhase.Disconnecting => global::Core.Networking.Fusion.ConnectionState.Connecting,
            ConnectionPhase.Lobby => global::Core.Networking.Fusion.ConnectionState.Lobby,
            ConnectionPhase.JoiningRoom => global::Core.Networking.Fusion.ConnectionState.Joining,
            ConnectionPhase.InRoom => global::Core.Networking.Fusion.ConnectionState.InRoom,
            _ => global::Core.Networking.Fusion.ConnectionState.Disconnected
        };
        public IReadOnlyList<PlayerRef> RoomPlayers => Snapshot.Peers;
        public bool IsSessionOwner => Snapshot.IsOwner;
        public NetworkRunner Runner => _runner;
        public NetworkSceneLoader SceneLoader => _session?.Scenes;

        public IDisposable RegisterInputSource(NetworkRunner runner, IPlayerInputSource source)
        {
            if (!AcceptCallback(runner))
            {
                throw new InvalidOperationException("Runner is not current");
            }

            if (_session.Input.Current != null)
            {
                throw new InvalidOperationException("Release the previous input binding before registering another source");
            }

            return _session.Input.Register(source);
        }

        internal void UpdateSession()
        {
            if (HasSessionOwnershipChanged())
            {
                PublishRoster();
            }
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!AcceptCallback(runner))
            {
                return;
            }

            PublishRoster();
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!AcceptCallback(runner))
            {
                return;
            }

            PublishRoster();
        }

        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> list)
        {
            if (!AcceptCallback(runner))
            {
                return;
            }

            var region = runner.LobbyInfo.Region;
            Directory.Update(Generation, region, list);
        }

        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
        {
        }

        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner)
        {
        }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            if (!AcceptCallback(runner))
            {
                return;
            }

            HandleUnexpectedDisconnect(runner);
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (!AcceptCallback(runner))
            {
                return;
            }

            HandleUnexpectedDisconnect(runner);
        }

        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress,
            NetConnectFailedReason reason)
        {
            if (!AcceptCallback(runner))
            {
                return;
            }

            if (Operations.IsBusy)
            {
                return;
            }

            PublishPhase(ConnectionPhase.Disconnected);
            var value = ConnectFailedReasonToString(reason);
            Broadcaster.Broadcast(new NetworkConnectionFailed(value));
        }

        private static string ConnectFailedReasonToString(NetConnectFailedReason reason) => reason switch
        {
            NetConnectFailedReason.Timeout => "No response from server",
            NetConnectFailedReason.ServerFull => "Server is full",
            NetConnectFailedReason.ServerRefused => "Server refused the connection",
            _ => $"Failed to connect ({reason})",
        };

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
        }

        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
        {
            if (!AcceptCallback(runner))
            {
                return;
            }

            var source = _session.Input.Current;
            var data = default(NetworkInputData);
            if (source != null)
            {
                source.TryCollect(out data);
            }

            input.Set(data);
        }

        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
        {
        }

        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner,
            NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
        }

        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }

        void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner,
            Dictionary<string, object> data)
        {
        }

        void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
        }

        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject networkObject, PlayerRef player)
        {
        }

        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject networkObject, PlayerRef player)
        {
        }

        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key,
            ReadOnlySpan<byte> data)
        {
        }

        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key,
            float progress)
        {
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Operations.Dispose();
            var session = _session;
            _session = null;
            if (session != null)
            {
                _epochs.Retire(session.Runner, session.Generation);
                session.StopOnOwnerDestroyedAsync().Forget();
            }
        }

        private void HandleUnexpectedDisconnect(NetworkRunner runner)
        {
            if (Snapshot.Phase != ConnectionPhase.InRoom)
            {
                return;
            }

            PublishPhase(ConnectionPhase.Disconnected);
            Broadcaster.Broadcast(new NetworkDisconnected());
            ReleaseDisconnectedRunnerAsync(runner).Forget(Debug.LogException);
        }

        private async UniTask ReleaseDisconnectedRunnerAsync(NetworkRunner runner)
        {
            // Leave outside Fusion's callback and skip if another operation has taken over.
            await UniTask.NextFrame();
            if (AcceptCallback(runner) && Snapshot.Phase == ConnectionPhase.Disconnected && !Operations.IsBusy)
            {
                await LeaveSessionAsync(LeaveMode.KeepReconnect);
            }
        }

        private bool HasSessionOwnershipChanged()
        {
            return Snapshot.Phase == ConnectionPhase.InRoom && AcceptCallback(_runner) &&
                Snapshot.IsOwner != _runner.IsLocalPlayerSessionOwner();
        }
    }
}
