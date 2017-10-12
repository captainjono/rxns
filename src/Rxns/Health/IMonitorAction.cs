using System;

namespace Rxns
{
    public interface IMonitorAction<T>
    {
        Func<T, bool> When { get; }
        Action<T> Do { get; }
    }
}
