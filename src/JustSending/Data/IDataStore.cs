using System.Text.Json;
using System.Threading.Tasks;

namespace JustSending.Data
{
    public interface IDataStore
    {
        private static readonly JsonSerializerOptions DefaultSerializerOption = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
        };

        protected Task<byte[]?> GetAsync(string id);
        protected Task SetAsync(string id, byte[] data);
        protected Task RemoveAsync(string id);
        
        public async Task<T?> Get<T>(string id)
        {
            var key = GetKey<T>(id);
            var data = await GetAsync(key);
            return data == null
                ? default
                : JsonSerializer.Deserialize<T>(data, DefaultSerializerOption);
        }

        public async Task Set<T>(string id, T model)
        {
            var key = GetKey<T>(id);
            var data = JsonSerializer.SerializeToUtf8Bytes(model, DefaultSerializerOption);
            await SetAsync(key, data);
        }
        
        public async Task Remove<T>(string id)
        {
            var key = GetKey<T>(id);
            await RemoveAsync(key);
        }

        private static string GetKey<T>(string id) => $"{typeof(T).Name.ToLower()}-{id}";
    }
}