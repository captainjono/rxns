using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{
    public class NestedInAppDirAppUpdateStore : CurrentDirectoryAppUpdateStore
    {
        public override IObservable<string> Run(GetAppDirectoryForAppUpdate command)
        {
            return Rxn.Create(() =>
            {
                if (command.Version.IsNullOrWhitespace() || command.Version.BasicallyContains("Latest"))
                    return Path.Combine(Directory.GetCurrentDirectory(), command.SystemName);

                return Path.Combine(Directory.GetCurrentDirectory(), command.SystemName, $"{command.SystemName}%%{command.Version}");
            });
        }
    }

    public class CurrentDirectoryAppUpdateStore : IStoreAppUpdates
    {
        public virtual IObservable<string> Run(GetAppDirectoryForAppUpdate command)
        {
            return Rxn.Create(() =>
            {
                if (command.Version.IsNullOrWhitespace() || command.Version.BasicallyContains("Latest"))
                    return Directory.GetCurrentDirectory();

                return Path.Combine(Directory.GetCurrentDirectory(), $"{command.SystemName}%%{command.Version}").AsCrossPlatformPath();
            });
        }

        public IObservable<string> Run(PrepareForAppUpdate command)
        {
            return Run(new GetAppDirectoryForAppUpdate(command.SystemName, command.Version, command.SystemRootPath)).Select(result =>
            {
                Debugger.Launch();
                var targetPath = result;
                var existingCfg = Path.Combine(targetPath, "rxn.cfg");

                if (File.Exists(existingCfg) && RxnAppCfg.LoadCfg(existingCfg).Version.Equals(command.Version))
                {
                    $"App already @ {command.Version}".LogDebug();
                    return null;
                }

                if (Directory.Exists(targetPath))
                    if (!command.OverwriteExisting & Directory.GetFiles("*.*").AnyItems())
                        return targetPath;
                    else
                        Directory.Delete(targetPath, true);

                if (!Directory.Exists(targetPath))
                    Directory.CreateDirectory(targetPath);

                return targetPath;
            });
        }

        public IObservable<string> Run(MigrateAppToVersion command)
        {
            return Rxn.Create<string>(o =>
            {
                var currentCfg = RxnAppCfg.Detect(new string[0]); //bypass commandline
                if (currentCfg.SystemName.BasicallyEquals(command.SystemName) && currentCfg.Version.BasicallyEquals(command.Version))
                {
                    "Bypassing since app is already at this version".LogDebug();
                    return string.Empty.ToObservable().Subscribe(o);
                }

                var appBinary = new FileInfo(currentCfg.AppPath).Name;

                return Run(new GetAppDirectoryForAppUpdate(command.SystemName, command.Version, command.SystemRootPath)).Select(targetDir =>
                {
                    currentCfg.AppPath = Path.Combine(targetDir, appBinary);
                    currentCfg.Version = command.Version;

                    currentCfg
                        .Save(targetDir) //update apps cfg
                        .Save(); //update the default cfg for the supervisor so it launches that new version

                    return targetDir;
                })
                .Subscribe(o);
            });
        }
    }
}
