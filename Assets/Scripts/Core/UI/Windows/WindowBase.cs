using System;
using Core.Infra.Lifetime;
using Core.Infra.Module;
using Core.Infra.View;
using Core.Input;
using Core.Resources;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Core.UI.Windows
{
    public abstract class WindowBase<T> : WindowBase
    {
        protected T ActiveModel { get; private set; }
        public sealed override Type ModelType => typeof(T);

        protected sealed override void OnOpen(object model)
        {
            ActiveModel = (T)model;
            OnOpen();
        }

        protected sealed override void OnClose()
        {
            ActiveModel = default;
        }

        protected abstract void OnOpen();
    }

    public abstract class WindowBase : ViewBase<IInternalModule>
    {
        [SerializeField] private Canvas _canvas;
        [SerializeField] private SafeArea _safeArea;

        private GameObject _lastSelected;
        private ResourceScope _opening;
        private bool _disposed;

        public abstract Type ModelType { get; }
        public LocalInputService Input { get; internal set; }
        internal IInternalModule Owner => Module;
        internal Action CloseRequested { get; set; }
        protected ResourceScope Opening => _opening;

        public void SetOrder(int order)
        {
            if (!_canvas)
            {
                _canvas = GetComponent<Canvas>();
            }
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = order;
        }

        public void Open(object model)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            Close();
            _opening = new ResourceScope();
            try
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                if (_safeArea)
                {
                    _safeArea.Fit();
                }

                OnOpen(model);
                gameObject.SetActive(true);
                UiNavigation.SelectFirst(gameObject, _lastSelected);
            }
            catch
            {
                Close();
                throw;
            }
        }

        public void Close()
        {
            var opening = _opening;
            if (opening == null)
            {
                return;
            }
            _opening = null;

            var eventSystem = EventSystem.current;
            var selected = eventSystem ? eventSystem.currentSelectedGameObject : null;
            if (selected && selected.transform.IsChildOf(transform))
            {
                _lastSelected = selected;
            }

            try
            {
                opening.Dispose();
            }
            finally
            {
                try
                {
                    OnClose();
                }
                finally
                {
                    gameObject.SetActive(false);
                }
            }
        }

        protected override void ManagedDispose()
        {
            _disposed = true;
            CloseRequested = null;
            Close();
        }

        protected void CloseWindow() => CloseRequested?.Invoke();

        public virtual void Cancel()
        {
        }

        protected abstract void OnOpen(object model);
        protected abstract void OnClose();
    }
}
