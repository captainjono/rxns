using System;
using System.Collections.Generic;
using StackExchange.Redis;

namespace Rxns.Redis
{
    public class RedisCacheFactory 
    {
        private readonly IRedisConfiguration _configuration;
        private readonly Lazy<ConnectionMultiplexer> _connection;

        public RedisCacheFactory(IRedisConfiguration configuration)
        {
            _configuration = configuration;
            _connection = new Lazy<ConnectionMultiplexer>(() => { return ConnectionMultiplexer.Connect(configuration.RedisConnectionString); });
        }

        public IDictionary<TKey, TValue> Create<TKey, TValue>(string dictionaryName = null)
        {
            if (_connection == null)
                throw new NotImplementedException("The property 'connection' for redis is empty");

            return new RedisDictionary<TKey, TValue>(_connection.Value.GetDatabase(), $"{_configuration.PartitionId}_{dictionaryName}");
        }
    }

    public interface IRedisConfiguration
    {
        string RedisConnectionString { get; }
        string PartitionId { get; }
    }

    public class RedisCfg : IRedisConfiguration
    {
        public string RedisConnectionString { get; set; }
        public string PartitionId { get; set; }
    }
}
