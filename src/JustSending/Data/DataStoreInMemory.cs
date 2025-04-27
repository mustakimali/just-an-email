using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace JustSending.Data
{
    public class DataStoreSqlite : IDataStore
    {
        private readonly AppDbContext _db;

        // ReSharper disable once SuggestBaseTypeForParameter
        public DataStoreSqlite(AppDbContext db, ILogger<DataStoreSqlite> logger)
        {
            _db = db;
        }

        async Task IDataStore.RemoveAsync<T>(string id)
        {
            await _db.KvRemove<T>(id);
        }

        public Task<T?> GetAsync<T>(string id)
        {
            return _db.KvGet<T?>(id);
        }

        public Task SetAsync<T>(string id, T data, TimeSpan ttl)
        {
            // todo: store and respect ttl
            return _db.KvSet(id, data);
        }
    }
}