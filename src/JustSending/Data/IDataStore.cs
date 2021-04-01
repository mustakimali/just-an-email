using System;
using System.Text.Json;
using System.Threading.Tasks;
using JustSending.Services;

namespace JustSending.Data
{
    public interface IDataStore
    {
        private static readonly JsonSerializerOptions DefaultSerializerOption = new()
        {
            PropertyNamingPolicy = new JsonShortNamePolicy(),
            DictionaryKeyPolicy = new JsonShortNamePolicy()
        };

        protected Task<byte[]?> GetAsync(string id);
        protected Task SetAsync(string id, byte[] data, TimeSpan ttl);
        protected Task RemoveAsync(string id);
        
        public async Task<T?> Get<T>(string id)
        {
            var key = GetKey<T>(id);
            var data = await GetAsync(key);
            return data == null
                ? default
                : JsonSerializer.Deserialize<T>(data, DefaultSerializerOption);
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
            await RemoveAsync(key);
        }

        private static string GetKey<T>(string id) => $"{typeof(T).Name.ToLower()}-{id}";
    }
}