using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace JustSending.Data
{
    public class DataStoreInMemory : IDataStore
    {
        private readonly IMemoryCache _memoryCache;

        public DataStoreInMemory(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        Task<byte[]?> IDataStore.GetAsync(string id) => Task.FromResult(_memoryCache.Get<byte[]?>(id));

        Task IDataStore.SetAsync(string id, byte[] data) => Task.FromResult(_memoryCache.Set(id, data,
            new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromHours(6))));

        Task IDataStore.RemoveAsync(string id)
        {
            _memoryCache.Remove(id);
            return Task.CompletedTask;
        }
    }
}