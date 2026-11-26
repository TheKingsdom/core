using System;
using Core.Services.Data;
using UnityEngine;

namespace Game.Windows
{
    [Serializable]
    public sealed class WindowsRecord : BaseRecord
    {
        [SerializeField] private string _loadingWindowId;
        [SerializeField] private string _pauseWindowId;

        public override string Name => nameof(WindowsRecord);
        public string LoadingWindowId => _loadingWindowId;
        public string PauseWindowId => _pauseWindowId;
    }
}
