using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using JustSending.Data.Models;
using JustSending.Data.Models.Bson;
using Microsoft.AspNetCore.Hosting;
using OpenTelemetry.Trace;
using Microsoft.Data.Sqlite;
using static JustSending.Controllers.StatsRawHandler;
using Dapper;
using Microsoft.Extensions.Configuration;

namespace JustSending.Data
{
    public class StatsDbContext : IDisposable
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILock _lock;
        private readonly Tracer _tracer;
        private readonly SqliteConnection _connection;

        public StatsDbContext(IWebHostEnvironment env, ILock @lock, Tracer tracer, IConfiguration config)
        {
            _tracer = tracer;
            _env = env;
            _lock = @lock;

            _connection = new SqliteConnection(config.GetConnectionString("StatsCs"));

        }

        public async Task<StatYear[]> GetAll()
        {
            using var span = _tracer.StartActiveSpan("get-all-stats");

            return [.. (await _connection.QueryAsync<Stats>("SELECT * FROM Stats WHERE Id > 1"))
                    .ToArray()
                    .GroupBy(x => x.Id.ToString()[..2])
                    .Select(x => new StatYear(x.Key, x
                        .GroupBy(y => y.Id.ToString().Substring(2, 2))
                        .Select(dayData => new StatMonth(dayData.Key, [.. dayData]))))];
        }

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

        public async Task RecordHistoricStat(Stats stat)
        {
            await _connection.ExecuteAsync(@"
                INSERT INTO Stats (Id, Messages, MessagesSizeBytes, Files, FilesSizeBytes, Devices, Sessions, DateCreatedUtc, Version)
                VALUES (@Id, @Messages, @MessagesSizeBytes, @Files, @FilesSizeBytes, @Devices, @Sessions, @DateCreatedUtc, 0) ON CONFLICT(Id) DO NOTHING", stat);
        }

        public async Task UpdateHistoricStat(Stats stat)
        {
            await _connection.ExecuteAsync(@"
                UPDATE Stats 
                SET Messages = @Messages,
                    MessagesSizeBytes = @MessagesSizeBytes,
                    Files = @Files,
                    FilesSizeBytes = @FilesSizeBytes,
                    Devices = @Devices,
                    Sessions = @Sessions,
                    Version = @Version
                WHERE Id = @Id AND Version < 1000", stat); // avoid updating more than once
        }

        private async Task RecordStats(Action<Stats> update, DateTime? date = null)
        {
            var utcNow = date ?? DateTime.UtcNow;
            using var _ = await _lock.Acquire($"stat-{utcNow:d}");

            // All time stats
            var allTime = await StatsFindByDateOrNew(null);
            update(allTime);

            // This year
            var thisYear = await StatsFindByDateOrNew(utcNow.Year);
            update(thisYear);

            // This month
            var thisMonth = await StatsFindByDateOrNew(utcNow.Year, utcNow.Month);
            update(thisMonth);

            // Today
            var today = await StatsFindByDateOrNew(utcNow.Year, utcNow.Month, utcNow.Day);
            update(today);

            await Upsert(allTime);
            await Upsert(thisYear);
            await Upsert(thisMonth);
            await Upsert(today);
        }

        private async Task Upsert(Stats stats)
        {
            stats.Version++;
            var rows = await _connection.ExecuteAsync(@"
                INSERT INTO Stats (Id, Messages, MessagesSizeBytes, Files, FilesSizeBytes, Devices, Sessions, DateCreatedUtc, Version)
                VALUES (@Id, @Messages, @MessagesSizeBytes, @Files, @FilesSizeBytes, @Devices, @Sessions, @DateCreatedUtc, @Version)
                ON CONFLICT(Id) DO UPDATE SET
                    Messages = @Messages,
                    MessagesSizeBytes = @MessagesSizeBytes,
                    Files = @Files,
                    FilesSizeBytes = @FilesSizeBytes,
                    Devices = @Devices,
                    Sessions = @Sessions,
                    Version = @Version
                WHERE Version = @Version - 1", stats);

            if (rows == 0) throw new Exception("Optimistic concurrency error");
        }

        public async Task<Stats> StatsFindByDateOrNew(int? year, int? month = null, int? day = null)
        {
            var id = StatsGetIdFor(year, month, day);
            return await StatsFindByIdOrNew(id);
        }

        public async Task<Stats> StatsFindByIdOrNew(int id)
        {
            var items = (await _connection.QueryAsync<Stats>("SELECT * FROM Stats WHERE Id = @Id", new { Id = id })).FirstOrDefault();
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
            _connection.Dispose();
        }
    }
}