using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace JustSending.Data
{
    public class DataStoreInMemory : IDataStore
    {
        private readonly IMemoryCache _memoryCache;

        // ReSharper disable once SuggestBaseTypeForParameter
        public DataStoreInMemory(IMemoryCache memoryCache, ILogger<DataStoreInMemory> logger)
        {
            _memoryCache = memoryCache;
            logger.LogInformation("In Memory data store in use, in production server configure `RedisCache` to use redis");
        }

        Task<byte[]?> IDataStore.GetAsync(string id) => Task.FromResult(_memoryCache.Get<byte[]?>(id));

        Task IDataStore.SetAsync(string id, byte[] data, TimeSpan ttl) => Task.FromResult(_memoryCache.Set(id, data,
            new MemoryCacheEntryOptions()
                .SetSlidingExpiration(ttl)));

        Task IDataStore.RemoveAsync(string id)
        {
            _memoryCache.Remove(id);
            return Task.CompletedTask;
        }
    }
}