using System.Collections.Generic;
using Core.Infra.Messaging;

namespace Core.Networking.Fusion
{
    public sealed class LobbySessionsChanged : Event
    {
        public IReadOnlyList<SessionData> Sessions { get; }

        public LobbySessionsChanged(IReadOnlyList<SessionData> sessions)
        {
            Sessions = sessions;
        }
    }
}
