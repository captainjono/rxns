using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rxns.NewtonsoftJson;
using StackExchange.Redis;

namespace Rxns.Redis
{
    public static class Extensions
    {
        public static RedisValue[] ToRedisValues<T>(this IEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            return source.Select(item => (RedisValue) ToRedisValue(source)).ToArray();
        }

        public static T To<T>(this RedisValue redisValue)
        {
            return redisValue.ToString().FromJson<T>();
        }

        public static string ToRedisValue(this object source)
        {
            return source.ToJson().ResolveAs(source.GetType());
        }
    }
}
