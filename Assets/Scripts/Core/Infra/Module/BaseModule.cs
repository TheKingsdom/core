using Core.Infra.Messaging;

namespace Core.Infra.Module
{
    public abstract class BaseModule : IModule
    {
        public IBroadcaster Broadcaster { get; } = new Broadcaster();
        public abstract void Dispose();

        public abstract void Start();
    }
}
