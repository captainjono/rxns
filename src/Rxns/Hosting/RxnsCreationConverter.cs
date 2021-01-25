using System;
using Newtonsoft.Json.Converters;

namespace Rxns.Hosting
{
    public class RxnsCreationConverter<T> : CustomCreationConverter<T> where T : class
    {
        private readonly Func<Type, T> _objectResolver;

        public override bool CanWrite
        {
            get { return false; }
        }

        public RxnsCreationConverter(Func<Type, T> objectResolver)
        {
            _objectResolver = objectResolver;
        }

        public override T Create(Type objectType)
        {
            return _objectResolver(objectType);
        }
    }
}
