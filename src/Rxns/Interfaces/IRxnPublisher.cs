using System;

namespace Rxns.Interfaces
{
    public interface IRxnPublisher<T>
    {
        void ConfigiurePublishFunc(Action<T> eventFunc);
    }
}
