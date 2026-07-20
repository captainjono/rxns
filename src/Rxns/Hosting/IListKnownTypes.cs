using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns.Hosting
{
    public interface IListKnownTypes
    {
        IEnumerable<Type> Services { get; }
    }
}
