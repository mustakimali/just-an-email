using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JustSending.Services;

namespace JustSending.Data
{
    public class JsonShortNamePolicy : JsonNamingPolicy
    {
        private static readonly Dictionary<string, string> Cache = new();
        public override string ConvertName(string name)
        {
            if (Cache.TryGetValue(name, out var shortName))
                return shortName;

            var toTake = new List<char?>();
            char? lastLetter = null;
            foreach (var l in name)
            {
                if (lastLetter == null)
                {
                    lastLetter = l;
                    toTake.Add(lastLetter);
                }
                else if (char.IsLower(lastLetter.Value) && char.IsUpper(l))
                {
                    toTake.Add(l);
                }

                lastLetter = l;
            }
            
            shortName = string.Join("", toTake).ToLower() + name.ToSha1(2);
            Cache[name] = shortName;

            return shortName;
        }
    }
}