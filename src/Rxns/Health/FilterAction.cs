using System;
using System.Collections.Generic;

namespace Rxns
{
    public class FilterAction<T>
    {
        public Func<T, bool> When { get; set; }
        public Func<T, IEnumerable<T>> Do { get; set; }
    }
}
