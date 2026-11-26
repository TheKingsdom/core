using Core.Infra.Messaging;
using Core.Networking.Core;

namespace Core.Networking.Players
{
    public sealed class PlayerAvatarChanged : Event
    {
        public AvatarChangeKind Kind { get; }
        public PlayerAvatar Avatar { get; }

        public PlayerAvatarChanged(AvatarChangeKind kind, PlayerAvatar avatar)
        {
            Kind = kind;
            Avatar = avatar;
        }
    }
}
