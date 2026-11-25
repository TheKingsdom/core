using System;
using Core.Resources;
using Core.UI.Windows;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Session
{
    public sealed class LoadingWindow : WindowBase<Action>
    {
        [SerializeField] private Button _cancel;

        protected override void OnOpen()
        {
            if (_cancel)
            {
                _cancel.onClick.AddListener(Cancel);
                Opening.Own(new ActionLease(() =>
                {
                    if (_cancel)
                    {
                        _cancel.onClick.RemoveListener(Cancel);
                    }
                }));
            }
        }

        public override void Cancel() => ActiveModel?.Invoke();
    }
}
