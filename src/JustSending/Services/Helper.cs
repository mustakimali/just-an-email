using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;

namespace JustSending
{
    public static class Helper
    {
        public static readonly DateTime BeginningOfUnixTime = new DateTime(1970, 1, 1);
        public static readonly Random Random = new Random(DateTime.UtcNow.Millisecond);

        public static string GetPrime(int length, IHostingEnvironment env)
        {
            var file = Path.Combine(env.WebRootPath, "Assets", "Primes", $"primes-{length}.txt");
            if (!File.Exists(file)) return string.Empty;

            var fileLines = File.ReadLines(file);
            var totalLines = Convert.ToInt16(fileLines.FirstOrDefault());
            var randomLine = Random.Next(0, totalLines) + 1;

            return fileLines.Skip(randomLine).FirstOrDefault();
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
                len = len / 1024;
            }

            return string.Format("{0:#,###,##0.##} {1}", len, sizes[order]);
        }
    }
}