using Core.Infra.Messaging;

namespace Core.Input
{
    public sealed class InputDeviceChanged : Event
    {
        public bool IsGamepad { get; }

        public InputDeviceChanged(bool isGamepad)
        {
            IsGamepad = isGamepad;
        }
    }
}
