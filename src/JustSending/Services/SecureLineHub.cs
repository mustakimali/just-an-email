using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using JustSending.Data;

namespace JustSending.Services
{
    public class SecureLineHub : Hub
    {
        private readonly ConcurrentDictionary<string, string> _connectionIdSessionMap = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, HashSet<string>> _sessionIdConnectionIds = new ConcurrentDictionary<string, HashSet<string>>();
        private readonly IWebHostEnvironment _env;
        private readonly AppDbContext _db;
        private readonly object _lock = new object();

        public SecureLineHub(IWebHostEnvironment env, AppDbContext db)
        {
            _env = env;
            _db = db;
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
                    _db.RecordStats(s =>
                    {
                        s.Messages++;
                        s.Sessions++;
                    });

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

        public async Task Broadcast(string @event, string data, bool all = false)
        {
            _db.RecordStats(s =>
            {
                s.Messages++;
                s.MessagesSizeBytes += @event.Length + (data ?? "").Length;
            });

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

            _db.RecordStats(s => s.Messages += 2);
        }

        public async Task CallPeer(string peerId, string method, string param)
        {
            _db.RecordStats(s => s.Messages++);
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
            _db.RecordStats(s => s.Devices++);
            return base.OnConnectedAsync();
        }
    }
}