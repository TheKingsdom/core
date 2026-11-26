using Core.Infra.Module;
using Core.Networking.Core;
using Cysharp.Threading.Tasks;

namespace Game.Session
{
    public interface ISessionModuleInternal : IInternalModule
    {
        UniTask<OperationResult> LeaveAsync(LeaveMode mode = LeaveMode.ForgetSession);
    }
}
