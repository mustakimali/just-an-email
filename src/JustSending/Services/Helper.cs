using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;

namespace JustSending.Services
{
    public static class Helper
    {
        public static readonly DateTime BeginningOfUnixTime = new(1970, 1, 1);
        public static readonly Random Random = new(DateTime.UtcNow.Millisecond);

        public static string GetPrime(int length, IWebHostEnvironment env)
        {
            var file = Path.Combine(env.WebRootPath, "Assets", "Primes", $"primes-{length}.txt");
            if (!File.Exists(file)) return string.Empty;

            var fileLines = File.ReadLines(file);
            var totalLines = Convert.ToInt16(fileLines.FirstOrDefault());
            var randomLine = Random.Next(0, totalLines) + 1;

            return fileLines.Skip(randomLine).First();
        }

        public static int ToEpoch(this DateTime date) => (int)date.Subtract(BeginningOfUnixTime).TotalSeconds;

        public static string ToFileSize(this int len) => ((long)len).ToFileSize();

        public static string ToFileSize(this long lens)
        {
            var len = (double)lens;
            var sizes = new[] { "B", "KB", "MB", "GB", "TB" };
            var order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:#,###,##0.##} {sizes[order]}";
        }

        public static string BuildDbConnectionString(string dbName, IWebHostEnvironment hostingEnvironment, bool sqlLite = false)
        {
            var dataDirectory = Path.Combine(hostingEnvironment.ContentRootPath, "App_Data");
            if (!Directory.Exists(dataDirectory)) Directory.CreateDirectory(dataDirectory);

            if (sqlLite)
            {
                var path = Path.Combine(dataDirectory, dbName);

                return $"DataSource={path};Cache=Shared;";
            }

            var connectionString = new StringBuilder($"FileName={Path.Combine(dataDirectory, $"{dbName}.ldb")}");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                connectionString.Append(";Mode=Exclusive");
            }

            return connectionString.ToString();
        }

        public static string GetUploadFolder(string sessionId, string root)
        {
            return Path.Combine(root, "..", "App_Data", "upload", sessionId);
        }

        public static string ToSha1(this string input, int? take = null)
        {
            using var sha1 = SHA1.Create();
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