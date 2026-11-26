using Core.Services.Data;
using UnityEngine;

namespace Game.Session
{
    [CreateAssetMenu(menuName = "Core/Coop/Session Settings", fileName = "SessionSettings")]
    public sealed class SessionRecordAsset : RecordSO
    {
        private void OnEnable()
        {
            Record ??= new SessionRecord();
        }
    }
}
