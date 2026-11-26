using Core.Infra.Module;

namespace Game.Windows
{
    public interface IWindowsModuleInternal : IInternalModule
    {
        void CancelLoading();
        void ClosePause();
        void LeaveRoom();
        void Disconnect();
    }
}
