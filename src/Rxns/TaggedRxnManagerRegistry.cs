using System.Collections.Generic;
using Rxns.Interfaces;

namespace Rxns
{
    public class TaggedServiceRxnManagerRegistry : IRxnManagerRegistry
    {
        public IRxnManager<IRxn> RxnsLocal { get; private set; }
        public IRxnManager<IRxn> RxnsCentralReliable { get; private set; }
        public IRxnManager<IRxn> RxnsCentral { get; private set; }
        public IDictionary<string, IRxnManager<IRxn>> ClientRoutes { get; private set; }

        public TaggedServiceRxnManagerRegistry(IResolveTypes resolver)
        {
            RxnsCentral = resolver.ResolveTag<IRxnManager<IRxn>>("central");
            RxnsCentralReliable = resolver.ResolveTag<IRxnManager<IRxn>>("centralReliable");
            RxnsLocal = resolver.ResolveTag<IRxnManager<IRxn>>("local");
        }
    }
}
