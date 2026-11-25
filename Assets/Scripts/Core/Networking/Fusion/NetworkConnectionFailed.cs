using Core.Infra.Messaging;

namespace Core.Networking.Fusion
{
    public sealed class NetworkConnectionFailed : Event
    {
        public string Reason { get; }

        public NetworkConnectionFailed(string reason)
        {
            Reason = reason;
        }
    }
}
