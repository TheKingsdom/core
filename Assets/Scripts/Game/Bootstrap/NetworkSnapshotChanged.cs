using Core.Infra.Messaging;
using Core.Networking.Fusion;

namespace Game.Bootstrap
{
    public sealed class NetworkSnapshotChanged : Event
    {
        public NetworkSnapshotChanged(NetworkSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public NetworkSnapshot Snapshot { get; }
    }
}
