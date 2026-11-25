using Core.Infra.View;
using Core.Networking.Fusion;
using Core.Networking.Players;
using UnityEngine.SceneManagement;

namespace Game.Session
{
    public class GameSessionView : ViewBase<SessionModule>
    {
        public SessionViewData Data { get; internal set; }
    }

    public sealed class SessionViewData
    {
        public Scene Scene { get; }
        public PlayerAvatar LocalPlayer { get; }
        public NetworkSnapshot Connection { get; }

        public SessionViewData(Scene scene, PlayerAvatar localPlayer, NetworkSnapshot connection)
        {
            Scene = scene;
            LocalPlayer = localPlayer;
            Connection = connection;
        }
    }
}
