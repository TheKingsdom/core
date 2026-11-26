using System.Threading;
using Core.Networking.Core;
using Cysharp.Threading.Tasks;
using Fusion;

namespace Core.Networking.Fusion
{
    public interface IRunnerScopeInstaller
    {
        void Install(NetworkRunner runner, RunnerGeneration generation);
        UniTask PrepareRoomAsync(NetworkRunner runner, RunnerGeneration generation, CreateSessionRequest creation,
            CancellationToken cancellationToken);
        UniTask PrepareLeaveAsync(NetworkRunner runner, RunnerGeneration generation);
        void Release(NetworkRunner runner, RunnerGeneration generation);
    }
}
