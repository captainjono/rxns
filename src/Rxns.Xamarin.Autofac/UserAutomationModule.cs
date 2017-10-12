using Autofac;
using Rxns.Playback;
using Rxns.Xamarin.Features.Automation;
using Rxns.Xamarin.Features.Automation.PlayBackFilter;
using Rxns.Xamarin.Features.Automation.Recordings;

namespace Rxns.Autofac
{
    public class UserAutomationModule : Module
    {
        protected override void Load(ContainerBuilder cb)
        {
            cb.RegisterType<UserAutomationPlayer>().AsImplementedInterfaces().SingleInstance();
            cb.RegisterType<UserAutomationService>().AsSelf().AsImplementedInterfaces().SingleInstance();
            //todo: port over command service etc
            //cb.RegisterType<AssertionFilter>().AsImplementedInterfaces().InstancePerDependency();
            //cb.RegisterType<CommandInterceptorAutomator>().AsImplementedInterfaces().SingleInstance();
            //cb.RegisterType<LocalFileSystemTapeRepository>().AsImplementedInterfaces().SingleInstance();
            cb.RegisterType<RecordingsPageModel>().AsSelf().InstancePerDependency();

            cb.RegisterType<UserActionsOnlyFilter>().AsImplementedInterfaces().InstancePerDependency();
            cb.RegisterType<BasicUserAutomator>().AsImplementedInterfaces().SingleInstance();

            base.Load(cb);
        }
    }
}
