using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Rxns.Health;

namespace Janison.MicroApp
{
    public class WindowsModule : Module
    {
        protected override void Load(ContainerBuilder cb)
        {
            cb.RegisterType<WindowsFileSystemConfiguration>().AsImplementedInterfaces().SingleInstance(); //StaticFileSystemConfiguration
            cb.RegisterType<WindowsFileSystemService>().AsImplementedInterfaces().SingleInstance();
            cb.RegisterType<WindowsSystemInformationService>().AsImplementedInterfaces().SingleInstance();
            cb.RegisterType<WindowsSystemServices>().AsImplementedInterfaces().SingleInstance();

            base.Load(cb);
        }

        
    }
}
