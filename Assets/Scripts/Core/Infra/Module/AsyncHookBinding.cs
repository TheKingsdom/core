using System;
using System.Collections.Generic;
using Core.Infra.Messaging;
using Cysharp.Threading.Tasks;

namespace Core.Infra.Module
{
    internal sealed class AsyncHookBinding<TEvent, THook> : IHookBinding
        where TEvent : AsyncEvent
        where THook : class
    {
        private readonly IBroadcaster _broadcaster;
        private readonly Func<THook, TEvent, UniTask> _call;
        private readonly Dictionary<IModule, Func<TEvent, UniTask>> _handlers = new();

        public AsyncHookBinding(IBroadcaster broadcaster, Func<THook, TEvent, UniTask> call)
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

            Func<TEvent, UniTask> handler = message =>
            {
                return _handlers.ContainsKey(module) ? _call(hook, message) : UniTask.CompletedTask;
            };

            _handlers.Add(module, handler);
            _broadcaster.SubscribeAsync(handler);
        }

        public void Detach(IModule module)
        {
            if (!_handlers.Remove(module, out var handler))
            {
                return;
            }

            _broadcaster.UnsubscribeAsync(handler);
        }
    }
}
