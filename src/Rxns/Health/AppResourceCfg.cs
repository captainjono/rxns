using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rxns.Health;

namespace Rxns.Hosting
{
    public class AppResourceCfg : ISystemResourceConfiguration
    {
        public int ThreadPoolSize { get; set; }
    }
}
