using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rxns.Interfaces;

namespace Rxns.Cloud
{
    public class AppStatusInfoProviderEvent : IRxn
    {
        public string Component = Guid.NewGuid().ToString();
        public string ReporterName;
        public Func<object> Info;
    }
}
