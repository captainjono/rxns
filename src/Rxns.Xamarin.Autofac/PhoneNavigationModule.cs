using Autofac;
using Rxns.Hosting;
using Rxns.Xamarin.Features.Navigation;

namespace Rxns.Autofac
{
    public class PhoneNavigationModule : Module
    {
        protected override void Load(ContainerBuilder cb)
        {
            new AutofacRxnLifecycle(cb)
                .CreatesOncePerApp<PhoneNavigationOrchestrator>()
                .CreatesOncePerApp<NavigationEventGenerator>()
                .CreatesOncePerApp<AutoFacPageResolver>()
                .CreatesOncePerApp<RxnAppNavigator>();

            base.Load(cb);
        }
    }
}
