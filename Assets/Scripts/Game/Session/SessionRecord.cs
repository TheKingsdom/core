using System;
using Core.Services.Data;
using UnityEngine;

namespace Game.Session
{
    [Serializable]
    public sealed class SessionRecord : BaseRecord
    {
        [SerializeField] private string _scenePath;
        [SerializeField] private string _gameViewId;
        [SerializeField] private string _startupRoom;
        [SerializeField] private string _startupRegion;
        [SerializeField] private bool _resumeOnStartup;

        public override string Name => nameof(SessionRecord);
        public string ScenePath => _scenePath;
        public string GameViewId => _gameViewId;
        public string StartupRoom => _startupRoom;
        public string StartupRegion => _startupRegion;
        public bool ResumeOnStartup => _resumeOnStartup;
    }
}
