using Core.UI.Windows;

namespace Game.Windows
{
    public sealed class PauseWindow : WindowBase<IWindowsModuleInternal>
    {
        public override void Cancel() => Module.ClosePause();
        public void Leave() => Module.LeaveRoom();
        public void Disconnect() => Module.Disconnect();
    }
}
