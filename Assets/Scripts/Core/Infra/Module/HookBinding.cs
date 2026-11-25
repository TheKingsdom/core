using System;
using System.Collections.Generic;
using Core.Infra.Messaging;

namespace Core.Infra.Module
{
    internal sealed class HookBinding<TEvent, THook> : IHookBinding
        where TEvent : Event
        where THook : class
    {
        private readonly IBroadcaster _broadcaster;
        private readonly Action<THook, TEvent> _call;
        private readonly Dictionary<IModule, Action<TEvent>> _handlers = new();

        public HookBinding(IBroadcaster broadcaster, Action<THook, TEvent> call)
        {
            _broadcaster = broadcaster;
            _call = call;
        }

        public void Attach(IModule module)
        {
            if (module is not THook hook)
            {
                return;
            }

            Action<TEvent> handler = message =>
            {
                if (_handlers.ContainsKey(module))
                {
                    _call(hook, message);
                }
            };

            _handlers.Add(module, handler);
            _broadcaster.Subscribe(handler);
        }

        public void Detach(IModule module)
        {
            if (!_handlers.Remove(module, out var handler))
            {
                return;
            }

            _broadcaster.Unsubscribe(handler);
        }
    }
}
