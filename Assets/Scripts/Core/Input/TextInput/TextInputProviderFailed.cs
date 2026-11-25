using System;
using Core.Infra.Messaging;

namespace Core.Input.TextInput
{
    public sealed class TextInputProviderFailed : Event
    {
        public Exception Error { get; }

        public TextInputProviderFailed(Exception error)
        {
            Error = error;
        }
    }
}
