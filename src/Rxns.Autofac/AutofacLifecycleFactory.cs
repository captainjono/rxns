using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Rxns.Hosting;

namespace Rxns.Autofac
{
    public class AutofacLifecyckeFactiory : IRxnLifecycleFactory
    {
        public IRxnLifecycle Create()
        {
            return new AutofacRxnLifecycle(new ContainerBuilder());
        }
    }
}
