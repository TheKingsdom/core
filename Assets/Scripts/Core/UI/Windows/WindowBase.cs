using System;
using Core.Infra.Lifetime;
using Core.Infra.Module;
using Core.Infra.View;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Core.UI.Windows
{
    public abstract class WindowBase<TModule> : ViewBase<TModule> where TModule : class, IInternalModule
    {
        [SerializeField] private Canvas _canvas;
        [SerializeField] private SafeArea _safeArea;

        private GameObject _lastSelected;
        private bool _open;
        private bool _disposed;

        public void SetOrder(int order)
        {
            if (!_canvas)
            {
                _canvas = GetComponent<Canvas>();
            }

            _canvas.overrideSorting = true;
            _canvas.sortingOrder = order;
        }

        public void Open()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            Close();
            _open = true;
            try
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                OnOpen();
                gameObject.SetActive(true);
                if (_safeArea)
                {
                    _safeArea.Fit();
                }

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
            if (!_open)
            {
                return;
            }

            _open = false;
            try
            {
                var eventSystem = EventSystem.current;
                var selected = eventSystem ? eventSystem.currentSelectedGameObject : null;
                if (selected && selected.transform.IsChildOf(transform))
                {
                    _lastSelected = selected;
                }

                OnClose();
            }
            finally
            {
                gameObject.SetActive(false);
            }
        }

        protected override void ManagedDispose()
        {
            _disposed = true;
            _lastSelected = null;
        }

        public virtual void Cancel()
        {
        }

        protected virtual void OnOpen()
        {
        }

        protected virtual void OnClose()
        {
        }
    }
}
