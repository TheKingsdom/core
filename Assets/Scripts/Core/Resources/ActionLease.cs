using System;

namespace Core.Resources
{
    public sealed class ActionLease : IDisposable
    {
        private Action _release;

        public ActionLease(Action release)
        {
            _release = release ?? throw new ArgumentNullException(nameof(release));
        }

        public void Dispose()
        {
            var release = _release;
            _release = null;
            release?.Invoke();
        }
    }
}
