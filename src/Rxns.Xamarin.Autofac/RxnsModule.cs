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
            RxnExtensions.OnReactionScheduler = RxnAppCfg.BackgroundScheduler;
            RxnExtensions.UIScheduler = RxnAppCfg.UIScheduler;
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
