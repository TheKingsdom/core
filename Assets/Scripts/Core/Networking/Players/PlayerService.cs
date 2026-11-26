using System;
using System.Linq;
using Core.Infra.Messaging;
using Core.Networking.Core;
using Core.Networking.Fusion;
using Fusion;
using AvatarIndex = Core.Networking.Core.AvatarRegistry<Fusion.NetworkId, Fusion.PlayerRef, Core.Networking.Players.PlayerAvatar>;

namespace Core.Networking.Players
{
    public sealed class PlayerService : IDisposable
    {
        private readonly AvatarIndex _avatars = new(PlayerRef.None);
        private readonly NetworkService _network;
        private readonly IBroadcaster _broadcaster;
        public NetworkRunner Runner { get; }
        public RunnerGeneration Generation { get; }
        public AvatarIndex.Record[] Avatars => _avatars.Records.ToArray();
        public PlayerRef[] Participants => _network.Snapshot.Generation == Generation
            ? _network.Snapshot.Peers.ToArray()
            : Array.Empty<PlayerRef>();
        public bool IsDisposed { get; private set; }

        public PlayerService(NetworkRunner runner, RunnerGeneration generation, NetworkService network,
            IBroadcaster broadcaster = null)
        {
            Runner = runner;
            Generation = generation;
            _avatars.Changed += OnAvatarChanged;
            _network = network;
            _broadcaster = broadcaster ?? new Broadcaster();
        }

        public void Register(PlayerAvatar avatar)
        {
            _avatars.Register(avatar.Object.Id, avatar.Player, avatar, avatar.Identity, avatar.IsDisconnected);
        }

        public void Unregister(NetworkId id, PlayerAvatar avatar)
        {
            if (_avatars.TryGetById(id, out var record) && ReferenceEquals(record.Avatar, avatar))
            {
                _avatars.Unregister(id, record.Version);
            }
        }

        public void Refresh(PlayerAvatar avatar)
        {
            if (!IsDisposed && _avatars.TryGetById(avatar.Object.Id, out var record) &&
                ReferenceEquals(record.Avatar, avatar))
            {
                _avatars.Register(record.Id, avatar.Player, avatar, avatar.Identity, avatar.IsDisconnected);
            }
        }

        public bool TryGet(PlayerRef player, out PlayerAvatar avatar)
        {
            avatar = null;
            if (!_avatars.TryGetByPlayer(player, out var record) || record.IsDisconnected)
            {
                return false;
            }
            avatar = record.Avatar;
            return avatar;
        }

        public PlayerAvatar FindIdentity(long identity)
        {
            return _avatars.Records.FirstOrDefault(record => record.Avatar && record.Avatar.Identity == identity)?.Avatar;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            IsDisposed = true;
            _avatars.Changed -= OnAvatarChanged;
            _avatars.Dispose();
        }

        private void OnAvatarChanged(AvatarChangeKind kind, AvatarIndex.Record record)
        {
            _broadcaster.Broadcast(new PlayerAvatarChanged(kind, record.Avatar));
        }
    }
}
