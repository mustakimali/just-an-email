using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JustSending.Data;
using JustSending.Data.Models;
using JustSending.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JustSending.Services
{
    public class ConversationHub : Hub
    {
        public const string FILE_EXT = ".file";

        private readonly AppDbContext _db;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWebHostEnvironment _env;
        private readonly BackgroundJobScheduler _jobScheduler;
        private readonly ILogger<ConversationHub> _logger;
        private readonly string _uploadFolder;

        public ConversationHub(
            AppDbContext db, 
            IServiceProvider serviceProvider, 
            IWebHostEnvironment env, 
            BackgroundJobScheduler jobScheduler,
            ILogger<ConversationHub> logger)
        {
            _db = db;
            _serviceProvider = serviceProvider;
            _env = env;
            _jobScheduler = jobScheduler;
            _logger = logger;
            _uploadFolder = Helper.GetUploadFolder(string.Empty, _env.WebRootPath);

            _logger.LogInformation("Initializing with upload folder: {uploadFolder}", _uploadFolder);
            if (!Directory.Exists(_uploadFolder)) Directory.CreateDirectory(_uploadFolder);
        }

        internal async Task RequestReloadMessage(string sessionId)
        {
            await SignalREvent(sessionId, "requestReloadMessage");
        }

        internal async Task ShowSharePanel(string sessionId, int token)
        {
            await SignalREvent(sessionId, "showSharePanel", token.ToString(Constants.TOKEN_FORMAT_STRING));
        }

        internal async Task HideSharePanel(string sessionId)
        {
            await SignalREvent(sessionId, "hideSharePanel");
        }

        internal async Task NotifySessionDeleted(string[] connectionIds)
        {
            await SignalREventToClients(connectionIds, "sessionDeleted");
        }

        internal async Task SendNumberOfDevices(string sessionId, int count)
        {
            await SignalREvent(sessionId, "setNumberOfDevices", count);
        }

        internal async Task RedirectTo(string sessionId, string relativeUrl)
        {
            await SignalREvent(sessionId, "redirect", relativeUrl);
        }

        private async Task<int> SendNumberOfDevices(string sessionId)
        {
            var numConnectedDevices = await GetNumberOfDevices(sessionId);
            await SendNumberOfDevices(sessionId, numConnectedDevices);
            return numConnectedDevices;
        }

        private async Task<int> GetNumberOfDevices(string sessionId)
        {
            var session = await _db.Get<Session>(sessionId);
            var numConnectedDevices = session?.ConnectionIds.Count ?? 0;
            return numConnectedDevices;
        }

        private async Task SignalREvent(string sessionId, string message, object? data = null)
        {
            _logger.LogInformation("[SignaR Event] {message} ({@data})", message, data);

            try
            {
                if (data != null)
                    await (await GetClientsBySessionId(sessionId)).SendAsync(message, data);
                else
                    await (await GetClientsBySessionId(sessionId)).SendAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SignalR/SignalREvent:{message}] {errorMessage}", message, ex.GetBaseException().Message);
            }
        }

        private async Task SignalREventToClients(string[] connectionIds, string message, object? data = null)
        {
            _logger.LogInformation("[SignaR Event] {message} to {@connectionIds} ({@data})", message, connectionIds, data);
            try
            {
                if (data != null)
                    await Clients.Clients(connectionIds).SendAsync(message, data);
                else
                    await Clients.Clients(connectionIds).SendAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SignalR/SignalREventToClients:{message}] {errorMessage}", message, ex.GetBaseException().Message);
            }
        }

        private async Task<IClientProxy> GetClientsBySessionId(string sessionId, string? except = null)
        {
            var connectionIds = await _db.FindClient(sessionId);

            if (!string.IsNullOrEmpty(except))
            {
                connectionIds = connectionIds.Where(x => x != except);
            }

            return Clients.Clients(connectionIds.ToList());
        }

        private IClientProxy GetClientByConnectionId(string connectionId) => Clients.Client(connectionId);

        public override Task OnConnectedAsync()
        {
            _logger.LogInformation("[CONNECTED]: {connectionId}", Context.ConnectionId);

            using var statsDb = _serviceProvider.GetRequiredService<StatsDbContext>();
            statsDb.RecordStats(StatsDbContext.RecordType.Device);
            return Task.CompletedTask;
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogWarning(exception, "[DISCONNECTED]: {connectionId}", Context.ConnectionId);
            var sessionId = await _db.UntrackClientReturnSessionId(Context.ConnectionId);

            if (string.IsNullOrEmpty(sessionId)) return;

            // Don't Erase session if this session had been converted
            // to a Lite Session in the mean time
            //
            var session = await _db.Get<Session>(sessionId);
            if (session?.IsLiteSession ?? true) return;

            if (!string.IsNullOrEmpty(sessionId))
            {
                var numDevices = await SendNumberOfDevices(sessionId);
                if (numDevices == 0)
                {
                    var cIds = await _jobScheduler.EraseSessionReturnConnectionIds(sessionId);
                    await NotifySessionDeleted(cIds);
                }
                else
                {
                    await AddSessionNotification(sessionId, "A device was disconnected.");
                }
            }
        }

        public async Task AddSessionNotification(string sessionId, string message, bool isLiteSession = false)
        {
            _logger.LogInformation("AddSessionNotification: {sessionId}, ConnectionId: {connectionId}", sessionId, Context.ConnectionId);
            var msg = new Message
            {
                Id = AppDbContext.NewGuid(),
                SessionId = sessionId,
                DateSent = DateTime.UtcNow,
                Text = message,
                SocketConnectionId = Context.ConnectionId,
                IsNotification = true
            };

            await _db.MessagesInsert(msg);

            if (!isLiteSession)
                await RequestReloadMessage(sessionId);
        }

        public async Task<string> Connect(string sessionId)
        {
            await _db.TrackClient(sessionId, Context.ConnectionId);

            // Check if any active share token is open
            //
            await CheckIfShareTokenExists(sessionId);

            var numDevices = await GetNumberOfDevices(sessionId);
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
            var newDeviceId = Context.ConnectionId;
            var session = await _db.Get<Session>(sessionId);
            var firstDeviceId = session?.ConnectionIds.FirstOrDefault(cId => cId != newDeviceId);
            if (firstDeviceId == null) return;

            //
            // Send shared p & g to both parties then
            // request the original device to initite key-exchange
            //
            var p = Helper.GetPrime(1024, _env);
            var g = Helper.GetPrime(2, _env);
            var pka = Guid.NewGuid().ToString("N");

            var initFirstDevice = Clients
                .Client(firstDeviceId)
                .SendAsync("startKeyExchange", newDeviceId, p, g, pka, false);

            await initFirstDevice.ContinueWith(async x =>
            {
                await Clients
                    .Client(newDeviceId)
                    .SendAsync("startKeyExchange", firstDeviceId, p, g, pka, true);
            });

        }

        public async Task CallPeer(string peerId, string method, string param)
        {
            await ValidateIntent(peerId);

            IClientProxy endpoint;

            if (peerId == "ALL")
            {
                var sessionId = await GetSessionIdFromConnection();
                if (sessionId == null) return;

                endpoint = await GetClientsBySessionId(sessionId, except: Context.ConnectionId);

                var msg = new StringBuilder("A new device connected.<br/><i class=\"fa fa-lock\"></i> Message is end to end encrypted.");
                var numDevices = await GetNumberOfDevices(sessionId);
                if (numDevices == 2)
                {
                    msg.Append("<hr/><div class='text-info'>Frequently share data between these devices?<br/><span class='small'>Bookmark this page on each devices to quickly connect your devices.</span></div>");
                }
                await AddSessionNotification(sessionId, msg.ToString());
            }
            else
            {
                endpoint = GetClientByConnectionId(peerId);
            }

            await endpoint.SendAsync("callback", method, param);
        }

        private async Task ValidateIntent(string peerId)
        {

            if (peerId == "ALL")
                return;

            // check if the peer is actually a device within the current session
            // this is to prevent cross session communication
            //

            // Get session from connection
            var sessionMeta = await _db.Get<SessionMetaByConnectionId>(Context.ConnectionId);
            if (sessionMeta != null)
            {
                var devices = await _db.FindClient(sessionMeta.SessionId);
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
            var sessionShareToken = await _db.Get<SessionShareToken>(sessionId);
            if (sessionShareToken != null)
            {
                if (notifyIfExist)
                    await ShowSharePanel(sessionId, sessionShareToken.Token);
                return true;
            }
            return false;
        }

        public async Task Share()
        {
            var sessionId = await GetSessionIdFromConnection();
            if (sessionId == null) return;

            // Check if any share exist
            if (!await CheckIfShareTokenExists(sessionId))
            {
                var token = await _db.CreateNewShareToken(sessionId);
                await ShowSharePanel(sessionId, token);
            }
        }

        private async Task<string?> GetSessionIdFromConnection()
        {
            return (await _db.Get<SessionMetaByConnectionId>(Context.ConnectionId))?.SessionId;
        }

        public async Task CancelShare()
        {
            var sessionId = await GetSessionIdFromConnection();
            if (sessionId == null) return;

            if (await CancelShareSessionBySessionId(sessionId))
            {
                await HideSharePanel(sessionId);
            }
        }

        public async Task<bool> CancelShareSessionBySessionId(string sessionId)
        {
            var sessionShareToken = await _db.Get<SessionShareToken>(sessionId);
            if (sessionShareToken != null)
            {
                await _db.Remove<ShareToken>(sessionShareToken.Token.ToString());
                await _db.Remove<SessionShareToken>(sessionId);

                return true;
            }

            return false;
        }

        public async Task EraseSession()
        {
            var sessionId = await GetSessionIdFromConnection();
            if (sessionId == null) return;

            await EraseSessionInternal(sessionId);
        }

        private async Task EraseSessionInternal(string sessionId)
        {
            var cIds = await _jobScheduler.EraseSessionReturnConnectionIds(sessionId);
            await NotifySessionDeleted(cIds);
        }
    }
}