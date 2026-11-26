using Core.Networking.Fusion;

namespace Game.Bootstrap
{
    public interface INetworkSnapshotChanged
    {
        void OnNetworkSnapshotChanged(NetworkSnapshot snapshot);
    }
}
