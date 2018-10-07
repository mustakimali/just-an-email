using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using System;

namespace JustSending.Services
{
    public class SecureLineHub : Hub
    {
        private Dictionary<string, string> _connectionIdSessionMap = new Dictionary<string, string>();
        private Dictionary<string, HashSet<string>> _idConnectionIds = new Dictionary<string, HashSet<string>>();

        public async Task Init(string id)
        {
            var connectionId = Context.ConnectionId;

            if (!_connectionIdSessionMap.ContainsKey(connectionId))
            {
                _connectionIdSessionMap.Add(connectionId, id);
            }

            if (_idConnectionIds.TryGetValue(id, out var entry))
            {
                if (entry.Count == 2) return;

                if (!entry.Contains(connectionId))
                {
                    entry.Add(connectionId);
                }

                if (entry.Count > 1)
                {
                    await Broadcast("Start", null);
                }
            }
            else
            {
                _idConnectionIds.Add(id, new HashSet<string> { connectionId });
            }
        }

        public async Task Broadcast(string @event, string data)
        {
            var sessionId = _connectionIdSessionMap.GetValueOrDefault(Context.ConnectionId);
            if (string.IsNullOrEmpty(sessionId)) return;

            var clients = _idConnectionIds.GetValueOrDefault(sessionId);
            if (clients == null) return;

            await Clients.Clients(clients.Where(c => c != Context.ConnectionId).ToArray()).SendAsync(@event, data);
        }
    }
}