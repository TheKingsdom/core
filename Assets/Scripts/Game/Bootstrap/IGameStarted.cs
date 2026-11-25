using Cysharp.Threading.Tasks;

namespace Game.Bootstrap
{
    public interface IGameStarted
    {
        UniTask OnGameStarted();
    }
}
