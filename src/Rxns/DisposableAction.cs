using System;

namespace Rxns
{
    public class DisposableAction : IDisposable
    {
        private readonly Action _action;
        private bool _hasDisposed = false;

        public DisposableAction(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            if (!_hasDisposed)
            {
                _action.Invoke();
                _hasDisposed = true;
            }
        }
    }
}
