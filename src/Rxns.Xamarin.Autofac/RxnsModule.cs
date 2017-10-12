using System.Reactive.Concurrency;
using System.Threading.Tasks;
using Autofac;
using Rxns.Logging;

namespace Rxns.Autofac
{
    public class RxnsModule : Module
    {
        protected override void Load(ContainerBuilder cb)
        {
            RxnExtensions.OnReactionScheduler = RxnApp.BackgroundScheduler;
            RxnExtensions.UIScheduler = RxnApp.UIScheduler;
            RxnExtensions.UntilScheduler = Scheduler.Immediate;

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                GeneralLogging.Log.OnError(args.Exception);
                args.SetObserved();
            };

            cb.RegisterModule<PhoneNavigationModule>();

            base.Load(cb);
        }
    }

}
