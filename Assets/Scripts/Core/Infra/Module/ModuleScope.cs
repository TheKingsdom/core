using System;
using System.Collections.Generic;
using Core.Infra.Logging;
using Core.Infra.Messaging;
using Cysharp.Threading.Tasks;

namespace Core.Infra.Module
{
    public sealed class ModuleScope : IDisposable
    {
        private readonly IBroadcaster _broadcaster;
        private readonly List<IModule> _modules = new();
        private readonly List<IHookBinding> _bindings = new();
        private bool _started;

        public ModuleScope(IBroadcaster broadcaster)
        {
            _broadcaster = broadcaster;
        }

        public T Add<T>(T module) where T : class, IModule
        {
            _modules.Add(module);

            if (_started)
            {
                module.Start();
                AttachHooks(module);
            }

            return module;
        }

        public void Bind<TEvent, THook>(Action<THook, TEvent> call)
            where TEvent : Event
            where THook : class
        {
            AddBinding(new HookBinding<TEvent, THook>(_broadcaster, call));
        }

        public void BindAsync<TEvent, THook>(Func<THook, TEvent, UniTask> call)
            where TEvent : AsyncEvent
            where THook : class
        {
            AddBinding(new AsyncHookBinding<TEvent, THook>(_broadcaster, call));
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            foreach (var module in _modules)
            {
                module.Start();
            }

            foreach (var module in _modules)
            {
                AttachHooks(module);
            }

            _started = true;
        }

        public void Dispose()
        {
            for (var i = _modules.Count - 1; i >= 0; i--)
            {
                DetachHooks(_modules[i]);

                try
                {
                    _modules[i].Dispose();
                }
                catch (Exception e)
                {
                    Log.Exception(e);
                }
            }

            _modules.Clear();
            _bindings.Clear();
            _started = false;
        }

        private void AddBinding(IHookBinding binding)
        {
            _bindings.Add(binding);

            if (!_started)
            {
                return;
            }

            foreach (var module in _modules)
            {
                binding.Attach(module);
            }
        }

        private void AttachHooks(IModule module)
        {
            foreach (var binding in _bindings)
            {
                binding.Attach(module);
            }
        }

        private void DetachHooks(IModule module)
        {
            foreach (var binding in _bindings)
            {
                binding.Detach(module);
            }
        }
    }
}
