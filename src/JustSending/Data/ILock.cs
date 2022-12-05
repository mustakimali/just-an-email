using System;
using System.Threading.Tasks;
using JustSending.Services;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using RedLockNet.SERedis;

namespace JustSending.Data
{
    public interface ILock
    {
        Task<IDisposable> Acquire(string id);
    }

    public class RedisLock : ILock
    {
        private readonly RedLockFactory _lock;
        private readonly ILogger<RedisLock> _logger;
        private readonly Tracer _tracer;

        public RedisLock(RedLockFactory @lock, ILogger<RedisLock> logger, Tracer tracer)
        {
            _tracer = tracer;
            _lock = @lock;
            _logger = logger;
        }

        public async Task<IDisposable> Acquire(string id)
        {
            using var span = _tracer.StartActiveSpan("acquire-lock");
            span.SetAttribute("key", id);

            _logger.LogInformation("Lock acquiring for {key}", id);
            var started = DateTime.UtcNow;
            var result = await _lock.CreateLockAsync(id.ToSha1(), TimeSpan.FromHours(6));
            var elapsed = DateTime.UtcNow - started;
            _logger.LogInformation("Lock acquired for {key} in {time}ms", id, elapsed.TotalMilliseconds);
            return new Lock(id, result, DateTime.UtcNow, _logger);
        }

        private record Lock(string Id, IDisposable Inner, DateTime LockAcquired, ILogger Logger) : IDisposable
        {
            public void Dispose()
            {
                Inner.Dispose();
                var elapsed = DateTime.UtcNow - LockAcquired;
                Logger.LogInformation("Lock {id} dropped after {time}ms", Id, elapsed.TotalMilliseconds);
            }
        }
    }

    public class NoOpLock : ILock
    {
        public class NoOp : IDisposable
        {
            internal NoOp()
            {

            }
            public void Dispose()
            {
            }
        }

        public Task<IDisposable> Acquire(string id) => Task.FromResult((IDisposable)new NoOp());
    }
}