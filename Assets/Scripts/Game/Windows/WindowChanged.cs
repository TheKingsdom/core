using Core.Infra.Messaging;

namespace Game.Windows
{
    public sealed class WindowChanged : Event
    {
        public bool HasOpenWindow { get; }

        public WindowChanged(bool hasOpenWindow)
        {
            HasOpenWindow = hasOpenWindow;
        }
    }
}
