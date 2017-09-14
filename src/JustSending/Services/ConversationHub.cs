using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JustSending.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Infrastructure;

namespace JustSending.Services
{
    public class ConversationHub : Hub
    {
        private readonly AppDbContext _db;
        private readonly IConnectionManager _connectionManager;
        private readonly IHostingEnvironment _env;

        public ConversationHub(AppDbContext db, IConnectionManager connectionManager, IHostingEnvironment env)
        {
            _db = db;
            _connectionManager = connectionManager;
            _env = env;
        }

        internal void RequestReloadMessage(string sessionId)
        {
            GetClients(sessionId).requestReloadMessage();
        }

        internal void ShowSharePanel(string sessionId, int token)
        {
            GetClients(sessionId).showSharePanel(token.ToString("### ###"));
        }

        internal void HideSharePanel(string sessionId)
        {
            GetClients(sessionId).hideSharePanel();
        }

        internal void SessionDeleted(string sessionId)
        {
            GetClients(sessionId).sessionDeleted();
        }

        internal void SendNumberOfDevices(string sessionId, int count)
        {
            GetClients(sessionId).setNumberOfDevices(count);
        }

        internal int SendNumberOfDevices(string sessionId)
        {
            var numConnectedDevices = _db.Connections.Count(x => x.SessionId == sessionId);
            SendNumberOfDevices(sessionId, numConnectedDevices);
            return numConnectedDevices;
        }

        private dynamic GetClients(string sessionId, string except = null)
        {
            var connectionIds = _db.FindClient(sessionId);

            if(!string.IsNullOrEmpty(except)){
                connectionIds = connectionIds.Where(x => x != except);
            }

            return CurrentHub.Clients.Clients(connectionIds.ToList());
        }

        private dynamic GetClient(string connectionId) => CurrentHub.Clients.Client(connectionId);

        private IHubContext CurrentHub => _connectionManager.GetHubContext<ConversationHub>();

        private const string stringSessionKey = "session";
        public override Task OnConnected()
        {
            return Task.CompletedTask;
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            var sessionId = _db.UntrackClientReturnSessionId(Context.ConnectionId);
            if (!string.IsNullOrEmpty(sessionId))
            {
                var numDevices = SendNumberOfDevices(sessionId);
                if (numDevices == 0)
                {
                    EraseSessionInternal(sessionId);
                } else {
                    AddSessionNotification(sessionId, "A device was disconnected.");
                }
            }

            return Task.CompletedTask;
        }

        private void AddSessionNotification(string sessionId, string message) {
            var msg = new Message
            {
                Id = _db.NewGuid(),
                SessionId = sessionId,
                DateSent = DateTime.UtcNow,
                Text = message,
                SocketConnectionId = Context.ConnectionId,
                IsNotification = true
            };
            
            _db.MessagesInsert(msg);
            RequestReloadMessage(sessionId);
        }

        public string Connect(string sessionId)
        {
            _db.TrackClient(sessionId, Context.ConnectionId);

            // Check if any active share token is open
            //
            CheckIfShareTokenExists(sessionId);

            var numDevices = _db.Connections.Count(x => x.SessionId == sessionId);
            SendNumberOfDevices(sessionId, numDevices);

            if (numDevices > 1)
            {
                InitKeyExchange(sessionId);
            }

            return Context.ConnectionId;
        }

        #region KeyExchange

        private void InitKeyExchange(string sessionId)
        {
            // enable end to end encryption
            //
            var newDevice = Context.ConnectionId;
            var firstDeviceId =
                _db
                    .Connections
                    .FindOne(x => x.SessionId == sessionId && x.ConnectionId != newDevice)
                    .ConnectionId;

            //
            // Send shared p & g to both parties then
            // request the original device to initite key-exchange
            //
            var p = Helper.GetPrime(1024, _env);
            var g = Helper.GetPrime(2, _env);
            var pka = Guid.NewGuid().ToString("N");

            var initFirstDevice = (Task<object>) CurrentHub
                .Clients
                .Client(firstDeviceId)
                .startKeyExchange(newDevice, p, g, pka, false);

            initFirstDevice.ContinueWith(x =>
            {

                CurrentHub
                .Clients
                .Client(newDevice)
                .startKeyExchange(firstDeviceId, p, g, pka, true);

            });

        }

        public void CallPeer(string peerId, string method, string param) {
            ValidateIntent(peerId);

            dynamic endpoint;

            if(peerId == "ALL") {
                var sessionId = GetSessionId();

                endpoint = GetClients(sessionId, except: Context.ConnectionId);

                AddSessionNotification(sessionId, "A new device connected.<br/><i class=\"fa fa-lock\"></i> Message is End to End encrypted.");
            } else {
                endpoint = GetClient(peerId);
            }

            endpoint.callback(method, param);
        }

        private void ValidateIntent(string peerId) {

            if (peerId == "ALL")
                return;

            // check if the peer is actually a device within the current session
            // this is to prevent cross session communication
            //

            // Get session from connection
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if(connection != null) {
                var devices = _db.FindClient(connection.SessionId);
                if(devices.Contains(peerId)){
                    // OK
                    return;
                }
            }

            throw new InvalidOperationException("Attempt of cross session communication.");
        }

        #endregion

        private bool CheckIfShareTokenExists(string sessionId, bool notifyIfExist = true)
        {
            var shareToken = _db.ShareTokens.FindOne(x => x.SessionId == sessionId);
            if (shareToken != null)
            {
                if (notifyIfExist)
                    ShowSharePanel(sessionId, shareToken.Id);
                return true;
            }
            return false;
        }

        public void Share()
        {
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if (connection == null) return;

            // Check if any share exist
            if (!CheckIfShareTokenExists(connection.SessionId))
            {
                var token = _db.CreateNewShareToken(connection.SessionId);
                ShowSharePanel(connection.SessionId, token);
            }
        }

        public void CancelShare()
        {
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if (connection == null) return;

            var shareToken = _db.ShareTokens.FindOne(x => x.SessionId == connection.SessionId);
            if (shareToken != null)
            {
                _db.ShareTokens.Delete(shareToken.Id);
                HideSharePanel(connection.SessionId);
            }
        }

        public void EraseSession()
        {
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if (connection == null) return;

            EraseSessionInternal(connection.SessionId);
        }

        private void EraseSessionInternal(string sessionId)
        {
            _db.Sessions.Delete(sessionId);
            _db.Messages.Delete(x => x.SessionId == sessionId);
            _db.ShareTokens.Delete(x => x.SessionId == sessionId);

            SessionDeleted(sessionId);

            _db.Connections.Delete(x => x.SessionId == sessionId);

            try
            {
                var folder = Controllers.AppController.GetUploadFolder(sessionId, _env.WebRootPath);
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch
            {
                // ToDo: Schedule delete later
            }
        }

        private string GetSessionId() =>
                            _db
                                .Connections
                                .FindOne(x => x.ConnectionId == Context.ConnectionId)
                                .SessionId;


        public override Task OnReconnected()
        {
            return OnConnected();
        }
    }
}