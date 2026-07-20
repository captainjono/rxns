using System;
using System.Collections.Generic;
using System.Reactive;

namespace Rxns.Scheduling
{
    public interface ISqlTask : IDisposable
    {
        IObservable<Unit> Execute(IEnumerable<string> scripts, string connectionString = null, string database = null);
    }
}
