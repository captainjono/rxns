using System.Collections.Generic;
using Rxns.Hosting;

namespace Rxns.Redis
{
    public class RedisModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle.CreatesOncePerApp<RedisCacheFactory>()
                .CreateGenericOncePerAppAs(typeof(RedisDictionary<,>), typeof(IDictionary<,>));
        }
    }
}
