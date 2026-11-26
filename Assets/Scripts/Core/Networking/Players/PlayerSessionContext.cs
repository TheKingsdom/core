using Core.Networking.Core;
using Fusion;
using UnityEngine;

namespace Core.Networking.Players
{
    public sealed class PlayerSessionContext : MonoBehaviour
    {
        public PlayerService Players { get; private set; }
        public LocalPlayerBootstrapper Bootstrap { get; private set; }
        public PlayerSpawnCoordinator Spawning { get; private set; }
        public PlayerSessionDirector Director { get; set; }
        public RunnerGeneration Generation { get; private set; }

        internal void Release()
        {
            try
            {
                Bootstrap?.Dispose();
            }
            finally
            {
                try
                {
                    Spawning?.Dispose();
                }
                finally
                {
                    Players?.Dispose();
                }
            }
        }

        public void Configure(PlayerService players, RunnerGeneration generation)
        {
            Players = players;
            Generation = generation;
            Bootstrap = new LocalPlayerBootstrapper();
            Spawning = new PlayerSpawnCoordinator();
        }
    }
}
