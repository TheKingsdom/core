using Core.Services.Data;
using UnityEngine;

namespace Game.Windows
{
    [CreateAssetMenu(menuName = "Core/Coop/Windows Settings", fileName = "WindowsSettings")]
    public sealed class WindowsRecordAsset : RecordSO
    {
        private void OnEnable()
        {
            Record ??= new WindowsRecord();
        }
    }
}
