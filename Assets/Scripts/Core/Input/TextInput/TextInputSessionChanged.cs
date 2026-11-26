using Core.Infra.Messaging;

namespace Core.Input.TextInput
{
    public sealed class TextInputSessionChanged : Event
    {
        public bool IsOpen { get; }

        public TextInputSessionChanged(bool isOpen)
        {
            IsOpen = isOpen;
        }
    }
}
