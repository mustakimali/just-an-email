using System;
using System.Threading.Tasks;
using RedLockNet.SERedis;

namespace JustSending.Data
{
    public interface ILock
    {
        Task<IDisposable> AcquireLock(string id);
    }
    
    public class RedisLock : ILock
    {
        private readonly RedLockFactory _lock;

        public RedisLock(RedLockFactory @lock)
        {
            _lock = @lock;
        }

        public async Task<IDisposable> AcquireLock(string id) => await _lock.CreateLockAsync(id, TimeSpan.FromHours(6));
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

        public Task<IDisposable> AcquireLock(string id) => Task.FromResult((IDisposable) new NoOp());
    }
}