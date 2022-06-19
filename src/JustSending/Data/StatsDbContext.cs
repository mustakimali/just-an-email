using System;
using System.Threading.Tasks;
using Hangfire;
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
        private readonly ILock _lock;
        private LiteDatabase? _db;

        public StatsDbContext(IWebHostEnvironment env, ILock @lock)
        {
            _env = env;
            _lock = @lock;
        }
        
        private LiteDatabase Database
            => _db ??= new LiteDatabase(Helper.BuildDbConnectionString("AppDb", _env));

        public ILiteCollection<Stats> Statistics => Database.GetCollection<Stats>();
        
        public void RecordStats(RecordType type, int inc = 1)
        {
            BackgroundJob.Enqueue(() => RecordBg(DateTime.UtcNow, type, inc));
        }
        
        public void RecordMessageStats(Message msg)
        {
            long? fileSize = msg.HasFile
                ? msg.FileSizeBytes
                : null;

            BackgroundJob.Enqueue(() => RecordMsgBg(DateTime.UtcNow, msg.Text.Length, fileSize));
        }
        
        public void RecordMessageStats(int msgSize)
        {
            BackgroundJob.Enqueue(() => RecordMsgBg(DateTime.UtcNow, msgSize, null));
        }
        
        
        // Job
        public Task RecordMsgBg(DateTime date, int msgSizeBytes, long? fileSizeBytes)
        {
            return RecordStats(s =>
            {
                s.Messages++;
                s.MessagesSizeBytes += msgSizeBytes;

                if (fileSizeBytes.HasValue)
                {
                    s.Files++;
                    s.FilesSizeBytes += fileSizeBytes.Value;
                }
            }, date);
        }
        
        // job

        public enum RecordType
        {
            Session,
            Device,
            Message
        }
        public Task RecordBg(DateTime date, RecordType type, int inc)
        {
            return type switch
            {
                RecordType.Session => RecordStats(s => s.Sessions += inc, date),
                RecordType.Device => RecordStats(s => s.Devices += inc, date),
                RecordType.Message => RecordStats(s => s.Messages += inc, date),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        private async Task RecordStats(Action<Stats> update, DateTime? date = null)
        {
            var utcNow = date ?? DateTime.UtcNow;
            using var _ = await _lock.Acquire($"stat-{utcNow:d}");

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