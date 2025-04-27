using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace JustSending.Data
{
    public interface IDataStore
    {
        private static readonly JsonSerializerOptions DefaultSerializerOption = new()
        {
            PropertyNamingPolicy = new JsonShortNamePolicy(),
            DictionaryKeyPolicy = new JsonShortNamePolicy()
        };

        protected Task<T?> GetAsync<T>(string id);
        protected Task SetAsync<T>(string id, T data, TimeSpan ttl);
        protected Task RemoveAsync<T>(string id);

        public async Task<T?> Get<T>(string id)
        {
            var key = GetKey<T>(id);
            var data = await GetAsync<T>(key);
            return data;
        }

        public async Task Set<T>(string id, T model, TimeSpan ttl)
        {
            var key = GetKey<T>(id);
            var data = JsonSerializer.SerializeToUtf8Bytes(model, DefaultSerializerOption);
            await SetAsync(key, data, ttl);
        }

        public async Task Remove<T>(string id)
        {
            var key = GetKey<T>(id);
            await RemoveAsync<T>(key);
        }

        private static string GetKey<T>(string id) => $"{typeof(T).Name.ToLower()}-{id}";
    }
}