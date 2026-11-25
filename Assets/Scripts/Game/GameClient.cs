using Core.Infra.Bootstrap;
using Core.Infra.Logging;
using Cysharp.Threading.Tasks;
using Game.Hooks;

namespace Game
{
    public class GameClient : Client
    {
        protected override void CreateModules()
        {
            base.CreateModules();

            Modules.BindAsync<GameStarted, IGameStarted>((module, _) => module.OnGameStarted());
        }

        protected override async UniTask StartGame()
        {
            await base.StartGame();

            Log.Info("Game started");
            await Broadcaster.BroadcastAsync(new GameStarted());
        }
    }
}
