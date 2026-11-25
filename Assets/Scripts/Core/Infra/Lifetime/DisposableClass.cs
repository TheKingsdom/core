using System;
using Core.Resources;

namespace Core.Infra.Lifetime
{
    public class DisposableClass : IDisposable
    {
        protected ResourceScope Disposables;

        public IDisposable Init()
        {
            Disposables = new ResourceScope();

            OnInit();

            return Disposables;
        }

        protected virtual void OnInit()
        {
        }

        protected virtual void OnDisposed()
        {
        }

        public void Dispose()
        {
            OnDisposed();
            Disposables.Dispose();
        }
    }
}
