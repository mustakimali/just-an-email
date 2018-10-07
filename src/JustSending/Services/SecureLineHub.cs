using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using System;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Hosting;

namespace JustSending.Services
{
    public class SecureLineHub : Hub
    {
        private Dictionary<string, string> _connectionIdSessionMap = new Dictionary<string, string>();
        private Dictionary<string, HashSet<string>> _sessionIdConnectionIds = new Dictionary<string, HashSet<string>>();
        private readonly IHostingEnvironment _env;

        public SecureLineHub(IHostingEnvironment env)
        {
            _env = env;
        }

        public async Task Init(string id)
        {
            var connectionId = Context.ConnectionId;

            if (!_connectionIdSessionMap.ContainsKey(connectionId))
            {
                _connectionIdSessionMap.Add(connectionId, id);
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
                    await Broadcast("Start", null, true)
                        .ContinueWith(_ => InitKeyExchange(entry.First(), entry.Last()));
                }
            }
            else
            {
                _sessionIdConnectionIds.Add(id, new HashSet<string> { connectionId });
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
        }

        public async Task CallPeer(string peerId, string method, string param)
        {
            await Clients
                .Clients(GetClients(false).ToArray())
                .SendAsync("callback", method, param);
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            if (_connectionIdSessionMap.TryGetValue(Context.ConnectionId, out var sessionId))
            {
                _connectionIdSessionMap.Remove(Context.ConnectionId);
                if (_sessionIdConnectionIds.TryGetValue(sessionId, out var maps)
                    && maps.Contains(Context.ConnectionId))
                {
                    maps.Remove(Context.ConnectionId);

                }
            }

            return base.OnDisconnectedAsync(exception);
        }
    }
}