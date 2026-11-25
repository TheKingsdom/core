using Core.Infra.Messaging;

namespace Core.UI.Windows
{
    public sealed class WindowClosed : Event
    {
        public WindowBase Window { get; }

        public WindowClosed(WindowBase window)
        {
            Window = window;
        }
    }
}
