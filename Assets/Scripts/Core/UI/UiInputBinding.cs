using System;
using Core.Input;
using Core.Resources;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using Object = UnityEngine.Object;

namespace Core.UI
{
    public sealed class UiInputBinding : IDisposable
    {
        private readonly ResourceScope _resources = new();

        public UiInputBinding(InputSystemUIInputModule module, LocalInputService input)
        {
            var oldAsset = module.actionsAsset;
            var point = module.point;
            var click = module.leftClick;
            var move = module.move;
            var submit = module.submit;
            var cancel = module.cancel;

            try
            {
                var newPoint = Reference(input, GameInputAction.Point);
                var newClick = Reference(input, GameInputAction.Click);
                var newMove = Reference(input, GameInputAction.Navigate);
                var newSubmit = Reference(input, GameInputAction.Submit);
                var newCancel = Reference(input, GameInputAction.Cancel);

                _resources.Own(new ActionLease(() =>
                {
                    if (module)
                    {
                        module.actionsAsset = oldAsset;
                        module.point = point;
                        module.leftClick = click;
                        module.move = move;
                        module.submit = submit;
                        module.cancel = cancel;
                    }
                }));

                module.actionsAsset = input.Actions;
                module.point = newPoint;
                module.leftClick = newClick;
                module.move = newMove;
                module.submit = newSubmit;
                module.cancel = newCancel;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose() => _resources.Dispose();

        private InputActionReference Reference(LocalInputService input, GameInputAction command)
        {
            var reference = InputActionReference.Create(input.GetAction(command));
            _resources.Own(new ActionLease(() =>
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(reference);
                }
                else
                {
                    Object.DestroyImmediate(reference);
                }
            }));
            return reference;
        }
    }
}
