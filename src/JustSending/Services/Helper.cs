using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;

namespace JustSending
{
    public static class Helper
    {
        public static readonly Random Random = new Random(DateTime.UtcNow.Millisecond);

        public static string ToFileSizeString(this long fileSize)
        {
            var unitsOfFileSize = new[] { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            var fileSizeInLong = (double) fileSize;
            var order = 0;
            while (fileSizeInLong >= 1024 && order + 1 < unitsOfFileSize.Length)
            {
                order++;
                fileSizeInLong = fileSizeInLong / 1024;
            }

            return string.Format("{0:0.##} {1}", fileSizeInLong, unitsOfFileSize[order]);
        }

        public static string GetPrime(int length, IHostingEnvironment env)
        {
            var file = Path.Combine(env.ContentRootPath, "Assets", "Primes", $"primes-{length}.txt");
            if(!File.Exists(file)) return string.Empty;

            var fileLines = File.ReadLines(file);
            var totalLines = Convert.ToInt16(fileLines.FirstOrDefault());
            var randomLine = Helper.Random.Next(0, totalLines) + 1;

            return fileLines.Skip(randomLine).FirstOrDefault();
        }
    }
}