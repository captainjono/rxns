using System.Collections.Generic;
using Autofac;
using Rxns.Interfaces;

namespace Rxns.Autofac
{
    public class AutofacNamedEventManagerRegistry : IRxnManagerRegistry
    {
        public IRxnManager<IRxn> RxnsLocal { get; private set; }
        public IRxnManager<IRxn> RxnsCentralReliable { get; private set; }
        public IRxnManager<IRxn> RxnsCentral { get; private set; }
        public IDictionary<string, IRxnManager<IRxn>> ClientRoutes { get; private set; }

        public AutofacNamedEventManagerRegistry(IComponentContext resolver)
        {
            RxnsCentral = resolver.ResolveNamed<IRxnManager<IRxn>>("central");
            RxnsCentralReliable = resolver.ResolveNamed<IRxnManager<IRxn>>("centralReliable");
            RxnsLocal = resolver.ResolveNamed<IRxnManager<IRxn>>("local");
        }
    }
}
