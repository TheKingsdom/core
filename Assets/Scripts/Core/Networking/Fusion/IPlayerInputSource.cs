
namespace Core.Networking.Fusion
{
    public interface IPlayerInputSource
    {
        bool TryCollect(out NetworkInputData data);
    }
}
