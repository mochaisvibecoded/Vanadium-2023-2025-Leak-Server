using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using static Vanadium.Classes.DBs.DBClasses.RoomDBClasses;
using Vanadium.Classes.WebSocket;
using Vanadium.Utils.NotiController;
using Vanadium.Controllers;
using System.Collections.Concurrent;

namespace Vanadium.Controllers
{
    [ApiController]
    [Route("/match")]
    public partial class MatchController : ControllerBase
    {
        private static readonly ConcurrentDictionary<long, (long RoomId, DateTime MatchmadeAt)> _authorizedRoomSessions = new();
        private static readonly ConcurrentDictionary<(long, long), (int Count, DateTime Window)> _inviteRateLimit = new();

        private static readonly string PhotonChatAppId = "9c035c47-8725-43d4-8c53-ab45ed381511";

        private static readonly Random _photonRng = new Random();

        public static long EnsurePlayerDorm(FullPlayer player)
        {
            long dormRoomId = player.Player.PlayerExtra.DormRoomId;

            if (dormRoomId > 0 && RoomDB.DoesPlayerDormExist(player.PlayerId))
                return dormRoomId;

            var dormRoom = RoomDB.GetPlayerDormRoom(player.PlayerId);
            if (dormRoom != null)
            {
                dormRoomId = dormRoom.RoomId;
            }
            else
            {
                dormRoomId = RoomDB.CloneDormRoom(player.PlayerId);
            }

            PlayerDB.UpdateDormRoomId(player.PlayerId, dormRoomId);
            player.Player.PlayerExtra.DormRoomId = dormRoomId;
            return dormRoomId;
        }

        private static void AuthorizeRoomSession(long playerId, long roomId)
        {
            _authorizedRoomSessions[playerId] = (roomId, DateTime.UtcNow);
        }

        private static void ClearRoomSession(long playerId)
        {
            _authorizedRoomSessions.TryRemove(playerId, out _);
        }

        private static bool IsRoomSessionAuthorized(long playerId, long roomId)
        {
            if (!_authorizedRoomSessions.TryGetValue(playerId, out var session))
                return false;
            return session.RoomId == roomId && (DateTime.UtcNow - session.MatchmadeAt).TotalHours < 12;
        }

        private static void KickPlayer(long playerId)
        {
        }

        private static bool IsBannedFromRoom(Room? room, long playerId)
        {
            return room?.Roles?.Any(r => r.AccountId == playerId && r.Role == Role.Banned) ?? false;
        }

        [HttpGet("player")]
        public async Task<IActionResult> GetPlayerHeartbeatBulk([FromQuery] List<long> id)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            return Ok(PlayerDB.GetPlayerHeartbeatsBulk(id));
        }

        [HttpPost("roominstance/{roomInstanceId}/reportjoinresult")]
        public async Task<IActionResult> ReportJoinResultForRoomInstanceId(ulong roomInstanceId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            long resolvedAccountId = player.PlayerId;
            long newLevel = player.Player.Level;
            long newXP = player.Player.XP;

            _ = NotiController.SendEvent(resolvedAccountId, "PlayerProgressionXPUpdate", new
            {
                PlayerId = resolvedAccountId,
                Level = newLevel,
                XP = newXP
            });

            _ = NotiController.SendEvent(resolvedAccountId, "PlayerProgressionLevelUpdate", new
            {
                PlayerId = resolvedAccountId,
                Level = newLevel,
                XP = newXP
            });

            return Ok();
        }

        [HttpPut("roominstance/{roomInstanceId}/markprivate")]
        public async Task<IActionResult> MarkRoomInstancePrivate(long roomInstanceId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            Sessions.MarkInstancePrivate(roomInstanceId);
            Sessions.FakeInstanceFull(roomInstanceId);

            var playersInInstance = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == roomInstanceId)
                .ToList();

            foreach (var fp in playersInInstance)
            {
                if (fp.Player?.PlayerExtra?.Heartbeat?.roomInstance == null) continue;
                fp.Player.PlayerExtra.Heartbeat.roomInstance.isPrivate = true;
                PlayerDB.Players.Update(fp);
            }

