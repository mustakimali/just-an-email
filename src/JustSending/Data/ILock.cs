using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace JustSending.Data
{
    public interface ILock
    {
        Task<IDisposable> Acquire(string id);
    }

    public class SemaphoreLock : ILock
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

        public async Task<IDisposable> Acquire(string id)
        {
            var semaphore = _semaphores.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            return new SemaphoreReleaser(semaphore);
        }

        private class SemaphoreReleaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;

            public SemaphoreReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

            public void Dispose() => _semaphore.Release();
        }
    }
}