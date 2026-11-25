namespace Core.Infra.Module
{
    internal interface IHookBinding
    {
        void Attach(IModule module);
        void Detach(IModule module);
    }
}
