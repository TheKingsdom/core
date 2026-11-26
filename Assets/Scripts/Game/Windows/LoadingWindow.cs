using Core.UI.Windows;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Windows
{
    public sealed class LoadingWindow : WindowBase<IWindowsModuleInternal>
    {
        [SerializeField] private Button _cancel;

        protected override void OnOpen()
        {
            if (_cancel)
            {
                _cancel.onClick.AddListener(Cancel);
            }
        }

        protected override void OnClose()
        {
            RemoveCancelListener();
        }

        protected override void ManagedDispose()
        {
            RemoveCancelListener();
            base.ManagedDispose();
        }

        public override void Cancel() => Module.CancelLoading();

        private void RemoveCancelListener()
        {
            if (_cancel)
            {
                _cancel.onClick.RemoveListener(Cancel);
            }
        }
    }
}
