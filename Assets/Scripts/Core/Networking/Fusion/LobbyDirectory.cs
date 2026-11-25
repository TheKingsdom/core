using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Core.Networking.Core;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using Core.Infra.Messaging;
using UnityEngine;
using System.Threading;

namespace Core.Networking.Fusion
{
    public sealed class LobbyDirectory
    {
        public const string ConfirmedPlayersPropertyKey = "confirmedPlayers";
        private RegionInfo[] _regions = Array.Empty<RegionInfo>();

        public IBroadcaster Broadcaster { get; } = new Broadcaster();
        public ReadOnlyCollection<SessionData> Sessions { get; private set; } =
            Array.AsReadOnly(Array.Empty<SessionData>());
        public RunnerGeneration Generation { get; private set; }
        public string Region { get; private set; }

        internal void Reset(RunnerGeneration generation)
        {
            Generation = generation;
            Region = null;
            var array = Array.Empty<SessionData>();
            Sessions = Array.AsReadOnly(array);
            Broadcaster.Broadcast(new LobbySessionsChanged(Sessions));
        }

        internal void Update(RunnerGeneration generation, string region, IEnumerable<SessionInfo> sessions)
        {
            if (generation != Generation)
            {
                return;
            }

            Region = region;
            var array = sessions.Where(session => session.IsValid).Select(session =>
            {
                var name = session.Name;
                var playerCount =
                    session.Properties.TryGetValue(ConfirmedPlayersPropertyKey, out var confirmed) && confirmed.IsInt
                        ? (int)confirmed
                        : session.PlayerCount;
                var maxPlayers = session.MaxPlayers;
                return new SessionData(name, playerCount, maxPlayers, region, session.IsOpen, session.IsVisible);
            }).ToArray();
            Sessions = Array.AsReadOnly(array);
            Broadcaster.Broadcast(new LobbySessionsChanged(Sessions));
        }

        internal async UniTask FetchRegionsAsync(CancellationToken token)
        {
            try
            {
                _regions = (await NetworkRunner.GetAvailableRegions(cancellationToken: token).AsUniTask()).ToArray();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[LobbyDirectory] Failed to fetch regions: {error.Message}");
            }
        }

        public int GetRegionPing(string region)
        {
            if (string.IsNullOrEmpty(region))
            {
                return -1;
            }

            foreach (var item in _regions)
            {
                if (item.RegionCode == region)
                {
                    return item.RegionPing;
                }
            }

            return -1;
        }

        internal string BestRegion =>
            _regions.Length == 0 ? "" : _regions.OrderBy(region => region.RegionPing).First().RegionCode;
    }
}
