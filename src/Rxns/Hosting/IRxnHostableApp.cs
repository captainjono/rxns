using System;
using System.Reactive;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns.Hosting
{
    public interface IRxnHostableApp : IRxnApp
    {
        IRxnAppInfo AppInfo { get; }
        IAppContainer Container { get; }
        IResolveTypes Resolver { get; }
        string AppPath { get; set; }
        void Use(IAppContainer container);
    }
}
