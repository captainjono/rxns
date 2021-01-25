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
        string AppPath { get; }
        //Sets the rxn app to the version specified
        IObservable<Unit> MigrateTo(string systemName, string version);
        /// <summary>
        /// gets the directory to extract the new version too
        /// </summary>
        /// <param name="version"></param>
        /// <returns></returns>
        string GetDirectoryForVersion(string version);
    }
}
