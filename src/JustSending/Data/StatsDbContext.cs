using System;
using JustSending.Data.Models;
using JustSending.Data.Models.Bson;
using JustSending.Services;
using LiteDB;
using Microsoft.AspNetCore.Hosting;

namespace JustSending.Data
{
    public class StatsDbContext : IDisposable
    {
        private readonly IWebHostEnvironment _env;
        private LiteDatabase? _db;

        public StatsDbContext(IWebHostEnvironment env)
        {
            _env = env;
        }
        
        private LiteDatabase Database
            => _db ??= new LiteDatabase(Helper.BuildDbConnectionString("AppDb", _env));

        public ILiteCollection<Stats> Statistics => Database.GetCollection<Stats>();
        
        public void RecordMessageStats(Message msg)
        {
            RecordStats(s =>
            {
                s.Messages++;
                s.MessagesSizeBytes += msg.Text?.Length ?? 0;

                if (msg.HasFile)
                {
                    s.Files++;
                    s.FilesSizeBytes += msg.FileSizeBytes;
                }
            });
        }
        public void RecordStats(Action<Stats> update)
        {
            lock (this)
            {
                var utcNow = DateTime.UtcNow;

                // All time stats
                var allTime = StatsFindByIdOrNew(null);
                update(allTime);

                // This year
                var thisYear = StatsFindByIdOrNew(utcNow.Year);
                update(thisYear);

                // This month
                var thisMonth = StatsFindByIdOrNew(utcNow.Year, utcNow.Month);
                update(thisMonth);

                // Today
                var today = StatsFindByIdOrNew(utcNow.Year, utcNow.Month, utcNow.Day);
                update(today);

                Statistics.Upsert(new[]
                {
                    allTime,
                    thisYear,
                    thisMonth,
                    today
                });
            }
        }

        private Stats StatsFindByIdOrNew(int? year, int? month = null, int? day = null)
        {
            var id = StatsGetIdFor(year, month, day);
            var items = Statistics.FindById(id);
            return items ?? new Stats { Id = id, DateCreatedUtc = DateTime.UtcNow };
        }

        private static int StatsGetIdFor(int? year, int? month = null, int? day = null)
        {
            if (!year.HasValue)
                return -1;

            return Convert.ToInt32(year.Value.ToString().Substring(2) + (month ?? 0).ToString("00") + (day ?? 0).ToString("00"));
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}