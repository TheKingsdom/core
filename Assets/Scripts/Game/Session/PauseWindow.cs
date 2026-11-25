using System;
using Core.UI.Windows;

namespace Game.Session
{
    public sealed class PauseWindow : WindowBase<PauseWindow.Model>
    {
        public sealed class Model
        {
            public Action Leave { get; }
            public Action Disconnect { get; }

            public Model(Action leave, Action disconnect)
            {
                Leave = leave;
                Disconnect = disconnect;
            }
        }

        public override void Cancel() => CloseWindow();
        public void Leave() => ActiveModel?.Leave();
        public void Disconnect() => ActiveModel?.Disconnect();

        protected override void OnOpen()
        {
        }
    }
}
