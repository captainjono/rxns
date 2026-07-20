using System;
using System.Reactive;

namespace Rxns.Collections
{
    public interface IAbstractFile
    {
        IObservable<string> Read();
        IObservable<Unit> Write(string contents);
        bool Exists { get; }
    }
}
