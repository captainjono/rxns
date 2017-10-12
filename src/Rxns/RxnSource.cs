using System;

namespace Rxns
{
    public class RxnSource
    {
        public string Id { get; set; }

        public RxnSource(string id = null)
        {
            Id = id ?? Guid.NewGuid().ToString();
        }

        public static RxnSource Create()
        {
            return new RxnSource();
        }

        public static RxnSource ShareWith(RxnSource source)
        {
            return new RxnSource(source.Id);
        }
    }
}
