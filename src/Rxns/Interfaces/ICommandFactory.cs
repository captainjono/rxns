using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns.Interfaces
{
    public interface ICommandFactory
    {
        dynamic FromString(string serialisedCmd);
    }
}
