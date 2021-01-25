using System;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    public class ResolverCommandFactory : ICommandFactory
    {
        private readonly IResolveTypes _typeResolver;

        public ResolverCommandFactory(IResolveTypes typeResolver)
        {
            _typeResolver = typeResolver;
        }

        public dynamic FromString(string jsonCmdOrQry)
        {
            try
            {
                if (jsonCmdOrQry.IsNullOrWhitespace()) throw new DomainQueryException("", "No command specified");

                var type = jsonCmdOrQry.GetTypeFromJson(_typeResolver);
                return jsonCmdOrQry.Deserialise(type);
            }
            catch (Exception e)
            {
                //e.ToString().LogDebug("[ERROR]");
                throw new DomainCommandException($"Unknown cmd: {jsonCmdOrQry}");
            }
        }
    }
}
