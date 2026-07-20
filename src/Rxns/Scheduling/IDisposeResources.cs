using System;

namespace Rxns.Scheduling
{
    public interface IDisposeResources<T> : IDisposable
    {
        T ManageDisposalOf(IDisposable resource);
    }
}
