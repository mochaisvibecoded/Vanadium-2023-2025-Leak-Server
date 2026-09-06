using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Newtonsoft.Json;
using Vanadium.Classes;
using Vanadium.Classes.WebSocket;
using Vanadium.Hubs;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    [Route("notifications")]
    public class NotificationsController : Controller
    {
        public static ConcurrentDictionary<string, WebSocket> WebSockets { get; } = new();
        public static ConcurrentDictionary<long, HashSet<string>> PlayerConnections { get; } = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> SendGates = new();
        private static readonly byte[] Separator = Encoding.UTF8.GetBytes("\u001e");

        public static class EventTypes
        {
            public const string AccountUpdate = NotiEventTypes.AccountUpdate;
            public const string SelfAccountUpdate = NotiEventTypes.SelfAccountUpdate;
            public const string PresenceUpdate = NotiEventTypes.PresenceUpdate;
            public const string RoomUpdate = NotiEventTypes.RoomUpdate;
            public const string RoomInstanceUpdate = NotiEventTypes.RoomInstanceUpdate;
            public const string ReputationUpdate = NotiEventTypes.ReputationUpdate;
            public const string PlayerProgressionLevelUpdate = NotiEventTypes.PlayerProgressionLevelUpdate;
            public const string PhotonAccessToken = NotiEventTypes.PhotonAccessToken;
            public const string InfluencerSupportedUpdate = NotiEventTypes.InfluencerSupportedUpdate;
        }

        [HttpGet]
        public IActionResult NameserverNoti()
        {
            return Ok(new
            {
                Auth = "https://reloxa.xyz/notifications",
                API = "https://reloxa.xyz/notifications",
                WWW = "https://reloxa.xyz/notifications",
                Notifications = "https://reloxa.xyz/notifications",
                Images = "https://reloxa.xyz/notifications/img",
                CDN = "https://reloxa.xyz/cdn",
                Commerce = "https://reloxa.xyz/notifications",
                Matchmaking = "https://reloxa.xyz/notifications",
                Storage = "https://reloxa.xyz/notifications",
                Chat = "https://reloxa.xyz/notifications/chat",
                Leaderboard = "https://reloxa.xyz/notifications",
                Accounts = "https://reloxa.xyz/notifications",
                Link = "https://reloxa.xyz/notifications",
                RoomComments = "https://reloxa.xyz/notifications",
                Clubs = "https://reloxa.xyz/notifications",
                Rooms = "https://reloxa.xyz/notifications/roomserver",
                PlatformNotifications = "https://reloxa.xyz/notifications/pn",
                Moderation = "https://reloxa.xyz/notifications/mo",
                DataCollection = "https://reloxa.xyz/notifications/dc",
                BugReporting = "https://reloxa.xyz/notifications/br",
                Discovery = "https://reloxa.xyz/notifications/disc",
                Econ = "https://reloxa.xyz/notifications",
                CMS = "https://reloxa.xyz/notifications/cms",
                GameLogs = "https://reloxa.xyz/notifications/gl",
                Lists = "https://reloxa.xyz/notifications/li",
                PlayerSettings = "https://reloxa.xyz/notifications",
                Strings = "https://reloxa.xyz/notifications/s",
                StringsCDN = "https://reloxa.xyz/notifications/scdn",
                Studio = "https://reloxa.xyz/notifications/st"
            });
        }



        [HttpPost("hub/v1/negotiate")]
        public IActionResult Negotiate()
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            string connectionId = Guid.NewGuid().ToString("N");

            var response = new
            {
                negotiateVersion = 0,
                connectionId = connectionId,
                availableTransports = new[]
                {
                    new
                    {
                        transport = "WebSockets",
                        transferFormats = new[] { "Text", "Binary" }
                    }
                }
            };

            return Ok(response);
        }

        [Route("hub/v1")]
        public async Task HandleHub([FromQuery] string id, [FromHeader(Name = "Authorization")] string? auth)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            string connectionId = id;
            long pId = playerId.Value;

            using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            //Console.WriteLine($"[WebSocket] Player {pId} connected with connection {connectionId}");

            PlayerConnections.AddOrUpdate(
                pId,
                _ => new HashSet<string> { connectionId },
                (_, hs) =>
                {
                    lock (hs) { hs.Add(connectionId); }
                    return hs;
                }
            );

            await HandleConnectionAsync(pId, connectionId, socket);
        }

        [HttpGet("preferences")]
        public IActionResult GetPreferences()
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");
            return Ok(new { mutedCategories = Array.Empty<object>() });
        }

        [HttpGet("crm/me/config/v3")]
        public IActionResult GetCRMConfig()
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");
            return Ok(Array.Empty<object>());
        }

        [HttpGet("accounts/{id}/receives/GameplayInvites")]
        public IActionResult GetGameplayInvites(ulong id)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");
            return Ok(true);
        }

        [HttpGet("config/categories")]
        public IActionResult GetCategories()
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");
            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "NotiConfigCategories.json");
            if (!System.IO.File.Exists(path))
                return Ok(new { results = Array.Empty<object>(), totalResults = 0 });
            return PhysicalFile(path, "application/json");
        }

        [HttpGet("/announcements/v2/mine/unread")]
        public IActionResult GetUnreadAnnouncements()
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/announcements/v2/subscription/mine/unread")]
        public IActionResult GetUnreadSubscriptionAnnouncements()
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/subscription/mine/member")]
        public IActionResult GetSubscriptionMember()
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");
            return Ok(ServerConfig.Bracket);
        }

        public static int ReconcileStartupOnlineHeartbeats(CancellationToken cancellationToken = default)
        {
            var connectedPlayerIds = PlayerConnections.Keys.ToHashSet();

            var onlinePlayers = PlayerDB.Players
                .FindAll()
                .Where(p => p?.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
                .ToList();

            int fixedCount = 0;

            foreach (var player in onlinePlayers)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (player == null || player.Player == null || player.Player.PlayerExtra == null)
                    continue;

                if (connectedPlayerIds.Contains(player.PlayerId))
                    continue;

                player.Player.PlayerExtra.Heartbeat = new Heartbeat
                {
                    playerId = player.PlayerId,
                    isOnline = false,
                    roomInstance = null,
                    errorCode = 0
                };

                PlayerDB.Players.Update(player);
                fixedCount++;
            }

            Console.WriteLine($"[Notifications] Startup heartbeat reconcile complete. Marked {fixedCount} stale online heartbeats offline.");
            return fixedCount;
        }

        private static async Task HandleConnectionAsync(long playerId, string connectionId, WebSocket socket)
        {
            using var pingCts = new CancellationTokenSource();

            try
            {
                WebSockets[connectionId] = socket;
                SendGates[connectionId] = new SemaphoreSlim(1, 1);

                await SendHandshakeAsync(connectionId, socket);

                _ = Task.Run(() => PingLoopAsync(connectionId, socket, pingCts.Token));

                var buffer = new byte[4096];
                while (socket.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var message = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length).TrimEnd('\x1e');
                    //Console.WriteLine($"Player ({playerId}) sent: {message}");

                    await HandleClientMessageAsync(connectionId, socket, message);
                }
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[Error:{connectionId}] {ex.Message}");
            }
            finally
            {
                pingCts.Cancel();
                WebSockets.TryRemove(connectionId, out _);

                if (SendGates.TryRemove(connectionId, out var gate))
                    gate.Dispose();

                if (PlayerConnections.TryGetValue(playerId, out var connections))
                {
                    lock (connections)
                    {
                        connections.Remove(connectionId);

                        if (connections.Count == 0)
                            PlayerConnections.TryRemove(playerId, out _);
                    }
                }

                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing connection", CancellationToken.None);
                }

                //Console.WriteLine($"[WebSocket] Player {playerId} disconnected from connection {connectionId}");

                bool lastConnection = !PlayerConnections.ContainsKey(playerId);
                if (lastConnection)
                {
                    var offlineHb = PlayerDB.UpdatePlayerHeartbeat(playerId, null, online: false);
                    if (offlineHb != null)
                    {
                        offlineHb.playerId = playerId;
                        var friendIds = FriendsDB.GetRelationships(playerId).Select(r => r.OtherPlayerID).ToList();
                        var hbJson = JsonConvert.SerializeObject(new WebsocketEvents.Response
                        {
                            Id = NotiEventTypes.PresenceUpdate, // to show they are offline now
                            Msg = offlineHb
                        });
                        await SendToPlayers(friendIds, hbJson);
                    }
                }
            }
        }

        private static async Task HandleClientMessageAsync(string connectionId, WebSocket socket, string message)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(message);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeProp)
                    && typeProp.ValueKind == System.Text.Json.JsonValueKind.Number
                    && typeProp.GetInt32() == 1)
                {
                    string target = root.TryGetProperty("target", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    string invocationId = root.TryGetProperty("invocationId", out var i) ? i.GetString() ?? string.Empty : string.Empty;

                    if (target == "SubscribeToPlayers")
                    {
                        var response = new
                        {
                            type = 3,
                            invocationId = invocationId,
                            result = (object?)null
                        };
                        await SendJsonAsync(connectionId, socket, response);
                    }
                }
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[ParseError] {ex.Message}");
            }
        }

        private static async Task SendHandshakeAsync(string connectionId, WebSocket socket)
        {
            var handshake = new { protocol = "json", version = 1 };
            await SendJsonAsync(connectionId, socket, handshake);
        }

        private static async Task PingLoopAsync(string connectionId, WebSocket socket, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var ping = new { type = 6 };
                    await SendJsonAsync(connectionId, socket, ping);
                    await Task.Delay(10000, token);
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                //Console.WriteLine($"[PingError] {ex.Message}");
            }
        }

        public static async Task SendNotificationToPlayer(long playerId, object message)
        {
            foreach (var connectionId in SnapshotConnections(playerId))
            {
                if (WebSockets.TryGetValue(connectionId, out var socket) && socket.State == WebSocketState.Open)
                {
                    await SendJsonAsync(connectionId, socket, message);
                }
            }
        }

        public static async Task SendToPlayer(long playerId, string message)
        {
            var connectionIds = SnapshotConnections(playerId);
            if (connectionIds.Length == 0)
                return;

            var tasks = new List<Task>();

            foreach (var connectionId in connectionIds)
            {
                if (WebSockets.TryGetValue(connectionId, out var socket) && socket != null)
                {
                    tasks.Add(SendToSocketAsync(connectionId, socket, message));
                }
            }

            await Task.WhenAll(tasks);
        }

        public static async Task SendRawToPlayer(long playerId, byte[] payload)
        {
            var connectionIds = SnapshotConnections(playerId);
            if (connectionIds.Length == 0)
                return;

            var tasks = new List<Task>();

            foreach (var connectionId in connectionIds)
            {
                if (WebSockets.TryGetValue(connectionId, out var socket) && socket != null)
                {
                    tasks.Add(SendRawToSocketAsync(connectionId, socket, payload));
                }
            }

            await Task.WhenAll(tasks);
        }

        public static async Task SendToAll(string message)
        {
            var tasks = WebSockets
                .Select(kv => SendToSocketAsync(kv.Key, kv.Value, message));

            await Task.WhenAll(tasks);
        }

        public static async Task SendToPlayers(List<long> playerIds, string message)
        {
            var tasks = new List<Task>();

            foreach (var id in playerIds)
            {
                foreach (var connectionId in SnapshotConnections(id))
                {
                    if (WebSockets.TryGetValue(connectionId, out var socket) && socket != null)
                    {
                        tasks.Add(SendToSocketAsync(connectionId, socket, message));
                    }
                }
            }

            await Task.WhenAll(tasks);
        }

        public static async Task SendClubMembershipUpdate(long toPlayerId, long clubMemberId, long accountId, long clubId, int membershipType, DateTime createdAt, int invitedMembershipType = 0)
        {
            var json = JsonConvert.SerializeObject(WebsocketEvents.CreateClubMembershipUpdateResponse(clubMemberId, accountId, clubId, membershipType, createdAt, invitedMembershipType));
            await SendToPlayer(toPlayerId, json);
        }

        public static async Task BroadcastClubMembershipUpdate(long clubMemberId, long accountId, long clubId, int membershipType, DateTime createdAt, int invitedMembershipType = 0)
        {
            var json = JsonConvert.SerializeObject(WebsocketEvents.CreateClubMembershipUpdateResponse(clubMemberId, accountId, clubId, membershipType, createdAt, invitedMembershipType));
            await SendToAll(json);
        }

        public static async Task SendCreatorClubSubscriptionUpdate(long toPlayerId, long creatorAccountId, long clubId, int membershipType)
        {
            var json = JsonConvert.SerializeObject(WebsocketEvents.CreateCreatorClubSubscriptionUpdateResponse(creatorAccountId, clubId, membershipType));
            await SendToPlayer(toPlayerId, json);
        }

        public static async Task BroadcastCreatorClubSubscriptionUpdate(long creatorAccountId, long clubId, int membershipType)
        {
            var json = JsonConvert.SerializeObject(WebsocketEvents.CreateCreatorClubSubscriptionUpdateResponse(creatorAccountId, clubId, membershipType));
            await SendToAll(json);
        }

        private static string[] SnapshotConnections(long playerId)
        {
            if (!PlayerConnections.TryGetValue(playerId, out var set))
                return Array.Empty<string>();
            lock (set) { return set.ToArray(); }
        }

        private static async Task SendToSocketAsync(string connectionId, WebSocket socket, string message)
        {
            var packet = new SockSignalR
            {
                type = MessageTypes.Invocation,
                target = "Notification",
                arguments = new object[] { message },
                nonblocking = true,
                result = "200 OK"
            };

            var data = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(packet));
            var buffer = new byte[data.Length + Separator.Length];
            Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
            Buffer.BlockCopy(Separator, 0, buffer, data.Length, Separator.Length);

            await SendBytesAsync(connectionId, socket, buffer, WebSocketMessageType.Text);
        }

        private static async Task SendRawToSocketAsync(string connectionId, WebSocket socket, byte[] payload)
        {
            await SendBytesAsync(connectionId, socket, payload, WebSocketMessageType.Binary);
        }

        private static async Task SendJsonAsync(string connectionId, WebSocket socket, object obj)
        {
            var bytes = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(obj) + "\x1e");
            await SendBytesAsync(connectionId, socket, bytes, WebSocketMessageType.Text);
        }

        private static async Task SendBytesAsync(string connectionId, WebSocket socket, byte[] buffer, WebSocketMessageType type)
        {
            var gate = SendGates.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (socket.State != WebSocketState.Open)
                    return;

                await socket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    type,
                    endOfMessage: true,
                    cancellationToken: CancellationToken.None
                );
            }
            catch (Exception)
            {
                try
                {
                    if (socket.State != WebSocketState.Closed && socket.State != WebSocketState.Aborted)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Send failed", CancellationToken.None);
                    }
                }
                catch
                {
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private class SockSignalR
        {
            public MessageTypes type { get; set; }
            public string? target { get; set; }
            public object[]? arguments { get; set; }
            public bool nonblocking { get; set; }
            public object? result { get; set; }
        }

        private enum MessageTypes
        {
            Invocation = 1,
            StreamItem,
            Completion,
            StreamInvocation,
            CancelInvocation,
            Ping,
            Close
        }

        public static async Task<bool> RefreshAccount(long accountId)
        {
            try
            {
                var selfDTO = PlayerDB.GetAccountMe(accountId);
                var globalDTO = PlayerDB.GetPlayerDTOById(accountId, accountMe: false);

                var selfUpdate = JsonConvert.SerializeObject(WebsocketEvents.CreateWSEventString("SelfAccountUpdate", selfDTO));
                var globalUpdate = JsonConvert.SerializeObject(WebsocketEvents.CreateWSEventString("AccountUpdate", globalDTO));

                var notifySelfTask = SendToPlayer(accountId, selfUpdate);
                var notifyAllTask = SendToAll(globalUpdate);

                await Task.WhenAll(notifySelfTask, notifyAllTask);
                return true;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Failed to send notification: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> RefreshRoom(long roomId)
        {
            try
            {
                var room = RoomDB.GetRoom(roomId);

                var globalUpdate = JsonConvert.SerializeObject(WebsocketEvents.CreateWSEventString("RoomUpdate", room));

                await SendToAll(globalUpdate);
                return true;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Failed to send notification: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> RefreshHeartbeat(long playerId, PlayerDBClasses.Heartbeat newHeartbeat)
        {
            try
            {
                var globalUpdate = JsonConvert.SerializeObject(WebsocketEvents.CreateWSEventString("PresenceUpdate", newHeartbeat));

//<<<<<<< HEAD
                var friendIds = FriendsDB.GetRelationships(playerId)
                    .Select(r => r.OtherPlayerID)
                    .ToList();

                friendIds.Add(playerId);

                await SendToPlayers(friendIds, globalUpdate);
/*=======
                // Scoped to friends: SendToAll here makes every in-room client refresh and dump to dorm.
                var recipients = FriendsDB.GetRelationships(playerId)
                    .Where(r => r.RelationshipType == Vanadium.Classes.DBs.DBClasses.FriendsDBClasses.RelationshipType.Friend)
                    .Select(r => r.OtherPlayerID)
                    .ToList();
                recipients.Add(playerId);

                await SendToPlayers(recipients, globalUpdate);
>>>>>>> 1a0e5b1d7a6cffb81e5d328fc59daaf33d9082f4*/
                return true;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Failed to send notification: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> CreateAndSendWSMessageRecieved(
            long toPlayerId,
            long fromPlayerId,
            MessageType type,
            string? data,
            long? roomId = null,
            long? playerEventId = null)
        {
            try
            {
                var message = PlayerDB.AddMessage(
                    toPlayerId,
                    fromPlayerId,
                    type,
                    data,
                    roomId,
                    playerEventId
                );

                if (message == null)
                    return false;

                var wsEventTo = WebsocketEvents.CreateMessageResponse(message, toPlayerId);
                var wsEventFrom = WebsocketEvents.CreateMessageResponse(message, fromPlayerId);

                await Task.WhenAll(
                    SendToPlayer(toPlayerId, JsonConvert.SerializeObject(wsEventTo)),
                    SendToPlayer(fromPlayerId, JsonConvert.SerializeObject(wsEventFrom))
                );

                return true;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"WS MessageReceived failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> SendBanned(long accountId, PlayerDBClasses.ModerationBlockDetails mbd)
        {
            try
            {
                mbd.Message = await APIController.FormatModerationBlockMessageString(mbd);
                var selfUpdate = JsonConvert.SerializeObject(WebsocketEvents.CreateBanResponse(mbd));
                await SendToPlayer(accountId, selfUpdate);
                return true;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Failed to send notification: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> RefreshHeartbeatsBulk(List<long> playerIds)
        {
            try
            {
                var tasks = playerIds.Select(async id =>
                {
                    var hb = PlayerDB.GetPlayerHeartbeat(id);
                    if (hb == null) return;

                    var updateString = JsonConvert.SerializeObject(WebsocketEvents.CreateWSEventString("PresenceUpdate", hb));

                    var friendIds = FriendsDB.GetRelationships(id)
                        .Select(r => r.OtherPlayerID)
                        .ToList();

                    friendIds.Add(id);

                    await SendToPlayers(friendIds, updateString);
                });

                await Task.WhenAll(tasks);
                return true;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[Notifications] Bulk refresh failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> BalanceUpdate(long accountId)
        {
            try
            {
                var selfDTO = PlayerDB.GetPlayerDTOById(accountId, accountMe: true);
                var selfUpdate = JsonConvert.SerializeObject(WebsocketEvents.CreateWSEventString("SelfAccountUpdate", selfDTO));
                await SendToPlayer(accountId, selfUpdate);
                return true;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Failed to send notification: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> GameRewardReceived(long accountId)
        {
            try
            {
                var selfDTO = PlayerDB.GetPlayerDTOById(accountId, accountMe: true);
                var selfUpdate = JsonConvert.SerializeObject(WebsocketEvents.CreateWSEventString("SelfAccountUpdate", selfDTO));
                await SendToPlayer(accountId, selfUpdate);
                return true;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Failed to send notification: {ex.Message}");
                return false;
            }
        }

        public static async Task SendEvent(long playerId, string eventType, object payload)
        {
            try
            {
                var message = JsonConvert.SerializeObject(new WebsocketEvents.Response
                {
                    Id = eventType,
                    Msg = payload
                });

                await SendToPlayer(playerId, message);
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Failed to send event: {ex.Message}");
            }
        }

        public static async Task SendMaintenance(int minutes)
        {
            var safeMinutes = Math.Clamp(minutes, 1, 1440);
            var json = JsonConvert.SerializeObject(new WebsocketEvents.Response
            {
                Id = ((int)WebsocketEvents.ResponseResults.ServerMaintenance).ToString(),
                Msg = new { StartsInMinutes = safeMinutes }
            });
            await SendToAll(json);
        }

        public static async Task SendAnnoucementUpdate(AnnoucementDBClasses.Announcement announcement)
        {
            var json = JsonConvert.SerializeObject(new
            {
                Id = "AnnouncementUpdate",
                Msg = announcement
            });
            await SendToAll(json);
        }

        public static async Task SendAnnoucementDelete(long announcementId)
        {
            var json = JsonConvert.SerializeObject(new
            {
                Id = "AnnouncementDelete",
                Msg = new
                {
                    AnnouncementId = announcementId
                }
            });
            await SendToAll(json);
        }

        public static async Task SendCoahMessage(string msg)
        {
            var json = JsonConvert.SerializeObject(new
            {
                Id = "2",
                Msg = new
                {
                    FromPlayerId = 1,
                    Id = Random.Shared.Next(1, 0x7ffffff),
                    SentTime = DateTime.UtcNow,
                    Type = 100,
                    Data = msg,
                    RoomId = 1
                }
            });
            await SendToAll(json);
        }

        public static async Task SendAccountUpdate(long playerId, object? accountData)
        {
            var json = JsonConvert.SerializeObject(new WebsocketEvents.Response
            {
                Id = NotiEventTypes.SelfAccountUpdate,
                Msg = accountData
            });
            await SendToPlayer(playerId, json);
        }

        public static async Task BroadcastAccountUpdate(long playerId, object? publicDto)
        {
            var json = JsonConvert.SerializeObject(new WebsocketEvents.Response
            {
                Id = NotiEventTypes.AccountUpdate,
                Msg = publicDto
            });
            await SendToAll(json);
        }

        public static async Task SendRoomUpdate(long playerId, object room)
        {
            var json = JsonConvert.SerializeObject(new WebsocketEvents.Response
            {
                Id = NotiEventTypes.RoomUpdate,
                Msg = room
            });
            await SendToPlayer(playerId, json);
        }

        public static async Task SendPresenceUpdate(long playerId, object heartbeat)
        {
            var json = JsonConvert.SerializeObject(new WebsocketEvents.Response
            {
                Id = NotiEventTypes.PresenceUpdate,
                Msg = heartbeat
            });
            await SendToPlayer(playerId, json);
        }

        public static async Task SendReputationUpdate(long playerId, object reputation)
        {
            var json = JsonConvert.SerializeObject(new WebsocketEvents.Response
            {
                Id = NotiEventTypes.ReputationUpdate,
                Msg = reputation
            });
            await SendToPlayer(playerId, json);
        }

        public static async Task SendInvite(long playerId, long fromPlayerId, long roomId, MatchDBClasses.InviteDTO invite)
        {
            var json = JsonConvert.SerializeObject(new
            {
                Id = "2",
                Msg = new
                {
                    FromPlayerId = fromPlayerId,
                    Id = Random.Shared.Next(1, 0x7ffffff),
                    SentTime = DateTime.UtcNow,
                    Type = MessageType.GameInviteV2,
                    Data = JsonConvert.SerializeObject(invite),
                    RoomId = roomId,
                    PlayerEventId = (long?)null
                }
            });
            await SendToPlayer(playerId, json);
        }

        public static async Task SendPlayerOnline(long fromPlayerId, MatchDBClasses.PlayerOnlineDTO invite)
        {
            var json = JsonConvert.SerializeObject(new
            {
                Id = "2",
                Msg = new
                {
                    FromPlayerId = fromPlayerId,
                    Id = Random.Shared.Next(1, 0x7ffffff),
                    SentTime = DateTime.UtcNow,
                    Type = MessageType.FriendStatusOnline,
                    Data = JsonConvert.SerializeObject(invite),
                    RoomId = (long?)null,
                    PlayerEventId = (long?)null
                }
            });
            var relationships = FriendsDB.GetRelationships(fromPlayerId);
            var friendIds = relationships
                .Where(r => r.RelationshipType == Vanadium.Classes.DBs.DBClasses.FriendsDBClasses.RelationshipType.Friend)
                .Select(r => r.OtherPlayerID)
                .Where(friendId =>
                {
                    var theirRel = FriendsDB.GetRelationships(friendId)
                        .FirstOrDefault(x => x.OtherPlayerID == fromPlayerId);
                    return theirRel?.Favorited == Vanadium.Classes.DBs.DBClasses.FriendsDBClasses.ReciprocalStatus.Local
                        || theirRel?.Favorited == Vanadium.Classes.DBs.DBClasses.FriendsDBClasses.ReciprocalStatus.Mutual;
                })
                .ToList();
            await SendToPlayers(friendIds, json);
        }
        
        public static async Task SendPartyActivitySwitch(long fromPlayerId, List<long> targetPlayerIds, long roomId, Heartbeat heartbeat)
        {
            var invite = new MatchDBClasses.InviteDTO
            {
                InviteId = heartbeat?.roomInstance?.roomInstanceId ?? 0,
                Name = heartbeat?.roomInstance?.Name ?? "Unknown Room",
                InviteMode = 0
            };

            var json = JsonConvert.SerializeObject(new
            {
                Id = "2",
                Msg = new
                {
                    FromPlayerId = fromPlayerId,
                    Id = Random.Shared.Next(1, 0x7ffffff),
                    SentTime = DateTime.UtcNow,
                    Type = WebsocketEvents.WebsocketMessageType.PartyActivitySwitchV2,
                    Data = JsonConvert.SerializeObject(invite),
                    RoomId = roomId,
                    PlayerEventId = (long?)null
                }
            });

            foreach (var pid in targetPlayerIds)
                await SendToPlayer(pid, json);
        }

        public static async Task SendPlayerCheer(long toPlayerId, long fromPlayerId, int cheerCategory)
		{
			var json = JsonConvert.SerializeObject(new
			{
				Id = "2",
				Msg = new
				{
					FromPlayerId = fromPlayerId,
					Id = Random.Shared.Next(1, 0x7ffffff),
					SentTime = DateTime.UtcNow,
					Type = MessageType.PlayerCheer,
					Data = cheerCategory.ToString(),
					RoomId = (long?)null,
					PlayerEventId = (long?)null
				}
			});
			await SendToPlayer(toPlayerId, json);
		}
        
        public static async Task SendPlayerCheerAnonymous(long toPlayerId, long fromPlayerId, int cheerCategory)
		{
			var json = JsonConvert.SerializeObject(new
			{
				Id = "2",
				Msg = new
				{
					FromPlayerId = fromPlayerId,
					Id = Random.Shared.Next(1, 0x7ffffff),
					SentTime = DateTime.UtcNow,
					Type = MessageType.PlayerCheerAnonymous,
					Data = cheerCategory.ToString(),
					RoomId = (long?)null,
					PlayerEventId = (long?)null
				}
			});
			await SendToPlayer(toPlayerId, json);
		}

        public static async Task SendMessage(long toPlayerId, long fromPlayerId, int type, string data)
        {
            var json = JsonConvert.SerializeObject(new
            {
                Id = "2",
                Msg = new
                {
                    FromPlayerId = fromPlayerId,
                    Id = Random.Shared.Next(1, 0x7ffffff),
                    SentTime = DateTime.UtcNow,
                    Type = type,
                    Data = data,
                    RoomId = (long?)null,
                    PlayerEventId = (long?)null
                }
            });
            await SendToPlayer(toPlayerId, json);
        }
    }

    [ApiController]
    [Route("api/messages/v2")] // request to join player
    public class MessagesController : Controller
    {
        [HttpPost("send")]
        public async Task<IActionResult> SendMessage(
            [FromForm] long ToPlayerId,
            [FromForm] int Type,
            [FromForm] string Data = "")
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            await NotificationsController.SendMessage(ToPlayerId, id.Value, Type, Data);

            return Ok(new { success = true });
        }
    }

    public class NotificationStartupHeartbeatCleanupService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            try
            {
                NotificationsController.ReconcileStartupOnlineHeartbeats(stoppingToken);
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[Notifications] Startup heartbeat reconcile failed: {ex.Message}");
            }
        }
    } 
}