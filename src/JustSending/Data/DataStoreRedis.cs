using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace JustSending.Data
{
    public class DataStoreRedis : IDataStore
    {
        private readonly IDistributedCache _distributedCache;

        public DataStoreRedis(IDistributedCache distributedCache)
        {
            _distributedCache = distributedCache;
        }

        Task<byte[]?> IDataStore.GetAsync(string id) => _distributedCache.GetAsync(id);

        Task IDataStore.SetAsync(string id, byte[] data) => _distributedCache.SetAsync(id, data,
            new DistributedCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromHours(6)));
        Task IDataStore.RemoveAsync(string id) => _distributedCache.RemoveAsync(id);
    }
}