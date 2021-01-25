using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Rxns.Hosting;
using Rxns.Interfaces;

namespace Rxns.Scheduling
{
    /// <summary>
    /// This module contains a list of dependencies used to power the scheduling sub-system
    /// </summary>
    public class SchedulingDependencyModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle cb)
        {

            cb.CreatesOncePerRequest<RxScheduler>();
            cb.CreatesOncePerApp<AdaptiveScheduler>();

            //register inbuilt tasks
            cb.CreatesOncePerRequest<SchedulableTaskGroup>();
            cb.CreatesOncePerRequest<SchedulableTask>();
            cb.CreatesOncePerRequest<CmdTask>();
            cb.CreatesOncePerRequest<FileCopyTask>();

            cb.CreatesOncePerRequest<IObservable<ITaskProvider>>(c =>
            {
                return c.Resolve<ITaskProvider[]>().ToObservable(Scheduler.Immediate);
            });

            cb.CreatesOncePerApp<ITaskProvider>(_ => new FileSystemTaskProvider(_.Resolve<IFileSystemService>(), "tasks.json"));
            
            return cb;
        }
    }
}
