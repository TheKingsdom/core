using System;
using System.Collections.Generic;
using Core.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using Object = UnityEngine.Object;

namespace Core.UI
{
    public sealed class UiInputBinding : IDisposable
    {
        private readonly InputSystemUIInputModule _module;
        private readonly InputActionAsset _previousActions;
        private readonly InputActionReference _previousPoint;
        private readonly InputActionReference _previousClick;
        private readonly InputActionReference _previousMove;
        private readonly InputActionReference _previousSubmit;
        private readonly InputActionReference _previousCancel;
        private readonly List<InputActionReference> _references = new();
        private bool _disposed;

        public UiInputBinding(InputSystemUIInputModule module, LocalInputService input)
        {
            _module = module;
            _previousActions = module.actionsAsset;
            _previousPoint = module.point;
            _previousClick = module.leftClick;
            _previousMove = module.move;
            _previousSubmit = module.submit;
            _previousCancel = module.cancel;

            try
            {
                module.actionsAsset = input.Actions;
                module.point = Reference(input, GameInputAction.Point);
                module.leftClick = Reference(input, GameInputAction.Click);
                module.move = Reference(input, GameInputAction.Navigate);
                module.submit = Reference(input, GameInputAction.Submit);
                module.cancel = Reference(input, GameInputAction.Cancel);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (_module)
                {
                    _module.actionsAsset = _previousActions;
                    _module.point = _previousPoint;
                    _module.leftClick = _previousClick;
                    _module.move = _previousMove;
                    _module.submit = _previousSubmit;
                    _module.cancel = _previousCancel;
                }
            }
            finally
            {
                foreach (var reference in _references)
                {
                    if (Application.isPlaying)
                    {
                        Object.Destroy(reference);
                    }
                    else
                    {
                        Object.DestroyImmediate(reference);
                    }
                }
                _references.Clear();
            }
        }

        private InputActionReference Reference(LocalInputService input, GameInputAction command)
        {
            var reference = InputActionReference.Create(input.GetAction(command));
            _references.Add(reference);
            return reference;
        }
    }
}
