using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JustSending.Controllers;
using JustSending.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;

namespace JustSending.Services
{
    public class ConversationHub : Hub
    {
        public const string FILE_EXT = ".file";

        private readonly AppDbContext _db;
        private readonly IHostingEnvironment _env;
        private readonly BackgroundJobScheduler _jobScheduler;

        private readonly string _uploadFolder;

        public ConversationHub(AppDbContext db, IHostingEnvironment env, BackgroundJobScheduler jobScheduler)
        {
            _db = db;
            _env = env;
            _jobScheduler = jobScheduler;

            _uploadFolder = Helper.GetUploadFolder(string.Empty, _env.WebRootPath);

            if (!Directory.Exists(_uploadFolder)) Directory.CreateDirectory(_uploadFolder);
        }

        internal async Task RequestReloadMessage(string sessionId)
        {
            await GetClients(sessionId).SendAsync("requestReloadMessage");
        }

        internal async Task ShowSharePanel(string sessionId, int token)
        {
            await GetClients(sessionId).SendAsync("showSharePanel", token.ToString("### ###"));
        }

        internal async Task HideSharePanel(string sessionId)
        {
            await GetClients(sessionId).SendAsync("hideSharePanel");
        }

        internal async Task SessionDeleted(string sessionId)
        {
            await GetClients(sessionId).SendAsync("sessionDeleted");
        }

        internal async Task SendNumberOfDevices(string sessionId, int count)
        {
            await GetClients(sessionId).SendAsync("setNumberOfDevices", count);
        }

        internal async Task<int> SendNumberOfDevices(string sessionId)
        {
            var numConnectedDevices = _db.Connections.Count(x => x.SessionId == sessionId);
            await SendNumberOfDevices(sessionId, numConnectedDevices);
            return numConnectedDevices;
        }

        private IClientProxy GetClients(string sessionId, string except = null)
        {
            var connectionIds = _db.FindClient(sessionId);

            if (!string.IsNullOrEmpty(except))
            {
                connectionIds = connectionIds.Where(x => x != except);
            }

            return Clients.Clients(connectionIds.ToList());
        }

        private IClientProxy GetClient(string connectionId) => Clients.Client(connectionId);

        public override Task OnConnectedAsync()
        {
            _db.RecordStats(s => s.Devices++);
            return Task.CompletedTask;
        }

        public override async Task OnDisconnectedAsync(Exception ex)
        {
            var sessionId = _db.UntrackClientReturnSessionId(Context.ConnectionId);
            if (!string.IsNullOrEmpty(sessionId))
            {
                var numDevices = await SendNumberOfDevices(sessionId);
                if (numDevices == 0)
                {
                    await EraseSessionInternal(sessionId);
                }
                else
                {
                    await AddSessionNotification(sessionId, "A device was disconnected.");
                }
            }
        }

        private async Task AddSessionNotification(string sessionId, string message)
        {
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
            await RequestReloadMessage(sessionId);
        }

        public async Task<string> Connect(string sessionId)
        {
            _db.TrackClient(sessionId, Context.ConnectionId);

            // Check if any active share token is open
            //
            await CheckIfShareTokenExists(sessionId);

            var numDevices = _db.Connections.Count(x => x.SessionId == sessionId);
            await SendNumberOfDevices(sessionId, numDevices);

            if (numDevices > 1)
            {
                await InitKeyExchange(sessionId);
                await CancelShare();
            }

            return Context.ConnectionId;
        }

        public async Task StreamFile(string sessionId, string content)
        {
            var uploadPath = Path.Combine(_uploadFolder, Context.ConnectionId + FILE_EXT);

            await File.AppendAllLinesAsync(uploadPath, new[] { content });
        }

        #region KeyExchange

        private async Task InitKeyExchange(string sessionId)
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
            ValidateIntent(peerId);

            IClientProxy endpoint;

            if (peerId == "ALL")
            {
                var sessionId = GetSessionId();

                endpoint = GetClients(sessionId, except: Context.ConnectionId);

                var msg = new StringBuilder("A new device connected.<br/><i class=\"fa fa-lock\"></i> Message is end to end encrypted.");
                var numDevices = _db.Connections.Count(x => x.SessionId == sessionId);
                if (numDevices == 2)
                {
                    msg.Append("<hr/><div class='text-info'>Frequently share data between these devices?<br/><span class='small'>Bookmark this page on each devices to quickly connect your devices.</span></div>");
                }
                await AddSessionNotification(sessionId, msg.ToString());
            }
            else
            {
                endpoint = GetClient(peerId);
            }

            await endpoint.SendAsync("callback", method, param);
        }

        private void ValidateIntent(string peerId)
        {

            if (peerId == "ALL")
                return;

            // check if the peer is actually a device within the current session
            // this is to prevent cross session communication
            //

            // Get session from connection
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if (connection != null)
            {
                var devices = _db.FindClient(connection.SessionId);
                if (devices.Contains(peerId))
                {
                    // OK
                    return;
                }
            }

            throw new InvalidOperationException("Attempt of cross session communication.");
        }

        #endregion

        private async Task<bool> CheckIfShareTokenExists(string sessionId, bool notifyIfExist = true)
        {
            var shareToken = _db.ShareTokens.FindOne(x => x.SessionId == sessionId);
            if (shareToken != null)
            {
                if (notifyIfExist)
                    await ShowSharePanel(sessionId, shareToken.Id);
                return true;
            }
            return false;
        }

        public async Task Share()
        {
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if (connection == null) return;

            // Check if any share exist
            if (!await CheckIfShareTokenExists(connection.SessionId))
            {
                var token = _db.CreateNewShareToken(connection.SessionId);
                await ShowSharePanel(connection.SessionId, token);
            }
        }

        public async Task CancelShare()
        {
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if (connection == null) return;

            var shareToken = _db.ShareTokens.FindOne(x => x.SessionId == connection.SessionId);
            if (shareToken != null)
            {
                _db.ShareTokens.Delete(shareToken.Id);
                await HideSharePanel(connection.SessionId);
            }
        }

        public async Task EraseSession()
        {
            var connection = _db.Connections.FindById(Context.ConnectionId);
            if (connection == null) return;

            await EraseSessionInternal(connection.SessionId);
        }

        private async Task EraseSessionInternal(string sessionId)
        {
            _jobScheduler.EraseSession(sessionId);
            await SessionDeleted(sessionId);
        }

        private string GetSessionId() =>
                            _db
                                .Connections
                                .FindOne(x => x.ConnectionId == Context.ConnectionId)
                                .SessionId;
    }
}