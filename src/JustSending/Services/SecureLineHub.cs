using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using JustSending.Data;
using Microsoft.Extensions.DependencyInjection;

namespace JustSending.Services
{
    public class SecureLineHub : Hub
    {
        private readonly ConcurrentDictionary<string, string> _connectionIdSessionMap = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _sessionIdConnectionIds = new();
        private readonly IWebHostEnvironment _env;
        private readonly IServiceProvider _serviceProvider;
        private readonly object _lock = new();

        public SecureLineHub(IWebHostEnvironment env, IServiceProvider serviceProvider)
        {
            _env = env;
            _serviceProvider = serviceProvider;
        }

        public async Task Init(string id)
        {
            var connectionId = Context.ConnectionId;
            lock (_lock)
            {
                if (!_connectionIdSessionMap.ContainsKey(connectionId))
                {
                    _ = _connectionIdSessionMap.TryAdd(connectionId, id);
                }
            }

            if (_sessionIdConnectionIds.TryGetValue(id, out var entry))
            {
                if (entry.Count == 2) return;

                if (!entry.Contains(connectionId))
                {
                    entry.Add(connectionId);
                }

                if (entry.Count > 1)
                {
                    using var db = _serviceProvider.GetRequiredService<StatsDbContext>();
                    db.RecordStats(StatsDbContext.RecordType.Message);
                    db.RecordStats(StatsDbContext.RecordType.Device);

                    await Broadcast("Start", null, true)
                        .ContinueWith(_ => InitKeyExchange(entry.First(), entry.Last()));
                }
            }
            else
            {
                _ = _sessionIdConnectionIds.TryAdd(id, new HashSet<string> { connectionId });
            }

        }

        private IEnumerable<string> GetClients(bool all)
        {
            var sessionId = _connectionIdSessionMap.GetValueOrDefault(Context.ConnectionId);
            if (string.IsNullOrEmpty(sessionId)) Enumerable.Empty<string>();

            var clients = _sessionIdConnectionIds.GetValueOrDefault(sessionId);
            if (clients == null) return Enumerable.Empty<string>();

            if (all)
                return clients;
            else
                return clients.Where(c => c != Context.ConnectionId);
        }

        public async Task Broadcast(string @event, string? data, bool all = false)
        {
            using var db = _serviceProvider.GetRequiredService<StatsDbContext>();
            db.RecordMessageStats(@event.Length + (data ?? "").Length);

            await Clients
                .Clients(GetClients(all).ToArray())
                .SendAsync("Boradcast", @event, data);
        }

        private async Task InitKeyExchange(string firstDeviceId, string newDevice)
        {
            var p = Helper.GetPrime(1024, _env);
            var g = Helper.GetPrime(2, _env);
            var pka = Guid.NewGuid().ToString("N");

            var initFirstDevice = Clients
                .Client(firstDeviceId)
                .SendAsync("startKeyExchange", newDevice, p, g, pka, false);

            await initFirstDevice.ContinueWith(async x =>
            {
                await Clients
                    .Client(newDevice)
                    .SendAsync("startKeyExchange", firstDeviceId, p, g, pka, true);
            });

            using var db = _serviceProvider.GetRequiredService<StatsDbContext>();
            db.RecordStats(StatsDbContext.RecordType.Message, 2);
        }

        public async Task CallPeer(string peerId, string method, string param)
        {
            using var db = _serviceProvider.GetRequiredService<StatsDbContext>();
            db.RecordStats(StatsDbContext.RecordType.Message);
            await Clients
                .Clients(GetClients(false).ToArray())
                .SendAsync("callback", method, param);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            if (_connectionIdSessionMap.TryGetValue(Context.ConnectionId, out var sessionId))
            {
                if (_sessionIdConnectionIds.TryGetValue(sessionId, out var maps)
                    && maps.Contains(Context.ConnectionId))
                {
                    await Broadcast("GONE", "", false);

                    _ = _connectionIdSessionMap.TryRemove(Context.ConnectionId, out _);
                    maps.Remove(Context.ConnectionId);

                }
            }

            base.OnDisconnectedAsync(exception).GetAwaiter().GetResult();
        }

        public override Task OnConnectedAsync()
        {
            using var db = _serviceProvider.GetRequiredService<StatsDbContext>();
            db.RecordStats(StatsDbContext.RecordType.Device);
            return base.OnConnectedAsync();
        }
    }
}