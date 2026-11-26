using System;
using System.Threading;
using Core.Infra.Module;
using Cysharp.Threading.Tasks;

namespace Game.Windows
{
    public interface IWindowsModule : IModule
    {
        bool HasOpenWindow { get; }
        UniTask OpenLoadingAsync(Action cancel, CancellationToken token);
        void CloseLoading();
        UniTask OpenPauseAsync(Action leave, Action disconnect, CancellationToken token);
        void ClosePause();
    }
}
