using System;
using System.Collections.Generic;
using Core.Infra.Messaging;
using Core.Infra.Module;
using Core.Input;
using Core.Input.TextInput;
using Core.Resources;
using Core.Services.Asset;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Core.UI.Windows
{
    public sealed class WindowsService : IDisposable
    {
        private const int EnvironmentOrder = 1;

        private readonly IAssetService _assets;
        private readonly Dictionary<(IInternalModule, WindowBase), WindowBase> _instances = new();
        private readonly Stack<WindowBase> _windows = new();
        private bool _disposed;

#if UNITY_EDITOR
        public static Func<ITextInputProvider> EditorTextInputProviderFactory { private get; set; }
#endif

        public LocalInputService Input { get; }
        public TextInputService TextInput { get; } = new();
        public IBroadcaster Broadcaster { get; } = new Broadcaster();
        public WindowBase CurrentWindow { get; private set; }

        public WindowsService(IAssetService assets, LocalInputService input)
        {
            _assets = assets;
            Input = input;
            TextInput.Register(new UnityTouchKeyboardProvider(), -100);
#if UNITY_EDITOR
            var editorProvider = EditorTextInputProviderFactory?.Invoke();
            if (editorProvider != null)
            {
                TextInput.Register(editorProvider, -1000);
            }
#endif
            TextInput.Broadcaster.Subscribe<TextInputSessionChanged>(OnTextInputChanged);
            TextInput.Broadcaster.Subscribe<TextInputProviderFailed>(OnTextInputProviderFailed);
        }

        public WindowBase Open<T>(IInternalModule module, WindowBase<T> prefab, Transform anchor, T model,
            bool isDisplacing = true)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WindowsService));
            }

            var instance = GetOrCreate(module, prefab, anchor);
            if (isDisplacing)
            {
                CloseAll();
            }
            else
            {
                RemoveFromStack(window => window == instance);
            }

            instance.SetOrder(_windows.Count + EnvironmentOrder);
            instance.Open(model);
            _windows.Push(instance);
            PublishCurrentWindow();
            return instance;
        }

        public void Close()
        {
            if (_windows.TryPop(out var window))
            {
                try
                {
                    CloseWindow(window);
                }
                finally
                {
                    PublishCurrentWindow();
                }
            }
        }

        public void Close(WindowBase window)
        {
            RemoveFromStack(candidate => ReferenceEquals(candidate, window));
        }

        public void CloseAll()
        {
            RemoveFromStack(_ => true);
        }

        public void CloseViews(IInternalModule module)
        {
            RemoveFromStack(window => window && window.Owner == module);
        }

        public void ReleaseViews(IInternalModule module)
        {
            using var cleanup = new ResourceScope();
            QueueWindowDestruction(cleanup, owner => owner == module);
            cleanup.Own(new ActionLease(() => CloseViews(module)));
        }

        public void HandleCancel()
        {
            if (!Input.WasPressed(GameInputAction.Cancel))
            {
                return;
            }

            if (TextInput.IsOpen)
            {
                TextInput.Cancel();
                return;
            }

            CurrentWindow?.Cancel();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            TextInput.Broadcaster.Unsubscribe<TextInputSessionChanged>(OnTextInputChanged);
            TextInput.Broadcaster.Unsubscribe<TextInputProviderFailed>(OnTextInputProviderFailed);
            using var cleanup = new ResourceScope();
            QueueWindowDestruction(cleanup, _ => true);
            cleanup.Own(new ActionLease(CloseAll));
            cleanup.Own(TextInput);
        }

        private void QueueWindowDestruction(ResourceScope cleanup, Predicate<IInternalModule> matches)
        {
            foreach (var (key, instance) in _instances)
            {
                if (matches(key.Item1))
                {
                    cleanup.Own(new ActionLease(() =>
                    {
                        _instances.Remove(key);
                        DestroyWindow(instance);
                    }));
                }
            }
        }

        private WindowBase GetOrCreate(IInternalModule module, WindowBase prefab, Transform anchor)
        {
            var key = (module, prefab);
            if (_instances.TryGetValue(key, out var instance) && instance)
            {
                return instance;
            }

            instance = Object.Instantiate(prefab, anchor);
            try
            {
                instance.gameObject.SetActive(false);
                instance.Input = Input;
                instance.CloseRequested = () =>
                {
                    if (CurrentWindow == instance)
                    {
                        Close();
                    }
                };
                foreach (var field in instance.GetComponentsInChildren<GamepadInputField>(true))
                {
                    field.Configure(TextInput, Input);
                }

                instance.Init(module, _assets);
                _instances[key] = instance;
                return instance;
            }
            catch
            {
                DestroyWindow(instance);
                throw;
            }
        }

        private void RemoveFromStack(Predicate<WindowBase> shouldClose)
        {
            var retained = new Stack<WindowBase>();
            List<Exception> errors = null;
            try
            {
                while (_windows.TryPop(out var window))
                {
                    if (shouldClose(window))
                    {
                        try
                        {
                            CloseWindow(window);
                        }
                        catch (Exception error)
                        {
                            (errors ??= new()).Add(error);
                        }
                    }
                    else
                    {
                        retained.Push(window);
                    }
                }
            }
            finally
            {
                while (retained.TryPop(out var window))
                {
                    _windows.Push(window);
                }

                try
                {
                    var order = EnvironmentOrder + _windows.Count - 1;
                    foreach (var window in _windows)
                    {
                        if (window)
                        {
                            window.SetOrder(order);
                        }

                        order--;
                    }
                }
                finally
                {
                    PublishCurrentWindow();
                }
            }

            if (errors != null)
            {
                throw new AggregateException(errors).Flatten();
            }
        }

        private void CloseWindow(WindowBase window)
        {
            if (window)
            {
                window.Close();
                Broadcaster.Broadcast(new WindowClosed(window));
            }
        }

        private static void DestroyWindow(WindowBase window)
        {
            if (window)
            {
                try
                {
                    window.Dispose();
                }
                finally
                {
                    Object.Destroy(window.gameObject);
                }
            }
        }

        private void OnTextInputChanged(TextInputSessionChanged _) => Input.ClearTransientInput();

        private static void OnTextInputProviderFailed(TextInputProviderFailed message) =>
            Debug.LogException(message.Error);

        private void PublishCurrentWindow()
        {
            var previous = CurrentWindow;
            CurrentWindow = _windows.Count > 0 ? _windows.Peek() : null;
            if (ReferenceEquals(previous, CurrentWindow))
            {
                return;
            }

            Input.ClearTransientInput();
            Broadcaster.Broadcast(new CurrentWindowChanged(previous, CurrentWindow));
        }
    }
}
