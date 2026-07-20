using System;

namespace Rxns.Hosting.Updates
{
    /// <summary>
    /// This interface should evolve into a place where
    /// updates "streams" are stored and retrieved from. currently only
    /// supporting "strings". But could be cloud etc and that should probably be an interface also
    /// dont want to make it too complicated, not a full blown CMS
    /// </summary>
    public interface IStoreAppUpdates
    {
        IObservable<string> Run(GetAppDirectoryForAppUpdate command);
        IObservable<string> Run(PrepareForAppUpdate command);
        IObservable<string> Run(MigrateAppToVersion command);

    }
}