            var outsiderIds = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true
                         && p.Player.PlayerExtra.Heartbeat.roomInstance?.roomInstanceId != roomInstanceId)
                .Select(p => p.PlayerId)
                .ToList();

            foreach (var fp in playersInInstance)
            {
                if (fp.Player?.PlayerExtra?.Heartbeat == null) continue;

                var hbJson = Newtonsoft.Json.JsonConvert.SerializeObject(new WebsocketEvents.Response
                {
                    Id = NotificationsController.EventTypes.PresenceUpdate,
                    Msg = fp.Player.PlayerExtra.Heartbeat
                });

                await NotificationsController.SendToPlayer(fp.PlayerId, hbJson);
                await NotificationsController.SendToPlayers(outsiderIds, hbJson);
            }

            return Ok(new StorefrontsDBClasses.SuccessResult());
        }

        private static HashSet<long> RecRoomPlusCheckWhitelisgt()
        {
            var path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "RRPlus.txt");
            if (!System.IO.File.Exists(path))
                return new HashSet<long>();

            var ids = new HashSet<long>();
            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                if (long.TryParse(line.Trim(), out var id))
                    ids.Add(id);
            }
            return ids;
        }

        [HttpGet("rooms/requiring/developer")]
        public async Task<IActionResult> RoomsRequiringDeveloper()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("rooms/requiring/rrplus")]
        public async Task<IActionResult> RoomsRequiringRRPlus()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            return Ok(ServerConfig.Bracket);
        }

        [HttpPut("player/statusvisibility")]
        public IActionResult setStatusVisibilityShit()
        {
            return Ok(new { success = true });
        }

        [HttpPut("player/photonregionpings")]
        public IActionResult setPhotonRegionShit()
        {
            return Ok(new { success = true });
        }

        [HttpGet("room/{roomId}/instances")] // ripped from other code only for reference? i only needed the response data
        public async Task<IActionResult> GetRoomInstances(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return NotFound("");

            var currentPlayer = PlayerDB.GetCurrentPlayer((long)id);

            bool isDeveloper = currentPlayer?.PlayerRoles?.Any(r =>
                    r == PlayerDBClasses.PlayerRoles.Developer) ?? false;

            bool hasRoomModRole = room.CreatorAccountId == (long)id || (room.Roles?.Any(r =>
                r.AccountId == (long)id &&
                (int)r.Role >= (int)Role.Moderator) ?? false);

            if (!isDeveloper && !hasRoomModRole)
                return StatusCode(403, "Permission Denied");

            var allPlayers = PlayerDB.Players.FindAll()
            .Where(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance != null
                     && p.Player.PlayerExtra.Heartbeat.isOnline)
            .ToList();

            var instanceGroups = allPlayers
                .Where(p => p.Player.PlayerExtra.Heartbeat.roomInstance.roomId == roomId)
                .GroupBy(p => p.Player.PlayerExtra.Heartbeat.roomInstance.roomInstanceId)
                .ToList();

            if (!instanceGroups.Any())
                return Ok(Array.Empty<object>());

            bool canViewAll = isDeveloper || room.CreatorAccountId == (long)id || (room.Roles?.Any(r =>
                r.AccountId == (long)id &&
                (int)r.Role >= (int)Role.Moderator) ?? false);

            var result = instanceGroups
                .Where(g =>
                {
                    var sample = g.First();
                    var instance = sample.Player.PlayerExtra.Heartbeat.roomInstance;
                    return canViewAll || instance.isPrivate != true;
                })
                .OrderByDescending(g => g.Count())
                .Select(g =>
                {
                    var sample = g.First().Player.PlayerExtra.Heartbeat.roomInstance;
                    return new Dictionary<string, object>
                    {
                        ["RoomInstanceId"] = sample.roomInstanceId,
                        ["RoomId"] = roomId,
                        ["SubRoomId"] = sample.subRoomId,
                        ["IsFull"] = Sessions.IsInstanceActuallyFull(sample.roomInstanceId),
                        ["MaxCapacity"] = sample.maxCapacity,
                        ["IsPrivate"] = sample.isPrivate,
                        ["PlayerIds"] = g.Select(p => p.PlayerId).ToList()
                    };
                })
                .ToList();

            return Ok(result);
        }

        [HttpPost("player/exclusivelogin")]
        public async Task<IActionResult> PlayerExclusiveLogin()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            await NotificationsController.SendPlayerOnline(id.Value, new MatchDBClasses.PlayerOnlineDTO() { SenderIsMyFavoriteFriend = true });

            return Ok();
        }

        [HttpPost("player/login")]
        public async Task<IActionResult> PlayerLogin()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id != null)
            {
                ClearRoomSession(id.Value);
                PlayerDB.UpdateLastLoginAt(id.Value);
                PlayerDB.UpdatePlayerHeartbeat(id.Value, null, online: true);

                var someRequiredSetings = new[]
                {
                    ("Recroom.AccountCreation.HasStarted", "True"),
                    ("Recroom.AccountCreation.HasChosenUsername", "False"),
                    ("Recroom.AccountCreation.HasCreatedPassword", "True"),
                    ("Recroom.AccountCreation.HasFinished", "False"),
                    ("TUTORIAL_COMPLETE_MASK", "77")
                };

                var existingSettings = PlayerDB.GetPlayerSettings(id.Value);

                foreach (var (key, value) in someRequiredSetings)
                {
                    if (!existingSettings.Any(s => s.Key == key))
                        PlayerDB.SetPlayerSetting(key, value, id.Value);
                }
            }

            return Ok();
        }

        [HttpPost("player/logout")]
        public async Task<IActionResult> PlayerLogout()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id != null)
            {
                ClearRoomSession(id.Value);
                PlayerDB.UpdatePlayerHeartbeat(id.Value, null, online: false);
            }

            return Ok();
        }

        [HttpPost("player/heartbeat")]
        public async Task<IActionResult> PostPlayerHeartbeat()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var playerLock = PlayerDB.GetPlayerLock(id.Value);
            await playerLock.WaitAsync();
            FullPlayer player;
            Heartbeat hb;
            try
            {
                player = PlayerDB.Players.FindById(id.Value);
                if (player?.Player?.PlayerExtra == null)
                    return Unauthorized("");

                hb = player.Player.PlayerExtra.Heartbeat ?? new Heartbeat();

                if (hb.roomInstance != null)
                {
                    long claimedRoomId = hb.roomInstance.roomId;

                    if (!IsRoomSessionAuthorized(id.Value, claimedRoomId))
                    {
                    }
                }

                hb.playerId = id.Value;
                hb.isOnline = true;
                hb.errorCode = 0;
                hb.photonAuthToken = null;
                hb.appVersion = player.Player.PlayerExtra.Heartbeat?.appVersion ?? hb.appVersion;

                if (Request.HasFormContentType)
                {
                    var form = await Request.ReadFormAsync();
                    if (form.TryGetValue("DeviceClass", out var dc) && int.TryParse(dc, out var dcVal))
                        hb.deviceClass = (DeviceClasses)dcVal;
                    if (form.TryGetValue("StatusVisibility", out var sv) && int.TryParse(sv, out var svVal))
                        hb.statusVisibility = (StatusVisibility)svVal;
                    if (form.TryGetValue("VRMovementMode", out var vrm) && int.TryParse(vrm, out var vrmVal))
                        hb.vrMovementMode = vrmVal;
                }

                player.Player.PlayerExtra.Heartbeat = hb;
                PlayerDB.Players.Update(player);

                await Services.ToxModService.EnsureCurrent(player);
            }
            finally
            {
                playerLock.Release();
            }

            /*var (xpAwarded, leveledUp, newLevel, newXP) = PlayerDB.AwardHeartbeatXP(id.Value);

            if (xpAwarded)
            {
                var progressionPayload = Newtonsoft.Json.JsonConvert.SerializeObject(
                    WebsocketEvents.CreateLevelUpdateResponse(id.Value, newLevel, newXP));

                await NotificationsController.SendToPlayer(id.Value, progressionPayload);
            }*/

            return Ok(hb);
        }

        [HttpPost("matchmake/none")]
        public async Task<IActionResult> MatchmakeNone()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            ClearRoomSession(id.Value);

            var player = PlayerDB.GetCurrentPlayer(id.Value);
            if (player != null)
                EnsurePlayerDorm(player);

            var heartbeat = PlayerDB.GetPlayerHeartbeat(id.Value);

            if (heartbeat != null)
                await NotificationsController.RefreshHeartbeat(id.Value, heartbeat);

            return Ok(heartbeat);
        }

        [HttpPost("goto/room/{roomName}")]
        public async Task<IActionResult> MatchmakeByRoomName(string roomName)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                return Ok(new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                });
            }

            if (roomName.Equals("DormRoom", StringComparison.OrdinalIgnoreCase))
            {
                long dormRoomId = EnsurePlayerDorm(player);
                AuthorizeRoomSession(player.PlayerId, dormRoomId);

                RoomDB.IncrementRoomVisitCount(dormRoomId);
                PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, dormRoomId);

                var dormHeartbeat = Sessions.CreateDorm(player.PlayerId, player.Player.Username, dormRoomId);
                if (PlayerDB.IsBanned(player.PlayerId))
                    dormHeartbeat.roomInstance.maxCapacity = 1;

                await NotificationsController.RefreshHeartbeat(player.PlayerId, dormHeartbeat);
                return Ok(dormHeartbeat);
            }

            var room = RoomDB.GetRoomByName(roomName);
            if (room == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.NoSuchRoom,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            long roomId = room.RoomId;

            if (IsBannedFromRoom(room, player.PlayerId))
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.BannedFromRoom,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            bool isDeveloper = player.PlayerRoles?.Any(r => r == PlayerDBClasses.PlayerRoles.Developer) ?? false;
            bool isCreator = room.CreatorAccountId == player.PlayerId;
            bool hasAccess = room.Roles?.Any(r =>
                r.AccountId == player.PlayerId && (int)r.Role >= (int)Role.CoOwner) ?? false;
            bool isOwnerOrCoOwner = isCreator || hasAccess;

            bool hasOwner = room.CreatorAccountId > 0 ||
                (room.Roles?.Any(r => (int)r.Role >= (int)Role.CoOwner) ?? false);

            if (!hasOwner)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.AccountDoesNotExist,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (room.Accessibility == RoomAccessibility.Dev_only || room.Accessibility == RoomAccessibility.Dev_Unlisted)
            {
                if (!isDeveloper && !isCreator && !hasAccess)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.DeveloperOnly,
                        isOnline = true,
                        playerId = player.PlayerId
                    });
            }
            else if (room.Accessibility == RoomAccessibility.Private)
            {
                if (!isCreator && !hasAccess)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.RoomIsPrivate,
                        isOnline = true,
                        playerId = player.PlayerId
                    });
            }

            if (!isOwnerOrCoOwner)
            {
                if (player.Player?.IsJunior == true && room.SupportsJuniors == false)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.JuniorNotAllowed,
                        isOnline = true,
                        playerId = player.PlayerId
                    });

                if (room.MinLevel > 0 && (player.Player?.Level ?? 1) < room.MinLevel)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.LevelTooLow,
                        isOnline = true,
                        playerId = player.PlayerId
                    });
            }

            var (joinMode, autoFollow, _, additionalPlayerIds) = await ParseMatchmakeForm(Request);

            RoomDB.IncrementRoomVisitCount(roomId);
            PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, roomId);

            AuthorizeRoomSession(player.PlayerId, roomId);

            var heartbeat = Sessions.CreateRoom(
                player.PlayerId,
                roomId,
                null,
                joinMode: joinMode,
                additionalPlayersAutoFollow: autoFollow);

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            if (additionalPlayerIds.Count > 0 && autoFollow)
                await ShovePlayersToInstance(additionalPlayerIds, roomId, 0, heartbeat);
            else if (additionalPlayerIds.Count > 0)
                await NotificationsController.SendPartyActivitySwitch(player.PlayerId, additionalPlayerIds, roomId, heartbeat);

            return Ok(heartbeat);
        }

        [HttpPost("matchmake/dorm")]
        public async Task<IActionResult> MatchmakeDorm()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            long dormRoomId = EnsurePlayerDorm(player);
            AuthorizeRoomSession(player.PlayerId, dormRoomId);

            RoomDB.IncrementRoomVisitCount(dormRoomId);
            PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, dormRoomId);

            var heartbeat = Sessions.CreateDorm(player.PlayerId, player.Player.Username, dormRoomId);
            if (PlayerDB.IsBanned(player.PlayerId))
                heartbeat.roomInstance.maxCapacity = 1;

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            return Ok(heartbeat);
        }

        [HttpPost("matchmake/event/{eventId}")]
        public async Task<IActionResult> MatchmakeEvent(long eventId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                return Ok(new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                });
            }

            var ev = Vanadium.Classes.DBs.EventDB.GetPlayerEventById(eventId);
            if (ev == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.NoSuchGame,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            long roomId = ev.RoomId;
            long? subRoomId = ev.SubRoomId;

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.NoSuchRoom,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (IsBannedFromRoom(room, player.PlayerId))
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.BannedFromRoom,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            var (joinMode, autoFollow, _, additionalPlayerIds) = await ParseMatchmakeForm(Request);

            RoomDB.IncrementRoomVisitCount(roomId);
            PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, roomId);
            AuthorizeRoomSession(player.PlayerId, roomId);

            var heartbeat = Sessions.CreateRoom(
                player.PlayerId,
                roomId,
                subRoomId,
                joinMode: joinMode,
                additionalPlayersAutoFollow: autoFollow);

            if (heartbeat?.roomInstance != null)
                heartbeat.roomInstance.eventId = eventId;

            var fp = PlayerDB.Players.FindById(player.PlayerId);
            if (fp?.Player?.PlayerExtra != null)
            {
                fp.Player.PlayerExtra.Heartbeat = heartbeat;
                PlayerDB.Players.Update(fp);
            }

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            if (additionalPlayerIds.Count > 0 && autoFollow)
                await ShovePlayersToInstance(additionalPlayerIds, roomId, subRoomId ?? 0, heartbeat);
            else if (additionalPlayerIds.Count > 0)
                await NotificationsController.SendPartyActivitySwitch(player.PlayerId, additionalPlayerIds, roomId, heartbeat);

            return Ok(heartbeat);
        }

        [HttpPost("matchmake/club/{clubId}")]
        public async Task<IActionResult> MatchmakeToClub(long clubId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
                return StatusCode(403);

            var targetClub = Classes.DBs.ClubsDB.GetClub(clubId);
            if (targetClub == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.NoSuchClub,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            long roomId = targetClub.ClubhouseRoomId ?? 0;
            if (roomId <= 0)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.ClubHasNoClubhouse,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.ClubHasNoClubhouse,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (IsBannedFromRoom(room, player.PlayerId))
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.BannedFromClub,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            bool isDeveloper = player.PlayerRoles?.Any(r => r == PlayerDBClasses.PlayerRoles.Developer) ?? false;
            bool isCreator = room.CreatorAccountId == player.PlayerId;
            bool hasAccess = room.Roles?.Any(r =>
                r.AccountId == player.PlayerId && (int)r.Role >= (int)Role.CoOwner) ?? false;
            bool isOwnerOrCoOwner = isCreator || hasAccess;

            bool hasOwner = room.CreatorAccountId > 0 ||
                (room.Roles?.Any(r => (int)r.Role >= (int)Role.CoOwner) ?? false);

            if (!hasOwner)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.AccountDoesNotExist,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (room.Accessibility == RoomAccessibility.Dev_only || room.Accessibility == RoomAccessibility.Dev_Unlisted)
            {
                if (!isDeveloper && !isCreator && !hasAccess)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.DeveloperOnly,
                        isOnline = true,
                        playerId = player.PlayerId
                    });
            }

            if (!isOwnerOrCoOwner)
            {
                if (player.Player?.IsJunior == true && room.SupportsJuniors == false)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.JuniorNotAllowed,
                        isOnline = true,
                        playerId = player.PlayerId
                    });

                if (room.MinLevel > 0 && (player.Player?.Level ?? 1) < room.MinLevel)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.LevelTooLow,
                        isOnline = true,
                        playerId = player.PlayerId
                    });
            }

            var currentInstanceHb = player.Player?.PlayerExtra?.Heartbeat;

            var targetInstance = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomId == roomId)
                .Select(p => p.Player!.PlayerExtra!.Heartbeat!.roomInstance!)
                .GroupBy(ri => ri.roomInstanceId)
                .Select(g => g.First())
                .OrderByDescending(ri => Sessions.CountPlayersInInstance(ri.roomInstanceId))
                .FirstOrDefault();

            if (targetInstance != null)
            {
                if (currentInstanceHb?.roomInstance?.roomInstanceId == targetInstance.roomInstanceId)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.AlreadyInTargetInstance,
                        isOnline = true,
                        playerId = player.PlayerId
                    });

                if (targetInstance.isFull || Sessions.CountPlayersInInstance(targetInstance.roomInstanceId) >= targetInstance.maxCapacity)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.InsufficientSpace,
                        isOnline = true,
                        playerId = player.PlayerId
                    });

                RoomDB.IncrementRoomVisitCount(roomId);
                PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, roomId);

                AuthorizeRoomSession(player.PlayerId, roomId);

                var heartbeat = PlayerDB.UpdatePlayerHeartbeat(player.PlayerId, targetInstance);
                if (heartbeat == null)
                    return StatusCode(500);

                heartbeat.playerId = player.PlayerId;

                await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

                return Ok(heartbeat);
            }

            RoomDB.IncrementRoomVisitCount(roomId);
            PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, roomId);

            AuthorizeRoomSession(player.PlayerId, roomId);

            var createdHeartbeat = Sessions.CreateRoom(player.PlayerId, roomId, joinMode: 0, additionalPlayersAutoFollow: false, roomNameOverride: targetClub.Name + " Clubhouse");
            if (createdHeartbeat == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.ClubHasNoClubhouse,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            createdHeartbeat.playerId = player.PlayerId;

            await NotificationsController.RefreshHeartbeat(player.PlayerId, createdHeartbeat);

            return Ok(createdHeartbeat);
        }

        private static async Task ShovePlayersToInstance(List<long> playerIds, long roomId, long subRoomId, Heartbeat leaderHeartbeat)
        {
            foreach (var pid in playerIds)
            {
                var fp = PlayerDB.Players.FindById(pid);
                if (fp == null) continue;

                var hb = Sessions.CreateRoom(pid, roomId, subRoomId, joinMode: 0, additionalPlayersAutoFollow: false);

                if (leaderHeartbeat.roomInstance != null && hb.roomInstance != null)
                {
                    hb.roomInstance.photonRoomId = leaderHeartbeat.roomInstance.photonRoomId;
                    hb.roomInstance.photonRegion = leaderHeartbeat.roomInstance.photonRegion;
                    hb.roomInstance.roomInstanceId = leaderHeartbeat.roomInstance.roomInstanceId;
                }

                fp.Player.PlayerExtra ??= new PlayerExtra();
                fp.Player.PlayerExtra.Heartbeat = hb;
                PlayerDB.Players.Update(fp);

                await NotificationsController.RefreshHeartbeat(pid, hb);
            }
        }

        private static async Task<(int joinMode, bool autoFollow, bool bypassMovement, List<long> additionalPlayerIds)> ParseMatchmakeForm(HttpRequest request)
        {
            int joinMode = 0;
            bool autoFollow = false;
            bool bypassMovement = false;
            var additionalPlayerIds = new List<long>();

            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync();
                if (form.TryGetValue("JoinMode", out var joinModeStr))
                    int.TryParse(joinModeStr, out joinMode);
                if (form.TryGetValue("AdditionalPlayersAutoFollow", out var autoFollowStr))
                    autoFollow = autoFollowStr.ToString().Equals("True", StringComparison.OrdinalIgnoreCase);
                if (form.TryGetValue("BypassMovementModeRestriction", out var bypassStr))
                    bypassMovement = bypassStr.ToString().Equals("True", StringComparison.OrdinalIgnoreCase);
                var additionalKey = form.Keys.FirstOrDefault(k => k.Equals("AdditionalPlayerIds", StringComparison.OrdinalIgnoreCase));
                if (additionalKey != null)
                {
                    foreach (var val in form[additionalKey])
                    {
                        if (long.TryParse(val?.Trim(), out var pid))
                            additionalPlayerIds.Add(pid);
                    }
                }
            }

            return (joinMode, autoFollow, bypassMovement, additionalPlayerIds);
        }

        [HttpPost("player/notifydisconnect")]
        public async Task<IActionResult> NotifyDisconnect()
        {
            return Ok();
        }

        [HttpPost("matchmake/instance/{roomInstanceId}")]
        public async Task<IActionResult> MatchmakeInstance(long roomInstanceId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                return Ok(new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                });
            }

            var targetInstance = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == roomInstanceId)
                .Select(p => p.Player.PlayerExtra.Heartbeat.roomInstance)
                .FirstOrDefault();

            if (targetInstance == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.NoSuchGame,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (targetInstance.isFull || Sessions.CountPlayersInInstance(roomInstanceId) >= targetInstance.maxCapacity)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.InsufficientSpace,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            var currentInstanceHb = player.Player?.PlayerExtra?.Heartbeat;
            if (currentInstanceHb?.roomInstance?.roomInstanceId == roomInstanceId)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.AlreadyInTargetInstance,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            AuthorizeRoomSession(player.PlayerId, targetInstance.roomId);

            var heartbeat = PlayerDB.UpdatePlayerHeartbeat(player.PlayerId, targetInstance);
            heartbeat.playerId = player.PlayerId;

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            var (_, autoFollowInstance, _, additionalPlayerIdsInstance) = await ParseMatchmakeForm(Request);
            if (additionalPlayerIdsInstance.Count > 0 && autoFollowInstance)
                await ShovePlayersToInstance(additionalPlayerIdsInstance, targetInstance.roomId, targetInstance.subRoomId, heartbeat);
            else if (additionalPlayerIdsInstance.Count > 0)
                await NotificationsController.SendPartyActivitySwitch(player.PlayerId, additionalPlayerIdsInstance, targetInstance.roomId, heartbeat);

            return Ok(heartbeat);
        }

        [HttpPost("matchmake/v2/instance/{roomInstanceId}")]
        public async Task<IActionResult> MatchmakeInstanceV2(long roomInstanceId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                return Ok(new { success = false, error_id = "Matchmaking.Banned", error = "You are banned.", value = new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                }});
            }

            var targetInstance = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == roomInstanceId)
                .Select(p => p.Player.PlayerExtra.Heartbeat.roomInstance)
                .FirstOrDefault();

            if (targetInstance == null)
                return Ok(new { success = false, error_id = "Matchmaking.NoSuchGame", error = "Instance not found.", value = new Heartbeat { errorCode = MatchmakingErrorCode.NoSuchGame, isOnline = true, playerId = player.PlayerId } });

            if (targetInstance.isFull || Sessions.CountPlayersInInstance(roomInstanceId) >= targetInstance.maxCapacity)
                return Ok(new { success = false, error_id = "Matchmaking.InsufficientSpace", error = "Instance is full.", value = new Heartbeat { errorCode = MatchmakingErrorCode.InsufficientSpace, isOnline = true, playerId = player.PlayerId } });

            var currentInstanceHb = player.Player?.PlayerExtra?.Heartbeat;
            if (currentInstanceHb?.roomInstance?.roomInstanceId == roomInstanceId)
                return Ok(new { success = false, error_id = "Matchmaking.AlreadyInTargetInstance", error = "Already in this instance.", value = new Heartbeat { errorCode = MatchmakingErrorCode.AlreadyInTargetInstance, isOnline = true, playerId = player.PlayerId } });

            AuthorizeRoomSession(player.PlayerId, targetInstance.roomId);

            var heartbeat = PlayerDB.UpdatePlayerHeartbeat(player.PlayerId, targetInstance);
            heartbeat.playerId = player.PlayerId;

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            var (_, autoFollowInstance, _, additionalPlayerIdsInstance) = await ParseMatchmakeForm(Request);
            if (additionalPlayerIdsInstance.Count > 0 && autoFollowInstance)
                await ShovePlayersToInstance(additionalPlayerIdsInstance, targetInstance.roomId, targetInstance.subRoomId, heartbeat);
            else if (additionalPlayerIdsInstance.Count > 0)
                await NotificationsController.SendPartyActivitySwitch(player.PlayerId, additionalPlayerIdsInstance, targetInstance.roomId, heartbeat);

            return Ok(new { success = true, error_id = (string?)null, error = (string?)null, value = heartbeat });
        }

        [HttpPost("matchmake/room/{roomId}")]
        public async Task<IActionResult> MatchmakeRoomRoomId(long roomId, [FromQuery] long? subRoomId = null)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                var bannedHeartbeat = new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                };

                return Ok(bannedHeartbeat);
            }

            var (joinMode, autoFollow, bypassMovement, additionalPlayerIds) = await ParseMatchmakeForm(Request);

            var room = RoomDB.GetRoom(roomId);

            if (room == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.NoSuchRoom,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (AuthStuff.GetAndEnsureAppVersion(Request) != "20250724" && room.Tags.Any(x => x.Tag.Equals("2025", StringComparison.OrdinalIgnoreCase)))
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.UpdateRequired,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (IsBannedFromRoom(room, player.PlayerId))
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.BannedFromRoom,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (room != null)
            {
                bool isDeveloper = player.PlayerRoles?.Any(r => r == PlayerDBClasses.PlayerRoles.Developer) ?? false;
                bool isCreator = room.CreatorAccountId == player.PlayerId;
                bool hasAccess = room.Roles?.Any(r =>
                    r.AccountId == player.PlayerId && (int)r.Role >= (int)Role.CoOwner) ?? false;
                bool isOwnerOrCoOwner = isCreator || hasAccess;

                bool hasOwner = room.CreatorAccountId > 0 ||
                    (room.Roles?.Any(r => (int)r.Role >= (int)Role.CoOwner) ?? false);

                if (!hasOwner)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.AccountDoesNotExist,
                        isOnline = true,
                        playerId = player.PlayerId
                    });

                if (room.Accessibility == RoomAccessibility.Dev_only || room.Accessibility == RoomAccessibility.Dev_Unlisted)
                {
                    if (!isDeveloper && !isCreator && !hasAccess)
                        return Ok(new Heartbeat
                        {
                            errorCode = MatchmakingErrorCode.DeveloperOnly,
                            isOnline = true,
                            playerId = player.PlayerId
                        });
                }
                else if (room.Accessibility == RoomAccessibility.Private)
                {
                    if (!isCreator && !hasAccess)
                        return Ok(new Heartbeat
                        {
                            errorCode = MatchmakingErrorCode.RoomIsPrivate,
                            isOnline = true,
                            playerId = player.PlayerId
                        });
                }

                if (!isOwnerOrCoOwner)
                {
                    if (player.Player?.IsJunior == true && room.SupportsJuniors == false)
                        return Ok(new Heartbeat
                        {
                            errorCode = MatchmakingErrorCode.JuniorNotAllowed,
                            isOnline = true,
                            playerId = player.PlayerId
                        });

                    if (room.MinLevel > 0 && (player.Player?.Level ?? 1) < room.MinLevel)
                        return Ok(new Heartbeat
                        {
                            errorCode = MatchmakingErrorCode.LevelTooLow,
                            isOnline = true,
                            playerId = player.PlayerId
                        });
                }

            }

            var currentHb = player.Player?.PlayerExtra?.Heartbeat;
            if (bypassMovement && joinMode == 0 && currentHb?.roomInstance != null && currentHb.roomInstance.roomId == roomId && !currentHb.roomInstance.isPrivate)
            {
                long currentInstanceId = currentHb.roomInstance.roomInstanceId;

                var otherInstance = PlayerDB.Players
                    .FindAll()
                    .Where(p =>
                        p.PlayerId != player.PlayerId &&
                        p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomId == roomId &&
                        !p.Player.PlayerExtra.Heartbeat.roomInstance.isPrivate &&
                        p.Player.PlayerExtra.Heartbeat.roomInstance.roomInstanceId != currentInstanceId &&
                        Sessions.CountPlayersInInstance(p.Player.PlayerExtra.Heartbeat.roomInstance.roomInstanceId) < p.Player.PlayerExtra.Heartbeat.roomInstance.maxCapacity)
                    .Select(p => p.Player.PlayerExtra.Heartbeat.roomInstance)
                    .FirstOrDefault();

                if (otherInstance != null)
                {
                    var hb = PlayerDB.UpdatePlayerHeartbeat(player.PlayerId, otherInstance);
                    hb.playerId = player.PlayerId;
                    await NotificationsController.RefreshHeartbeat(player.PlayerId, hb);
                    return Ok(hb);
                }

                joinMode = 1;
            }

            RoomDB.IncrementRoomVisitCount(roomId);
            PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, roomId);

            AuthorizeRoomSession(player.PlayerId, roomId);

            var heartbeat = Sessions.CreateRoom(
                player.PlayerId,
                roomId,
                subRoomId,
                joinMode: joinMode,
                additionalPlayersAutoFollow: autoFollow);

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            if (additionalPlayerIds.Count > 0 && autoFollow)
                await ShovePlayersToInstance(additionalPlayerIds, roomId, subRoomId ?? 0, heartbeat);
            else if (additionalPlayerIds.Count > 0)
                await NotificationsController.SendPartyActivitySwitch(player.PlayerId, additionalPlayerIds, roomId, heartbeat);

            return Ok(heartbeat);
        }

        [HttpPost("matchmake/v2/room/{roomId}")]
        public async Task<IActionResult> MatchmakeRoomRoomIdV2(long roomId, [FromQuery] long? subRoomId = null)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                return Ok(new { success = false, error_id = "Matchmaking.Banned", error = "You are banned.", value = new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                }});
            }

            var (joinMode, autoFollow, bypassMovement, additionalPlayerIds) = await ParseMatchmakeForm(Request);

            var room = RoomDB.GetRoom(roomId);

            if (room == null)
                return Ok(new { success = false, error_id = "Matchmaking.NoSuchRoom", error = "Room not found.", value = new Heartbeat { errorCode = MatchmakingErrorCode.NoSuchRoom, isOnline = true, playerId = player.PlayerId } });

            if (AuthStuff.GetAndEnsureAppVersion(Request) != "20250724" && room.Tags.Any(x => x.Tag.Equals("2025", StringComparison.OrdinalIgnoreCase)))
                return Ok(new { success = false, error_id = "Matchmaking.UpdateRequired", error = "Update required.", value = new Heartbeat { errorCode = MatchmakingErrorCode.UpdateRequired, isOnline = true, playerId = player.PlayerId } });

            if (IsBannedFromRoom(room, player.PlayerId))
                return Ok(new { success = false, error_id = "Matchmaking.BannedFromRoom", error = "You are banned from this room.", value = new Heartbeat { errorCode = MatchmakingErrorCode.BannedFromRoom, isOnline = true, playerId = player.PlayerId } });

            if (room != null)
            {
                bool isDeveloper = player.PlayerRoles?.Any(r => r == PlayerDBClasses.PlayerRoles.Developer) ?? false;
                bool isCreator = room.CreatorAccountId == player.PlayerId;
                bool hasAccess = room.Roles?.Any(r =>
                    r.AccountId == player.PlayerId && (int)r.Role >= (int)Role.CoOwner) ?? false;
                bool isOwnerOrCoOwner = isCreator || hasAccess;

                bool hasOwner = room.CreatorAccountId > 0 ||
                    (room.Roles?.Any(r => (int)r.Role >= (int)Role.CoOwner) ?? false);

                if (!hasOwner)
                    return Ok(new { success = false, error_id = "Matchmaking.AccountDoesNotExist", error = "Room has no owner.", value = new Heartbeat { errorCode = MatchmakingErrorCode.AccountDoesNotExist, isOnline = true, playerId = player.PlayerId } });

                if (room.Accessibility == RoomAccessibility.Dev_only || room.Accessibility == RoomAccessibility.Dev_Unlisted)
                {
                    if (!isDeveloper && !isCreator && !hasAccess)
                        return Ok(new { success = false, error_id = "Matchmaking.DeveloperOnly", error = "This room is developer only.", value = new Heartbeat { errorCode = MatchmakingErrorCode.DeveloperOnly, isOnline = true, playerId = player.PlayerId } });
                }
                else if (room.Accessibility == RoomAccessibility.Private)
                {
                    if (!isCreator && !hasAccess)
                        return Ok(new { success = false, error_id = "Matchmaking.RoomIsPrivate", error = "This room is private.", value = new Heartbeat { errorCode = MatchmakingErrorCode.RoomIsPrivate, isOnline = true, playerId = player.PlayerId } });
                }

                if (!isOwnerOrCoOwner)
                {
                    if (player.Player?.IsJunior == true && room.SupportsJuniors == false)
                        return Ok(new { success = false, error_id = "Matchmaking.JuniorNotAllowed", error = "Juniors are not allowed in this room.", value = new Heartbeat { errorCode = MatchmakingErrorCode.JuniorNotAllowed, isOnline = true, playerId = player.PlayerId } });

                    if (room.MinLevel > 0 && (player.Player?.Level ?? 1) < room.MinLevel)
                        return Ok(new { success = false, error_id = "Matchmaking.LevelTooLow", error = "Your level is too low.", value = new Heartbeat { errorCode = MatchmakingErrorCode.LevelTooLow, isOnline = true, playerId = player.PlayerId } });
                }
            }

            var currentHb = player.Player?.PlayerExtra?.Heartbeat;
            if (bypassMovement && joinMode == 0 && currentHb?.roomInstance != null && currentHb.roomInstance.roomId == roomId && !currentHb.roomInstance.isPrivate)
            {
                long currentInstanceId = currentHb.roomInstance.roomInstanceId;

                var otherInstance = PlayerDB.Players
                    .FindAll()
                    .Where(p =>
                        p.PlayerId != player.PlayerId &&
                        p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomId == roomId &&
                        !p.Player.PlayerExtra.Heartbeat.roomInstance.isPrivate &&
                        p.Player.PlayerExtra.Heartbeat.roomInstance.roomInstanceId != currentInstanceId &&
                        Sessions.CountPlayersInInstance(p.Player.PlayerExtra.Heartbeat.roomInstance.roomInstanceId) < p.Player.PlayerExtra.Heartbeat.roomInstance.maxCapacity)
                    .Select(p => p.Player.PlayerExtra.Heartbeat.roomInstance)
                    .FirstOrDefault();

                if (otherInstance != null)
                {
                    var hb = PlayerDB.UpdatePlayerHeartbeat(player.PlayerId, otherInstance);
                    hb.playerId = player.PlayerId;
                    await NotificationsController.RefreshHeartbeat(player.PlayerId, hb);
                    return Ok(new { success = true, error_id = (string?)null, error = (string?)null, value = hb });
                }

                joinMode = 1;
            }

            RoomDB.IncrementRoomVisitCount(roomId);
            PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, roomId);

            AuthorizeRoomSession(player.PlayerId, roomId);

            var heartbeat = Sessions.CreateRoom(
                player.PlayerId,
                roomId,
                subRoomId,
                joinMode: joinMode,
                additionalPlayersAutoFollow: autoFollow);

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            if (additionalPlayerIds.Count > 0 && autoFollow)
                await ShovePlayersToInstance(additionalPlayerIds, roomId, subRoomId ?? 0, heartbeat);
            else if (additionalPlayerIds.Count > 0)
                await NotificationsController.SendPartyActivitySwitch(player.PlayerId, additionalPlayerIds, roomId, heartbeat);

            return Ok(new { success = true, error_id = (string?)null, error = (string?)null, value = heartbeat });
        }

        [Route("player/avoidjuniors")]
        public IActionResult avoidjuniours()
        {
            return Ok(false);
        }

        [HttpPost("matchmake/room/{roomId}/{subRoomId}")]
        public async Task<IActionResult> MatchmakeRoomWithSubRoom(long roomId, long subRoomId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                var bannedHeartbeat = new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                };

                return Ok(bannedHeartbeat);
            }

            var (joinMode, autoFollow, bypassMovement, additionalPlayerIds) = await ParseMatchmakeForm(Request);

            var room = RoomDB.GetRoom(roomId);

            if (room == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.NoSuchRoom,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (IsBannedFromRoom(room, player.PlayerId))
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.BannedFromRoom,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (room != null)
            {
                bool isDeveloper = player.PlayerRoles?.Any(r => r == PlayerDBClasses.PlayerRoles.Developer) ?? false;
                bool isCreator = room.CreatorAccountId == player.PlayerId;
                bool hasAccess = room.Roles?.Any(r =>
                    r.AccountId == player.PlayerId && (int)r.Role >= (int)Role.CoOwner) ?? false;
                bool isOwnerOrCoOwner = isCreator || hasAccess;

                bool hasOwner = room.CreatorAccountId > 0 ||
                    (room.Roles?.Any(r => (int)r.Role >= (int)Role.CoOwner) ?? false);

                if (!hasOwner)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.AccountDoesNotExist,
                        isOnline = true,
                        playerId = player.PlayerId
                    });

                if (room.Accessibility == RoomAccessibility.Dev_only || room.Accessibility == RoomAccessibility.Dev_Unlisted)
                {
                    if (!isDeveloper && !isCreator && !hasAccess)
                        return Ok(new Heartbeat
                        {
                            errorCode = MatchmakingErrorCode.DeveloperOnly,
                            isOnline = true,
                            playerId = player.PlayerId
                        });
                }
                else if (room.Accessibility == RoomAccessibility.Private)
                {
                    if (!isCreator && !hasAccess)
                        return Ok(new Heartbeat
                        {
                            errorCode = MatchmakingErrorCode.RoomIsPrivate,
                            isOnline = true,
                            playerId = player.PlayerId
                        });
                }

                if (!isOwnerOrCoOwner)
                {
                    if (player.Player?.IsJunior == true && room.SupportsJuniors == false)
                        return Ok(new Heartbeat
                        {
                            errorCode = MatchmakingErrorCode.JuniorNotAllowed,
                            isOnline = true,
                            playerId = player.PlayerId
                        });

                    if (room.MinLevel > 0 && (player.Player?.Level ?? 1) < room.MinLevel)
                        return Ok(new Heartbeat
                        {
                            errorCode = MatchmakingErrorCode.LevelTooLow,
                            isOnline = true,
                            playerId = player.PlayerId
                        });
                }

            }

            RoomDB.IncrementRoomVisitCount(roomId);
            PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, roomId);

            AuthorizeRoomSession(player.PlayerId, roomId);

            var heartbeat = Sessions.CreateRoom(
                player.PlayerId,
                roomId,
                subRoomId,
                joinMode: joinMode,
                additionalPlayersAutoFollow: autoFollow);

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            if (additionalPlayerIds.Count > 0 && autoFollow)
                await ShovePlayersToInstance(additionalPlayerIds, roomId, subRoomId, heartbeat);
            else if (additionalPlayerIds.Count > 0)
                await NotificationsController.SendPartyActivitySwitch(player.PlayerId, additionalPlayerIds, roomId, heartbeat);

            return Ok(heartbeat);
        }

        [HttpPost("matchmake/v2/room/{roomId}/{subRoomId}")]
        public async Task<IActionResult> MatchmakeRoomWithSubRoomV2(long roomId, long subRoomId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                return Ok(new { success = false, error_id = "Matchmaking.Banned", error = "You are banned.", value = new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                }});
            }

            var (joinMode, autoFollow, bypassMovement, additionalPlayerIds) = await ParseMatchmakeForm(Request);

            var room = RoomDB.GetRoom(roomId);

            if (room == null)
                return Ok(new { success = false, error_id = "Matchmaking.NoSuchRoom", error = "Room not found.", value = new Heartbeat { errorCode = MatchmakingErrorCode.NoSuchRoom, isOnline = true, playerId = player.PlayerId } });

            if (IsBannedFromRoom(room, player.PlayerId))
                return Ok(new { success = false, error_id = "Matchmaking.BannedFromRoom", error = "You are banned from this room.", value = new Heartbeat { errorCode = MatchmakingErrorCode.BannedFromRoom, isOnline = true, playerId = player.PlayerId } });

            if (room != null)
            {
                bool isDeveloper = player.PlayerRoles?.Any(r => r == PlayerDBClasses.PlayerRoles.Developer) ?? false;
                bool isCreator = room.CreatorAccountId == player.PlayerId;
                bool hasAccess = room.Roles?.Any(r =>
                    r.AccountId == player.PlayerId && (int)r.Role >= (int)Role.CoOwner) ?? false;
                bool isOwnerOrCoOwner = isCreator || hasAccess;

                bool hasOwner = room.CreatorAccountId > 0 ||
                    (room.Roles?.Any(r => (int)r.Role >= (int)Role.CoOwner) ?? false);

                if (!hasOwner)
                    return Ok(new { success = false, error_id = "Matchmaking.AccountDoesNotExist", error = "Room has no owner.", value = new Heartbeat { errorCode = MatchmakingErrorCode.AccountDoesNotExist, isOnline = true, playerId = player.PlayerId } });

                if (room.Accessibility == RoomAccessibility.Dev_only || room.Accessibility == RoomAccessibility.Dev_Unlisted)
                {
                    if (!isDeveloper && !isCreator && !hasAccess)
                        return Ok(new { success = false, error_id = "Matchmaking.DeveloperOnly", error = "This room is developer only.", value = new Heartbeat { errorCode = MatchmakingErrorCode.DeveloperOnly, isOnline = true, playerId = player.PlayerId } });
                }
                else if (room.Accessibility == RoomAccessibility.Private)
                {
                    if (!isCreator && !hasAccess)
                        return Ok(new { success = false, error_id = "Matchmaking.RoomIsPrivate", error = "This room is private.", value = new Heartbeat { errorCode = MatchmakingErrorCode.RoomIsPrivate, isOnline = true, playerId = player.PlayerId } });
                }

                if (!isOwnerOrCoOwner)
                {
                    if (player.Player?.IsJunior == true && room.SupportsJuniors == false)
                        return Ok(new { success = false, error_id = "Matchmaking.JuniorNotAllowed", error = "Juniors are not allowed in this room.", value = new Heartbeat { errorCode = MatchmakingErrorCode.JuniorNotAllowed, isOnline = true, playerId = player.PlayerId } });

                    if (room.MinLevel > 0 && (player.Player?.Level ?? 1) < room.MinLevel)
                        return Ok(new { success = false, error_id = "Matchmaking.LevelTooLow", error = "Your level is too low.", value = new Heartbeat { errorCode = MatchmakingErrorCode.LevelTooLow, isOnline = true, playerId = player.PlayerId } });
                }
            }

            RoomDB.IncrementRoomVisitCount(roomId);
            PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, roomId);

            AuthorizeRoomSession(player.PlayerId, roomId);

            var heartbeat = Sessions.CreateRoom(
                player.PlayerId,
                roomId,
                subRoomId,
                joinMode: joinMode,
                additionalPlayersAutoFollow: autoFollow);

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            if (additionalPlayerIds.Count > 0 && autoFollow)
                await ShovePlayersToInstance(additionalPlayerIds, roomId, subRoomId, heartbeat);
            else if (additionalPlayerIds.Count > 0)
                await NotificationsController.SendPartyActivitySwitch(player.PlayerId, additionalPlayerIds, roomId, heartbeat);

            return Ok(new { success = true, error_id = (string?)null, error = (string?)null, value = heartbeat });
        }
        
        /* 20230616 & 20250724.01 photons */

        private static readonly string[] _fallbackRealtime = new[]
        {
            "616d6ba8-edb1-47f8-a0de-31c8ce426685",
            "a7c732cd-5cea-4839-b0b3-ba8e9af14b06",
            "90d94f73-9797-409d-84ab-ed3992177612"
        };

        private static readonly string[] _fallbackVoice = new[]
        {
            "b3a7f09a-b167-4ba1-87bb-2603da5a39be",
            "7e71f40c-d1fa-4e48-8851-981402e630e5",
            "3e010413-c8c8-48df-95cf-bf7f2bf430e6"
        };

        [HttpGet("player/connection-info")]
        public async Task<IActionResult> GetPlayerConnectionInfo([FromQuery] long? roomInstanceId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            if (roomInstanceId == null || roomInstanceId <= 0)
            {
                return Ok(new
                {
                    success = true,
                    error = (string?)null,
                    value = new
                    {
                        PhotonRealtimeAppId = _fallbackRealtime[_photonRng.Next(_fallbackRealtime.Length)],
                        PhotonChatAppId,
                        PhotonVoiceAppId = _fallbackVoice[_photonRng.Next(_fallbackVoice.Length)],
                        experiments = new { shouldUseGameServerNetworking = false }
                    }
                });
            }

            var dbEntry = InstanceDB.Get(roomInstanceId.Value);

            if (dbEntry == null)
            {
                var liveInstance = PlayerDB.Players.FindAll()
                    .Select(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance)
                    .FirstOrDefault(ri => ri?.roomInstanceId == roomInstanceId.Value);

                if (liveInstance != null)
                {
                    dbEntry = InstanceDB.Upsert(liveInstance, liveInstance.isPrivate);
                }
            }

            if (dbEntry == null)
            {
                return Ok(new
                {
                    success = true,
                    error = (string?)null,
                    value = new
                    {
                        PhotonRealtimeAppId = _fallbackRealtime[_photonRng.Next(_fallbackRealtime.Length)],
                        PhotonChatAppId,
                        PhotonVoiceAppId = _fallbackVoice[_photonRng.Next(_fallbackVoice.Length)],
                        experiments = new { shouldUseGameServerNetworking = false }
                    }
                });
            }

            if (string.IsNullOrEmpty(dbEntry.PhotonRealtimeAppId) || string.IsNullOrEmpty(dbEntry.PhotonVoiceAppId))
            {
                var (rt, vo) = InstanceDB.AssignPhotonIds(roomInstanceId.Value);
                dbEntry.PhotonRealtimeAppId = rt;
                dbEntry.PhotonVoiceAppId = vo;
            }

            var liveInst = PlayerDB.Players.FindAll()
                .Select(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance)
                .FirstOrDefault(ri => ri?.roomInstanceId == roomInstanceId.Value);

            return Ok(new
            {
                success = true,
                error = (string?)null,
                value = new
                {
                    PhotonAuthToken = "",
                    PhotonRealtimeAppId = dbEntry.PhotonRealtimeAppId,
                    PhotonChatAppId,
                    PhotonVoiceAppId = dbEntry.PhotonVoiceAppId,
                    PhotonRegion = liveInst?.photonRegion ?? dbEntry.PhotonRegion ?? "us",
                    PhotonRoomId = liveInst?.photonRoomId ?? dbEntry.PhotonRoomId,
                    VoiceConnectionInfo = (string?)null,
                    VoiceServerId = (string?)null,
                    experiments = new { shouldUseGameServerNetworking = false }
                }
            });
        }

        [HttpPost("invite")]
        public async Task<IActionResult> Invite([FromForm] long playerId, [FromForm] long roomInstanceId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (playerId <= 0 || roomInstanceId <= 0)
                return BadRequest();

            var targetPlayer = PlayerDB.Players.FindById(playerId);
            if (targetPlayer == null)
                return NotFound("");

            var rel = FriendsDB.GetRelationships(player.PlayerId).FirstOrDefault(r => r.OtherPlayerID == playerId);
            if (rel?.RelationshipType != FriendsDBClasses.RelationshipType.Friend)
                return BadRequest();

            var heartbeat = PlayerDB.GetPlayerHeartbeat(player.PlayerId);
            if (heartbeat?.roomInstance == null || heartbeat.roomInstance.roomInstanceId != roomInstanceId)
                return BadRequest();

            var key = (player.PlayerId, playerId);
            var now = DateTime.UtcNow;
            _inviteRateLimit.AddOrUpdate(key, _ => (1, now), (_, e) =>
                (now - e.Window).TotalSeconds >= 15 ? (1, now) : (e.Count + 1, e.Window));

            if (_inviteRateLimit.TryGetValue(key, out var rate) && rate.Count > 3 && (now - rate.Window).TotalSeconds < 15)
                return BadRequest();

            var invite = new MatchDBClasses.InviteDTO
            {
                InviteId = roomInstanceId,
                Name = heartbeat.roomInstance.Name ?? "Unknown Room",
                InviteMode = 0
            };

            await NotificationsController.SendInvite(playerId, player.PlayerId, heartbeat.roomInstance.roomId, invite);

            return Ok();
        }

        //[HttpPost("matchmake/instance/{roomInstanceId}")] // added back because some bitch deleted it
        [HttpPost("matchmake/invite/{roomInstanceId}")] // ez
        //[HttpPost("matchmake/v2/instance/{roomInstanceId}")]
        public async Task<IActionResult> MatchmakeToInviteInstance(long roomInstanceId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                return Ok(new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                });
            }

            var targetInstance = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == roomInstanceId)
                .Select(p => p.Player.PlayerExtra.Heartbeat.roomInstance)
                .FirstOrDefault();

            if (targetInstance == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.NoSuchGame,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (targetInstance.isFull || Sessions.CountPlayersInInstance(roomInstanceId) >= targetInstance.maxCapacity)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.InsufficientSpace,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            var currentInstanceHb = player.Player?.PlayerExtra?.Heartbeat;
            if (currentInstanceHb?.roomInstance?.roomInstanceId == roomInstanceId)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.AlreadyInTargetInstance,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            AuthorizeRoomSession(player.PlayerId, targetInstance.roomId);

            var heartbeat = PlayerDB.UpdatePlayerHeartbeat(player.PlayerId, targetInstance);
            heartbeat.playerId = player.PlayerId;

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            return Ok(heartbeat);
        }

        [HttpPost("matchmake/v2/invite/{roomInstanceId}")]
        public async Task<IActionResult> MatchmakeToInviteInstanceV2(long roomInstanceId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                return Ok(new { success = false, error_id = "Matchmaking.Banned", error = "You are banned.", value = new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                }});
            }

            var targetInstance = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == roomInstanceId)
                .Select(p => p.Player.PlayerExtra.Heartbeat.roomInstance)
                .FirstOrDefault();

            if (targetInstance == null)
                return Ok(new { success = false, error_id = "Matchmaking.NoSuchGame", error = "Instance not found.", value = new Heartbeat { errorCode = MatchmakingErrorCode.NoSuchGame, isOnline = true, playerId = player.PlayerId } });

            if (targetInstance.isFull || Sessions.CountPlayersInInstance(roomInstanceId) >= targetInstance.maxCapacity)
                return Ok(new { success = false, error_id = "Matchmaking.InsufficientSpace", error = "Instance is full.", value = new Heartbeat { errorCode = MatchmakingErrorCode.InsufficientSpace, isOnline = true, playerId = player.PlayerId } });

            var currentInstanceHb = player.Player?.PlayerExtra?.Heartbeat;
            if (currentInstanceHb?.roomInstance?.roomInstanceId == roomInstanceId)
                return Ok(new { success = false, error_id = "Matchmaking.AlreadyInTargetInstance", error = "Already in this instance.", value = new Heartbeat { errorCode = MatchmakingErrorCode.AlreadyInTargetInstance, isOnline = true, playerId = player.PlayerId } });

            AuthorizeRoomSession(player.PlayerId, targetInstance.roomId);

            var heartbeat = PlayerDB.UpdatePlayerHeartbeat(player.PlayerId, targetInstance);
            heartbeat.playerId = player.PlayerId;

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            return Ok(new { success = true, error_id = (string?)null, error = (string?)null, value = heartbeat });
        }
        
        [HttpPost("matchmake/player/{targetPlayerId}")]
        public async Task<IActionResult> MatchmakeToPlayer(long targetPlayerId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (PlayerDB.IsBanned(player.PlayerId))
            {
                var previousHeartbeat = player.Player?.PlayerExtra?.Heartbeat;
                return Ok(new Heartbeat
                {
                    appVersion = AuthStuff.GetAndEnsureAppVersion(Request),
                    deviceClass = previousHeartbeat?.deviceClass ?? DeviceClasses.Unknown,
                    errorCode = MatchmakingErrorCode.Banned,
                    isOnline = previousHeartbeat?.isOnline ?? true,
                    playerId = player.PlayerId,
                    roomInstance = null,
                    statusVisibility = previousHeartbeat?.statusVisibility ?? StatusVisibility.Online,
                    vrMovementMode = previousHeartbeat?.vrMovementMode ?? 0,
                    photonAuthToken = null,
                    photonRealtimeAppId = null,
                    photonVoiceAppId = null,
                    photonChatAppId = null,
                    photonRegion = null,
                    photonRoomId = null,
                    voiceConnectionInfo = null,
                    voiceServerId = null,
                    experiments = null
                });
            }

            var targetPlayer = PlayerDB.Players.FindById(targetPlayerId);
            if (targetPlayer == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.AccountDoesNotExist,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            var targetHb = targetPlayer.Player?.PlayerExtra?.Heartbeat;
            if (targetHb == null || !targetHb.isOnline || targetHb.roomInstance == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.PlayerNotOnline,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            var targetInstance = targetHb.roomInstance;
            long roomId = targetInstance.roomId;
            long roomInstanceId = targetInstance.roomInstanceId;

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.NoSuchRoom,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (IsBannedFromRoom(room, player.PlayerId))
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.BannedFromRoom,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            bool isDeveloper = player.PlayerRoles?.Any(r => r == PlayerDBClasses.PlayerRoles.Developer) ?? false;
            bool isCreator = room.CreatorAccountId == player.PlayerId;
            bool hasAccess = room.Roles?.Any(r =>
                r.AccountId == player.PlayerId && (int)r.Role >= (int)Role.CoOwner) ?? false;
            bool isOwnerOrCoOwner = isCreator || hasAccess;

            bool hasOwner = room.CreatorAccountId > 0 ||
                (room.Roles?.Any(r => (int)r.Role >= (int)Role.CoOwner) ?? false);

            if (!hasOwner)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.AccountDoesNotExist,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            if (room.Accessibility == RoomAccessibility.Dev_only || room.Accessibility == RoomAccessibility.Dev_Unlisted)
            {
                if (!isDeveloper && !isCreator && !hasAccess)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.DeveloperOnly,
                        isOnline = true,
                        playerId = player.PlayerId
                    });
            }
            else if (room.Accessibility == RoomAccessibility.Private)
            {
                if (!isCreator && !hasAccess)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.RoomIsPrivate,
                        isOnline = true,
                        playerId = player.PlayerId
                    });
            }

            if (!isOwnerOrCoOwner)
            {
                if (player.Player?.IsJunior == true && room.SupportsJuniors == false)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.JuniorNotAllowed,
                        isOnline = true,
                        playerId = player.PlayerId
                    });

                if (room.MinLevel > 0 && (player.Player?.Level ?? 1) < room.MinLevel)
                    return Ok(new Heartbeat
                    {
                        errorCode = MatchmakingErrorCode.LevelTooLow,
                        isOnline = true,
                        playerId = player.PlayerId
                    });
            }

            if (targetInstance.isFull || Sessions.CountPlayersInInstance(roomInstanceId) >= targetInstance.maxCapacity)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.InsufficientSpace,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            var currentInstanceHb = player.Player?.PlayerExtra?.Heartbeat;
            if (currentInstanceHb?.roomInstance?.roomInstanceId == roomInstanceId)
                return Ok(new Heartbeat
                {
                    errorCode = MatchmakingErrorCode.AlreadyInTargetInstance,
                    isOnline = true,
                    playerId = player.PlayerId
                });

            RoomDB.IncrementRoomVisitCount(roomId);
            PlayerDB.UpdateRoomLastVisitedAt(player.PlayerId, roomId);

            AuthorizeRoomSession(player.PlayerId, roomId);

            var heartbeat = PlayerDB.UpdatePlayerHeartbeat(player.PlayerId, targetInstance);
            heartbeat.playerId = player.PlayerId;

            await NotificationsController.RefreshHeartbeat(player.PlayerId, heartbeat);

            return Ok(heartbeat);
        }

        [HttpDelete("invite/{inviteId}")]
        public async Task<IActionResult> DeclineInvite(long inviteId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            var messages = PlayerDB.GetMessages(player.PlayerId);
            var invite = messages.FirstOrDefault(m => m.Id == inviteId);

            var invitedata = new MatchDBClasses.InviteDTO
            {
                InviteId = inviteId,
                Name = "Unknown Room",
                InviteMode = 0
            };

            if (invite != null)
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    Id = "2",
                    Msg = new
                    {
                        FromPlayerId = player.PlayerId,
                        Id = PlayerDB.GenerateRandomMessageId(),
                        SentTime = DateTime.UtcNow,
                        Type = MessageType.GameInviteDeclined,
                        Data = invitedata,
                        RoomId = invite.RoomId,
                        PlayerEventId = (long?)null
                    }
                });

                await NotificationsController.SendToPlayer(invite.FromPlayerId, json);
                PlayerDB.DeleteMessage(player.PlayerId, inviteId);
            }

            return Ok();
        }
    }
}