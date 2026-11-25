using Cysharp.Threading.Tasks;

namespace Core.Services.Data
{
    public interface IRecordService
    {
        UniTask CreateAll();
        T GetRecord<T>() where T : BaseRecord;
    }
}