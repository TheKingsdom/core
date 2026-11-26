using System.Threading;
using Core.Infra.Module;
using Core.Networking.Core;
using Cysharp.Threading.Tasks;

namespace Game.Session
{
    public interface ISessionModule : IModule
    {
        UniTask<OperationResult> CreateAsync(CreateSessionRequest request, CancellationToken token = default);
        UniTask<OperationResult> JoinAsync(SessionLocator target, CancellationToken token = default);
        UniTask<OperationResult> JoinRandomAsync(CancellationToken token = default);
        UniTask<OperationResult> ReconnectAsync(CancellationToken token = default);
        UniTask<OperationResult> LeaveAsync(LeaveMode mode = LeaveMode.ForgetSession);
    }
}
