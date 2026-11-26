using Core.Infra.Messaging;

namespace Core.Infra.Module
{
    public abstract class BaseModule : IModule, IInternalModule
    {
        protected IBroadcaster Broadcaster { get; } = new Broadcaster();
        IBroadcaster IInternalModule.Broadcaster => Broadcaster;

        public abstract void Dispose();

        public abstract void Start();
    }
}
