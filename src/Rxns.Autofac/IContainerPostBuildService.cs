using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;

namespace Rxns.Interfaces
{
    public interface IContainerPostBuildService
    {
        void Run(IReportStatus logger, IContainer container);
    }
}
