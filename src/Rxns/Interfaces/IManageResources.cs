using System;

namespace Rxns.Interfaces
{
    public interface IManageResources : IDisposable
    {
        /// <summary>
        /// Release this resource during this objects disposal
        /// </summary>
        /// <param name="obj"></param>
        void OnDispose(IDisposable obj);
    }
}
