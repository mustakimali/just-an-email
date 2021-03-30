using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JustSending.Data
{
    public class JsonShortNamePolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
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

            var shortName = string.Join("", toTake).ToLower() + Hash(name, 2);
            return shortName;
        }

        private static string Hash(string input, int? take = null)
        {
            using SHA1Managed sha1 = new();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(hash.Length * 2);

            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
                if (take != null && sb.Length >= take)
                    break;
            }

            return sb.ToString();
        }
    }
}