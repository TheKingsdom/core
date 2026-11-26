using System;
using System.Collections.Generic;
using Core.Infra.Module;

namespace Core.UI.Windows
{
    public sealed class WindowStack<TModule> where TModule : class, IInternalModule
    {
        private readonly List<WindowBase<TModule>> _windows = new();
        private readonly Action _onCurrentWindowChanged;

        public WindowBase<TModule> CurrentWindow { get; private set; }

        public WindowStack(Action onCurrentWindowChanged)
        {
            _onCurrentWindowChanged = onCurrentWindowChanged;
        }

        public void Open(WindowBase<TModule> window, bool isDisplacing)
        {
            if (isDisplacing)
            {
                CloseAll();
            }
            else
            {
                Close(window);
            }

            window.SetOrder(_windows.Count + 1);
            window.Open();
            _windows.Add(window);
            Refresh();
        }

        public void Close(WindowBase<TModule> window)
        {
            if (!_windows.Remove(window))
            {
                return;
            }

            try
            {
                if (window)
                {
                    window.Close();
                }
            }
            finally
            {
                Refresh();
            }
        }

        public void CloseAll()
        {
            List<Exception> errors = null;
            try
            {
                while (_windows.Count > 0)
                {
                    var window = _windows[^1];
                    _windows.RemoveAt(_windows.Count - 1);
                    try
                    {
                        if (window)
                        {
                            window.Close();
                        }
                    }
                    catch (Exception error)
                    {
                        (errors ??= new()).Add(error);
                    }
                }
            }
            finally
            {
                Refresh();
            }

            if (errors != null)
            {
                throw new AggregateException(errors).Flatten();
            }
        }

        private void Refresh()
        {
            _windows.RemoveAll(window => !window);
            var previous = CurrentWindow;
            CurrentWindow = _windows.Count > 0 ? _windows[^1] : null;

            try
            {
                for (var index = 0; index < _windows.Count; index++)
                {
                    _windows[index].SetOrder(index + 1);
                }
            }
            finally
            {
                if (!ReferenceEquals(previous, CurrentWindow))
                {
                    _onCurrentWindowChanged?.Invoke();
                }
            }
        }
    }
}
