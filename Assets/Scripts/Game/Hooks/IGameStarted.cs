using Cysharp.Threading.Tasks;

namespace Game.Hooks
{
    public interface IGameStarted
    {
        UniTask OnGameStarted();
    }
}
