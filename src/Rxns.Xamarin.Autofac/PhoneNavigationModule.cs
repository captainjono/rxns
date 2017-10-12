using Autofac;
using Rxns.Xamarin.Features.Navigation;

namespace Rxns.Autofac
{
    public class PhoneNavigationModule : Module
    {
        protected override void Load(ContainerBuilder cb)
        {
            cb.RegisterType<PhoneNavigationOrchestrator>().AsImplementedInterfaces().SingleInstance();
            cb.RegisterType<NavigationEventGenerator>().AsImplementedInterfaces().SingleInstance();
            cb.RegisterType<AutoFacPageResolver>().AsImplementedInterfaces().SingleInstance();
            cb.RegisterType<RxnAppNavigator>().AsImplementedInterfaces().SingleInstance();

            base.Load(cb);
        }
    }
}
