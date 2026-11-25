using Core.Infra.Messaging;

namespace Core.UI.Windows
{
    public sealed class CurrentWindowChanged : Event
    {
        public WindowBase Previous { get; }
        public WindowBase Current { get; }

        public CurrentWindowChanged(WindowBase previous, WindowBase current)
        {
            Previous = previous;
            Current = current;
        }
    }
}
