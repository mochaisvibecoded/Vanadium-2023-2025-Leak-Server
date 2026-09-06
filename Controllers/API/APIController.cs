using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration.UserSecrets;
using Newtonsoft.Json;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Classes.Rooms;
using Vanadium.Utils.NotiController;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml;
using static ImageMetadataDB;
using static Vanadium.Classes.DBs.DBClasses.EventDBClasses;
using static Vanadium.Classes.DBs.DBClasses.FriendsDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using static Vanadium.Classes.DBs.DBClasses.RRPlus;
using Vanadium.Classes.WebSocket;
// :3
namespace Vanadium.Controllers
{
    [ApiController]
    public partial class APIController : ControllerBase
    {
        [HttpGet("/")]
        /*public IActionResult OldNameserver()
        {
        	return Ok(new { RecNetStatus = "You're on an old version of Vanadium.\n\nUpdate to continue playing!" });
        }*/
        [HttpGet("/altns")]
        public async Task<IActionResult> GetNS([FromQuery] int v)
        {
            if (Request.Path == "/" && v == 2)
                return Ok(new { RecNetStatus = "You're on an old version of Vanadium.\n\nUpdate to continue playing!" });

            string url = ServerConfig.BaseURL;

            return Ok(new
            {
                Accounts = url + "/acc",
                AI = url,
                API = url,
                Auth = url + "/auth",
                BugReporting = url,
                Cards = url,
                CDN = url + "/cdn",
                Chat = url + "/chat",
                Clubs = url,
                CMS = url,
                Commerce = url,
                Data = url,
                DataCollection = url,
                Discovery = url,
                Econ = url,
                GameLogs = url,
                Geo = url,
                Images = url + "/imageserver",
                //Images = "img.rec.net",
                Leaderboard = url,
                Link = url,
                Lists = url,
                Matchmaking = url + "/match",
                Moderation = url,
                Notifications = url + "/notifications",
                PlatformNotifications = url,
                PlayerSettings = url,
                RoomComments = url,
                Rooms = url + "/roomserver",
                RoomieIntegrations = url,
                Storage = url,
                Strings = url,
                StringsCDN = url,
                Studio = url,
                Thorn = url,
                Videos = url,
                WWW = url
            });
        }

        [HttpGet("api/versioncheck/v4")]
        public async Task<IActionResult> VersionCheck([FromQuery] string v)
        {
            string cfgPath = System.IO.Path.Combine(Program.configDir, "versioncheck.cfg");
            string[] validVersions = Array.Empty<string>();

            if (System.IO.File.Exists(cfgPath))
            {
                validVersions = (await System.IO.File.ReadAllLinesAsync(cfgPath))
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    .ToArray();
            }

			if (!validVersions.Contains(v) && HttpContext.Connection.RemoteIpAddress?.ToString() != "2600:1700:95d0:1070:5d5a:ec7a:8afa:9142")
			{
				return NotFound("");
			}

            return Ok(new
            {
                ValidVersion = 0,
                VersionStatus = 0,
                UpdateNotificationStage = 0,
                IsVersionIslanded = false,
                IsCrossPlayDisabled = false
            });
        }

        [HttpGet("api/gameconfigs/v1/all")]
        public async Task<IActionResult> GetGameConfigs()
        {
            string path = Path.Join(Program.dataDir, "APIS", "GameConfigs.json");
            return System.IO.File.Exists(path) ? Content(System.IO.File.ReadAllText(path), "application/json") : NotFound();
        }

        [HttpGet("api/config/v1/amplitude")]
        public async Task<IActionResult> GetAmplitude()
        {
            return Ok(new
            {
                AmplitudeKey = "cb2fb2ecb9953512c29af5bca58f2b4a",
                UseRudderStack = false,
                RudderStackKey = "23NiJHIgu3koaGNCZIiuYvIQNCu",
                UseStatSig = true,
                StatSigKey = "client-SBZkOrjD3r1Cat3f3W8K6sBd11WKlXZXIlCWj6l4Aje",
                StatSigEnvironment = 0
            });
        }

        [HttpGet("api/avatar/v1/defaultunlocked")]
        public async Task<IActionResult> GetDefaultUnlocked()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("api/challenge/v2/getCurrent")]
        public IActionResult GetCurrentChallengeBro()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("api/avatar/v1/defaultbaseavataritems")]
        public async Task<IActionResult> GetDefaultBaseAvatarItems()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/objectives/v1/myprogress")]
        public async Task<IActionResult> GetMyObjectiveProgress()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            return Ok(new
            {
                Objectives = ServerConfig.Bracket,
                ObjectiveGroups = ServerConfig.Bracket
            });
        }

        [HttpGet("/api/avatar/v2")]
        [HttpGet("/api/avatar/v2/{playerId}")]
        public async Task<IActionResult> GetMyAvatar(long? playerId = null)
        {
            if (playerId.HasValue)
            {
                var player = PlayerDB.Players.FindById(playerId.Value);
                if (player == null)
                    return NotFound("");

                return Ok(player.Player?.PlayerExtra.Avatar);
            }

            var currentPlayer = AuthStuff.GetCurrentPlayer(Request);
            if (currentPlayer == null)
                return Unauthorized("");

            return Ok(currentPlayer.Player?.PlayerExtra.Avatar);
        }

        [HttpPost("/api/avatar/v2/set")]
        public async Task<IActionResult> SetMyAvatar([FromBody] PlayerDBClasses.Avatar request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var result = PlayerDB.SetAvatar((long)id, request);
            return Ok(result);
        }

        [HttpGet("/api/avatar/v4/items")]
        public async Task<IActionResult> GetMyAvatarItems()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
                
            var version = AuthStuff.GetAndEnsureAppVersion(Request);
            if (version == "20240105")
            {
                string path2024 = Path.Join(Program.dataDir, "APIS", "Items", "AvatarItems2024.json");
            return System.IO.File.Exists(path2024) ? Content(System.IO.File.ReadAllText(path2024), "application/json") : NotFound();
            }

            string path = Path.Join(Program.dataDir, "APIS", "Items", "AvatarItems.json");
            return System.IO.File.Exists(path) ? Content(System.IO.File.ReadAllText(path), "application/json") : NotFound();
        }

        [HttpPost("/api/playerwarnings")] // dont touch.
        public async Task<IActionResult> PlayerWarningStuff()
        {
            return Ok(new { success = true, error_message = (string?)null, value = (object?)null });
        }

        private static readonly HttpClient _httpClient = new HttpClient();
        private const string ReportWebhookUrl = "https://discord.com/api/webhooks/1545909321139748955/d3GbBbDyB6YAJ7BZowiNwmsum0ziu_7avdJxMFaolhqLxdM50wn07WqHDBpAZFK_20AS";

        [HttpPost("/api/PlayerReporting/v3/create")]
        public async Task<IActionResult> ReportPlayerCreate()
        {
            var callerId = AuthStuff.GetPlayerId(Request);
            if (callerId == null)
                return StatusCode(403);

            var form = await Request.ReadFormAsync();
            if (!long.TryParse(form["PlayerIdReported"], out long reportedPlayerId) || reportedPlayerId <= 0)
                return BadRequest(new { success = false });

            var caller = PlayerDB.Players.FindById(callerId.Value);
            if (caller == null)
                return Ok(new { success = true });

            var target = PlayerDB.Players.FindById(reportedPlayerId);
            if (target?.Player == null)
                return Ok(new { success = false });

            int.TryParse(form["ReportCategory"], out int reportCategoryInt);
            string reportReason = form["Details"].ToString();
            if (string.IsNullOrWhiteSpace(reportReason))
                reportReason = form["Comments"].ToString();
            if (string.IsNullOrWhiteSpace(reportReason))
                reportReason = ReportCategoryReason(reportCategoryInt);

            string callerUsername = caller.Player?.Username ?? callerId.Value.ToString();
            string targetUsername = target.Player?.Username ?? reportedPlayerId.ToString();

            bool isDev = caller.PlayerRoles.Contains(PlayerRoles.Developer);
            bool isMod = caller.PlayerRoles.Contains(PlayerRoles.Moderator);
            bool isPrivileged = isDev || isMod;

            string webhookContent;

            if (isPrivileged && TryParseDurationReason(reportReason, out int durationSeconds, out string parsedReason))
            {
                string roleLabel = isDev && isMod ? "Dev/Mod" : isDev ? "Dev" : "Mod";

                if (durationSeconds == 0)
                {
                    UnbanPlayer(target);
                    webhookContent = $"# **🟢 {roleLabel} unbanned player!**  \nPlayer: @{targetUsername} ``{reportedPlayerId}`` \nReason: {parsedReason} \nComing From: {callerUsername}";
                }
                else
                {
                    if (durationSeconds == -1)
                    {
                        durationSeconds = int.MaxValue; // Permanent ban
                    }
                    await BanPlayer(target, reportedPlayerId, reportCategoryInt, parsedReason, durationSeconds);
                    string durationText = FormatDuration(durationSeconds);
                    webhookContent = $"# **🔴 {roleLabel} banned user!**  \nReported Player: @{targetUsername} ``{reportedPlayerId}`` \nDuration: {durationText} \nReason: {parsedReason} \nComing From: {callerUsername}";
                }
            }
            else
            {
                string prefix = isPrivileged
                    ? $"🟠 {(isDev && isMod ? "Dev/Mod" : isDev ? "Dev" : "Mod")} reported player! (not in ban format so not banned)"
                    : "🟠 Player Reported!";
                webhookContent = $"# **{prefix}**  \nReported Player: @{targetUsername} ``{reportedPlayerId}`` \nReason: {reportReason} \nComing From: {callerUsername}";
            }

            await SendWebhook(webhookContent);

            return Ok(new { success = true });
        }

        private static bool TryParseDurationReason(string input, out int durationSeconds, out string reason)
        {
            durationSeconds = 0;
            reason = input;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            int commaIndex = input.IndexOf(',');
            if (commaIndex <= -1)
                return false;

            string durationPart = input.Substring(0, commaIndex).Trim();
            string reasonPart = input.Substring(commaIndex + 1).Trim();

            if (!int.TryParse(durationPart, out int seconds) || seconds < -1)
                return false;

            durationSeconds = seconds;
            reason = string.IsNullOrWhiteSpace(reasonPart) ? "No reason given" : reasonPart;
            return true;
        }

        private static string FormatDuration(int seconds)
        {
            if (seconds == int.MaxValue)
                return "Permanent";

            var span = TimeSpan.FromSeconds(seconds);

            if (span.TotalDays >= 1)
                return $"{span.TotalDays:0.##} day(s)";
            if (span.TotalHours >= 1)
                return $"{span.TotalHours:0.##} hour(s)";
            if (span.TotalMinutes >= 1)
                return $"{span.TotalMinutes:0.##} minute(s)";

            return $"{seconds} second(s)";
        }

        private async Task BanPlayer(FullPlayer target, long reportedPlayerId, int reportCategoryInt, string reportReason, int durationSeconds) // bro dont use this
        {
            var mbd = new ModerationBlockDetails
            {
                IsBan = reportCategoryInt < 100 && durationSeconds != int.MaxValue, // If the report category is less than 100 and the duration is not permanent, it's a ban
                ReportCategory = Enum.IsDefined(typeof(ReportCategory), reportCategoryInt) ? (ReportCategory)reportCategoryInt : ReportCategory.Moderator,
                Duration = durationSeconds,
                Message = reportReason,
                ModerationSetUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            target.Player.PlayerExtra ??= new PlayerExtra();
            target.Player.PlayerExtra.ModerationBlockDetails = mbd;
            target.Player.PlayerExtra.Heartbeat = new Heartbeat
            {
                playerId = reportedPlayerId,
                isOnline = false,
                roomInstance = null,
                errorCode = 0
            };

            PlayerDB.Players.Update(target);

            await NotiController.SendPresenceUpdate(reportedPlayerId, target.Player.PlayerExtra.Heartbeat);
            await NotiController.SendAccountUpdate(reportedPlayerId, PlayerDB.GetAccountMe(reportedPlayerId));
            await NotificationsController.SendBanned(reportedPlayerId, mbd);
        }

        private void UnbanPlayer(FullPlayer target)
        {
            target.Player.PlayerExtra ??= new PlayerExtra();
            target.Player.PlayerExtra.ModerationBlockDetails = new ModerationBlockDetails
            {
                IsBan = false,
                ReportCategory = 0,
                Duration = 0,
                Message = ""
            };

            PlayerDB.Players.Update(target);
        }

        private static async Task SendWebhook(string content)
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new { content });
            await _httpClient.PostAsync(ReportWebhookUrl, new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        }

        private static string ReportCategoryReason(int category) => category switch
        {
            1 => "Microphone abuse",
            2 => "Harassment",
            3 => "Cheating or exploiting",
            5 => "AFK",
            6 => "Disruptive behavior",
            7 => "Underage",
            100 => "Underage (Code of Conduct)",
            101 => "Sexual content",
            102 => "Discrimination",
            103 => "Trolling",
            104 => "Inappropriate name or profile",
            200 => "Inappropriate outfit or avatar",
            _ => "Violation of the Student Code of Conduct"
        };

        public async static Task<string> FormatModerationBlockMessageString(ModerationBlockDetails mb)
        {
            var ReportCategory = mb?.ReportCategory ?? 0;
            string suffix = mb?.Duration == 2147483647 ? ".)" : ". Future bans may be longer or permanent.)";
            switch (ReportCategory)
            {
                case ReportCategory.Moderator:
                    mb.Message = "Moderator (" + mb.Message + suffix;
                    break;
                case ReportCategory.Harassment:
                case ReportCategory.CoC_Discrimination:
                    mb.Message = "Harassment or discrimination (" + mb.Message + suffix;
                    break;
                case ReportCategory.Cheating:
                    mb.Message = "Cheating (" + mb.Message + suffix;
                    break;
                case ReportCategory.InappropriateClothing:
                    mb.Message = "Inappropriate custom clothing (" + mb.Message + suffix;
                    break;
                case ReportCategory.CoC_Trolling:
                    mb.Message = "Trolling (" + mb.Message + suffix;
                    break;
                case ReportCategory.CoC_NameOrProfile:
                    mb.Message = "Inappropriate name, bio or profile photo (" + mb.Message + suffix;
                    break;
                case ReportCategory.Underage:
                case ReportCategory.CoC_Underage:
                    mb.Message = "Player under 13 must be on a junior account (" + mb.Message + suffix;
                    break;
                case ReportCategory.CoC_Sexual:
                    mb.Message = "Sexually explicit behavior or photos (" + mb.Message + suffix;
                    break;
            }
            return mb?.Message ?? "";
        }

        [HttpPost("/api/PlayerReporting/v1/moderationBlockDetails")] // dont allow httpget so people cant play on old versions
        public async Task<IActionResult> GetMyModerationBlockDetails()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            await Services.ToxModService.EnsureCurrent(player);
            var mb = player.Player.PlayerExtra.ModerationBlockDetails;
            mb.Message = await FormatModerationBlockMessageString(mb);
            return Ok(mb);
        }

        [HttpGet("/api/PlayerReporting/v1/voteToKickReasons")]
        public IActionResult GetVoteToKickReasons()
        {
            string path = Path.Join(Program.dataDir, "APIS", "VoteToKickReasons.json");
            if (System.IO.File.Exists(path))
                return Content(System.IO.File.ReadAllText(path), "application/json");

            var reasons = new[]
            {
                new { ReportCategory = (int)ReportCategory.Harassment, Reason = "Verbal harassment or bullying" },
                new { ReportCategory = (int)ReportCategory.Harassment, Reason = "Hate speech or discrimination" },
                new { ReportCategory = (int)ReportCategory.Harassment, Reason = "Inappropriate voice chat" },
                new { ReportCategory = (int)ReportCategory.Cheating, Reason = "Cheating or exploiting" },
                new { ReportCategory = (int)ReportCategory.AFK, Reason = "AFK or inactive" },
                new { ReportCategory = (int)ReportCategory.Misc, Reason = "Blocking or disrupting gameplay" },
                new { ReportCategory = (int)ReportCategory.Misc, Reason = "Trolling" },
                new { ReportCategory = (int)ReportCategory.InappropriateClothing, Reason = "Inappropriate outfit or avatar" }
            };
            return Ok(reasons);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (HashSet<long> Voters, DateTime Started)> _voteKickTally = new();

        [HttpPost("/api/PlayerReporting/v3/voteToKick")]
        public async Task<IActionResult> VoteToKick()
        {
            var voterId = AuthStuff.GetPlayerId(Request);
            if (voterId == null)
                return Unauthorized(new { Success = false, Value = (object?)null, Error = "Not authenticated." });

            long targetId = 0;
            long gameSessionId = 0;
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                targetId = ReadLong(form, "PlayerId", "playerId", "ReportedPlayer", "TargetPlayerId", "targetPlayerId", "AccountId");
                gameSessionId = ReadLong(form, "GameSessionId", "gameSessionId", "gamesessionid");
            }
            if (targetId == 0 && long.TryParse(Request.Query["playerId"], out var qp)) targetId = qp;

            if (targetId == 0 || targetId == voterId.Value)
                return Ok(new { Success = false, Value = (object?)null, Error = "Invalid vote target." });

            var target = PlayerDB.Players.FindById(targetId);
            var targetInstance = target?.Player?.PlayerExtra?.Heartbeat?.roomInstance;
            if (target == null || targetInstance == null || target.Player?.PlayerExtra?.Heartbeat?.isOnline != true)
                return Ok(new { Success = false, Value = (object?)null, Error = "Target is not in a session." });

            string key = $"{targetInstance.roomInstanceId}:{targetId}";
            var entry = _voteKickTally.AddOrUpdate(key,
                _ => (new HashSet<long> { voterId.Value }, DateTime.UtcNow),
                (_, cur) =>
                {
                    if (DateTime.UtcNow - cur.Started > TimeSpan.FromMinutes(3))
                        return (new HashSet<long> { voterId.Value }, DateTime.UtcNow);
                    cur.Voters.Add(voterId.Value);
                    return cur;
                });

            int othersInInstance = PlayerDB.Players.FindAll().Count(p =>
                p.PlayerId != targetId &&
                p.Player?.PlayerExtra?.Heartbeat?.isOnline == true &&
                p.Player.PlayerExtra.Heartbeat.roomInstance?.roomInstanceId == targetInstance.roomInstanceId);

            int needed = Math.Max(2, (othersInInstance / 2) + 1);
            int votes = entry.Voters.Count;
            bool passed = votes >= needed;

            if (passed)
            {
                _voteKickTally.TryRemove(key, out _);
                string wsEvent = Newtonsoft.Json.JsonConvert.SerializeObject(
                    Vanadium.Classes.WebSocket.WebsocketEvents.CreateWSEventString("ModerationQuitGame", new { }));
                await NotificationsController.SendToPlayer(targetId, wsEvent);
            }

            return Ok(new { Success = true, Value = new { Passed = passed, Votes = votes, Needed = needed }, Error = (string?)null });
        }

        private static long ReadLong(IFormCollection form, params string[] keys)
        {
            foreach (var k in keys)
                if (form.TryGetValue(k, out var v) && long.TryParse(v, out var parsed))
                    return parsed;
            return 0;
        }

        [HttpGet("/beta/verifydiscord")]
        public IActionResult VerifyDiscordPage()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "Director", "VerifyDiscord.html");
            if (!System.IO.File.Exists(path))
                return NotFound("VerifyDiscord.html not found.");
            Response.Headers.CacheControl = "no-cache";
            return PhysicalFile(path, "text/html");
        }

        [HttpPost("/beta/verifydiscord")]
        public async Task<IActionResult> VerifyDiscord([FromBody] VerifyDiscordRequest body)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized(new { success = false, error = "Not signed in. Open this page from in-game." });

            var pending = Services.PlayerCountService.ConsumeCode(body?.Code);
            if (pending == null)
                return BadRequest(new { success = false, error = "That code is invalid or expired." });

            var already = PlayerDB.Players.FindOne(p =>
                p.Player != null && p.Player.DiscordUserId == pending.DiscordUserId.ToString());
            if (already != null && already.PlayerId != playerId.Value)
                return BadRequest(new { success = false, error = "That Discord account is already linked to another player." });

            var player = PlayerDB.Players.FindById(playerId.Value);
            if (player?.Player == null)
                return NotFound(new { success = false, error = "Player not found." });

            player.Player.DiscordUserId = pending.DiscordUserId.ToString();
            player.Player.DiscordUsername = pending.DiscordUsername;
            player.Player.DiscordLinked = true;
            player.Player.DiscordLinkedAt = DateTime.UtcNow;
            PlayerDB.Players.Update(player);

            await NotificationsController.CreateAndSendWSMessageRecieved(
                playerId.Value, 1, PlayerDBClasses.MessageType.TextMessage,
                $"Your Discord account (@{pending.DiscordUsername}) is now linked to your Vanadium account.");

            return Ok(new { success = true, discordUsername = pending.DiscordUsername, discordUserId = pending.DiscordUserId.ToString() });
        }

        public class VerifyDiscordRequest { public string? Code { get; set; } }

        private async Task PushRelToPlayer(long toPlayerId, long aboutPlayerId)
        {
            var rel = FriendsDB.GetRelationships(toPlayerId).FirstOrDefault(x => x.OtherPlayerID == aboutPlayerId);

            var payload = new
            {
                Id = "1",
                Msg = new
                {
                    PlayerID = aboutPlayerId,
                    RelationshipType = rel?.RelationshipType ?? RelationshipType.None,
                    Muted = rel?.Muted ?? ReciprocalStatus.None,
                    Ignored = rel?.Ignored ?? ReciprocalStatus.None,
                    Favorited = rel?.Favorited ?? ReciprocalStatus.None
                }
            };

            await NotificationsController.SendToPlayer(
                toPlayerId,
                JsonConvert.SerializeObject(payload)
            );
        }

        private async Task DeleteFriendInvites(long fromPlayerId, long toPlayerId)
        {
            var messages = PlayerDB.GetMessages(toPlayerId);
            if (messages == null) return;

            var messageToDelete = messages.FirstOrDefault(m =>
                m.FromPlayerId == fromPlayerId &&
                m.Type == PlayerDBClasses.MessageType.FriendInvite
            );

            if (messageToDelete != null)
            {
                PlayerDB.DeleteMessage(toPlayerId, messageToDelete.Id);

                var payload = new
                {
                    Id = "3",
                    Msg = new
                    {
                        Id = messageToDelete.Id
                    }
                };

                await NotificationsController.SendToPlayer(
                    toPlayerId,
                    JsonConvert.SerializeObject(payload)
                );
            }
        }

        [HttpGet("/api/relationships/v2/get")]
        public IActionResult GetMyRelationships()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var rels = FriendsDB.GetRelationships(id.Value)
                .Select(r => new
                {
                    PlayerID = r.OtherPlayerID,
                    r.RelationshipType,
                    r.Muted,
                    r.Ignored,
                    r.Favorited
                });

            return Ok(rels);
        }

        [HttpGet("/api/relationships/mutualfriends")]
        public IActionResult MutualFriendsGetBitch([FromQuery] long id)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null)
                return Unauthorized("");
            if (id == 0 || id == me.Value)
                return Ok(Array.Empty<object>());

            var mine = FriendsDB.GetRelationships(me.Value)
                .Where(r => r.RelationshipType == RelationshipType.Friend)
                .Select(r => r.OtherPlayerID)
                .ToHashSet();

            var theirs = FriendsDB.GetRelationships(id)
                .Where(r => r.RelationshipType == RelationshipType.Friend)
                .Select(r => r.OtherPlayerID)
                .ToHashSet();

            mine.IntersectWith(theirs);

            if (mine.Count == 0)
                return Ok(Array.Empty<object>());

            var result = mine.Select(mutualId => new
            {
                Id = (int)(mutualId & 0x7FFFFFFF),
                PlayerID = mutualId,
                OtherPlayerID = mutualId,
                RelationshipType = 3,
                Favorited = 0,
                Muted = 0,
                Ignored = 0,
                VoiceVolume = 100
            }).ToList();

            return Ok(result);
        }

        [HttpPost("/api/relationships/v1/mute")]
        public async Task<IActionResult> Mute([FromForm] long playerId)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null) return Unauthorized("");

            if (playerId == me.Value || playerId == 0)
                return Ok();

            var rel = FriendsDB.UpdateMute(me.Value, playerId, ReciprocalStatus.Local);

            await PushRelToPlayer(me.Value, playerId);
            await PushRelToPlayer(playerId, me.Value);

            return Ok(new
            {
                PlayerID = playerId,
                rel.RelationshipType,
                rel.Muted,
                rel.Ignored,
                rel.Favorited
            });
        }

        [HttpPost("/api/relationships/v1/unmute")]
        public async Task<IActionResult> Unmute([FromForm] long playerId)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null) return Unauthorized("");

            var rel = FriendsDB.UpdateMute(me.Value, playerId, ReciprocalStatus.None);

            await PushRelToPlayer(me.Value, playerId);
            await PushRelToPlayer(playerId, me.Value);

            return Ok(new
            {
                PlayerID = playerId,
                rel.RelationshipType,
                rel.Muted,
                rel.Ignored,
                rel.Favorited
            });
        }

        [HttpGet("/api/relationships/v2/addfriend")]
        [HttpGet("/api/relationships/v2/sendfriendrequest")]
        public async Task<IActionResult> SendFriendRequest([FromQuery] long id)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null)
                return StatusCode(403);

            var player = PlayerDB.Players.FindById(me.Value);
            if (player == null || PlayerDB.IsBanned(player.PlayerId))
                return StatusCode(StatusCodes.Status403Forbidden);

            if (id == 0 || id == me.Value)
                return Ok();

            var preRelationships = FriendsDB.GetRelationships(me.Value);
            var existingRel = preRelationships.FirstOrDefault(x => x.OtherPlayerID == id);

            var targetRelationships = FriendsDB.GetRelationships(id);
            var targetRel = targetRelationships.FirstOrDefault(x => x.OtherPlayerID == me.Value);

            if (existingRel?.RelationshipType == RelationshipType.Friend)
                return Ok();

            if (targetRel?.RelationshipType == RelationshipType.Sent || existingRel?.RelationshipType == RelationshipType.Received)
            {
                var rel = FriendsDB.AcceptFriendRequest(me.Value, id);
                if (rel == null) return BadRequest();

                await DeleteFriendInvites(me.Value, id);
                await DeleteFriendInvites(id, me.Value);

                var acceptedMsg = PlayerDB.AddMessage(id, me.Value, PlayerDBClasses.MessageType.FriendRequestAccepted, me.Value.ToString());
                if (acceptedMsg != null)
                {
                    var toTarget = JsonConvert.SerializeObject(new
                    {
                        Id = "2",
                        Msg = new
                        {
                            acceptedMsg.Id,
                            FromPlayerId = me.Value,
                            acceptedMsg.SentTime,
                            Type = (int)PlayerDBClasses.MessageType.FriendRequestAccepted,
                            Data = me.Value.ToString(),
                            RoomId = (long?)null,
                            PlayerEventId = (long?)null
                        }
                    });
                    await NotificationsController.SendToPlayer(id, toTarget);
                }

                await PushRelToPlayer(me.Value, id);
                await PushRelToPlayer(id, me.Value);

                return Ok(new
                {
                    PlayerID = id,
                    rel.RelationshipType,
                    rel.Muted,
                    rel.Ignored,
                    rel.Favorited
                });
            }

            var newRel = FriendsDB.SendFriendRequest(me.Value, id);

            await DeleteFriendInvites(id, me.Value);

            var inviteMsg = PlayerDB.AddMessage(id, me.Value, PlayerDBClasses.MessageType.FriendInvite, me.Value.ToString());
            if (inviteMsg != null)
            {
                var toTarget = JsonConvert.SerializeObject(new
                {
                    Id = "2",
                    Msg = new
                    {
                        inviteMsg.Id,
                        FromPlayerId = me.Value,
                        inviteMsg.SentTime,
                        Type = (int)PlayerDBClasses.MessageType.FriendInvite,
                        Data = me.Value.ToString(),
                        RoomId = (long?)null,
                        PlayerEventId = (long?)null
                    }
                });
                await NotificationsController.SendToPlayer(id, toTarget);
            }

            await PushRelToPlayer(me.Value, id);
            await PushRelToPlayer(id, me.Value);

            return Ok(new
            {
                PlayerID = id,
                newRel.RelationshipType,
                newRel.Muted,
                newRel.Ignored,
                newRel.Favorited
            });
        }

        [HttpGet("/api/relationships/v2/acceptfriendrequest")]
        public async Task<IActionResult> AcceptFriendRequest([FromQuery] long id)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null)
                return StatusCode(403);

            var player = PlayerDB.Players.FindById(me.Value);
            if (player == null || PlayerDB.IsBanned(player.PlayerId))
                return StatusCode(StatusCodes.Status403Forbidden);

            if (id == 0 || id == me.Value)
                return BadRequest();

            var rel = FriendsDB.AcceptFriendRequest(me.Value, id);
            if (rel == null)
                return BadRequest();

            await DeleteFriendInvites(id, me.Value);
            await DeleteFriendInvites(me.Value, id);

            var acceptedMsg = PlayerDB.AddMessage(id, me.Value, PlayerDBClasses.MessageType.FriendRequestAccepted, me.Value.ToString());
            if (acceptedMsg != null)
            {
                var toTarget = JsonConvert.SerializeObject(new
                {
                    Id = "2",
                    Msg = new
                    {
                        acceptedMsg.Id,
                        FromPlayerId = me.Value,
                        acceptedMsg.SentTime,
                        Type = (int)PlayerDBClasses.MessageType.FriendRequestAccepted,
                        Data = me.Value.ToString(),
                        RoomId = (long?)null,
                        PlayerEventId = (long?)null
                    }
                });
                await NotificationsController.SendToPlayer(id, toTarget);
            }

            await PushRelToPlayer(me.Value, id);
            await PushRelToPlayer(id, me.Value);

            return Ok(new
            {
                PlayerID = id,
                rel.RelationshipType,
                rel.Muted,
                rel.Ignored,
                rel.Favorited
            });
        }

        [HttpGet("/api/relationships/v2/removefriend")]
        public async Task<IActionResult> RemoveFriend([FromQuery] long id)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null)
                return StatusCode(403);

            var player = PlayerDB.Players.FindById(me.Value);
            if (player == null || PlayerDB.IsBanned(player.PlayerId))
                return StatusCode(StatusCodes.Status403Forbidden);

            if (id == 0 || id == me.Value)
                return BadRequest();
            var rel = FriendsDB.RemoveFriend(me.Value, id);
            await DeleteFriendInvites(id, me.Value);
            await DeleteFriendInvites(me.Value, id);
            await PushRelToPlayer(me.Value, id);
            await PushRelToPlayer(id, me.Value);
            return Ok(new
            {
                PlayerID = id,
                rel.RelationshipType,
                rel.Muted,
                rel.Ignored,
                rel.Favorited
            });
        }

        [HttpPost("/api/relationships/v1/ignore")]
        public async Task<IActionResult> Ignore([FromForm] long playerId)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null) return Unauthorized("");

            var rel = FriendsDB.UpdateIgnore(me.Value, playerId, ReciprocalStatus.Local);

            await PushRelToPlayer(me.Value, playerId);
            await PushRelToPlayer(playerId, me.Value);

            return Ok(new
            {
                PlayerID = playerId,
                rel.RelationshipType,
                rel.Muted,
                rel.Ignored,
                rel.Favorited
            });
        }

        [HttpPost("/api/relationships/v1/unignore")]
        public async Task<IActionResult> Unignore([FromForm] long playerId)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null) return Unauthorized("");

            var rel = FriendsDB.UpdateIgnore(me.Value, playerId, ReciprocalStatus.None);

            await PushRelToPlayer(me.Value, playerId);
            await PushRelToPlayer(playerId, me.Value);

            return Ok(new
            {
                PlayerID = playerId,
                rel.RelationshipType,
                rel.Muted,
                rel.Ignored,
                rel.Favorited
            });
        }



        [HttpGet("/api/relationships/v1/favorite")]
        public IActionResult Friend_Favorite([FromQuery] long id)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null) return Unauthorized("");

            var rel = FriendsDB.SetFavorite(me.Value, id, ReciprocalStatus.Local);

            return Ok(new
            {
                PlayerID = id,
                rel.RelationshipType,
                rel.Muted,
                rel.Ignored,
                rel.Favorited
            });
        }

        [HttpGet("/api/relationships/v1/unfavorite")]
        public IActionResult Friend_UnFavorite([FromQuery] long id)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null) return Unauthorized("");

            var rel = FriendsDB.SetFavorite(me.Value, id, ReciprocalStatus.None);

            return Ok(new
            {
                PlayerID = id,
                rel.RelationshipType,
                rel.Muted,
                rel.Ignored,
                rel.Favorited
            });
        }

        [HttpGet("/acc/account/search")]
        public async Task<IActionResult> SearchAccounts([FromQuery] string name)
        {
            /*var authId = AuthStuff.GetPlayerId(Request);
            if (authId == null)
                return StatusCode(403);*/

            //if (PlayerDB.IsBanned(player.PlayerId))
            //	return StatusCode(StatusCodes.Status403Forbidden);

            if (string.IsNullOrWhiteSpace(name))
                return Ok(new List<PlayerDTOBase>());

            name = name.Trim();

            bool isAtSearch = name.StartsWith("@");

            if (isAtSearch)
            {
                var target = name.Substring(1).ToLowerInvariant();

                var match = PlayerDB.Players.FindAll()
                    .FirstOrDefault(p =>
                        string.Equals(p.Player?.Username ?? "", target, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                    return Ok(new List<PlayerDTOBase>());

                var pl = match.Player;

                int platformFlags = match.PlatformIds?.Aggregate(0, (acc, pid) => acc | (int)pid.Platform) ?? 0;

                return Ok(new List<PlayerDTOBase>
                {
                    new PlayerDTO
                    {
                        accountId = match.PlayerId,
                        username = pl?.Username,
                        displayName = pl?.DisplayName,
                        profileImage = pl?.ProfileImage,
                        isJunior = pl?.IsJunior ?? false,
                        createdAt = pl?.CreatedAt ?? DateTime.UtcNow,
                        platforms = platformFlags,
                        personalPronouns = 0,
                        identityFlags = 0
                    }
                });
            }

            var q = name.ToLowerInvariant();

            var results = PlayerDB.Players.FindAll()
                .Where(p =>
                    (!string.IsNullOrEmpty(p.Player?.Username) &&
                     p.Player.Username.ToLowerInvariant().Contains(q)) ||
                    (!string.IsNullOrEmpty(p.Player?.DisplayName) &&
                     p.Player.DisplayName.ToLowerInvariant().Contains(q))
                )
                .Select(p =>
                {
                    var username = p.Player?.Username?.ToLowerInvariant() ?? "";
                    var display = p.Player?.DisplayName?.ToLowerInvariant() ?? "";

                    int score = 0;

                    if (username.StartsWith(q) || display.StartsWith(q))
                        score += 100;

                    if (username.Contains(q) || display.Contains(q))
                        score += 50;

                    score -= Math.Min(username.Length, display.Length);

                    return new { Player = p, Score = score };
                })
                .OrderByDescending(x => x.Score)
                .Take(50)
                .Select(x =>
                {
                    var pl = x.Player.Player;

                    int platformFlags = x.Player.PlatformIds?.Aggregate(0, (acc, pid) => acc | (int)pid.Platform) ?? 0;

                    return new PlayerDTO
                    {
                        accountId = x.Player.PlayerId,
                        username = pl?.Username,
                        displayName = pl?.DisplayName,
                        profileImage = pl?.ProfileImage,
                        isJunior = pl?.IsJunior ?? false,
                        createdAt = pl?.CreatedAt ?? DateTime.UtcNow,
                        platforms = platformFlags,
                        personalPronouns = 0,
                        identityFlags = 0
                    } as PlayerDTOBase;
                })
                .ToList();

            return Ok(results);
        }

        [HttpPost("/data/heartbeat")]
        public async Task<IActionResult> Heartbeat()
        {
            var jwtPlayerId = AuthStuff.GetPlayerId(Request);

            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(raw))
                return BadRequest("Empty body");

            var parsed = System.Web.HttpUtility.ParseQueryString(raw);
            var eventDataEncoded = parsed["eventData"];

            if (string.IsNullOrWhiteSpace(eventDataEncoded))
                return BadRequest("Missing eventData");

            string eventDataJson;

            try
            {
                eventDataJson = Uri.UnescapeDataString(eventDataEncoded);
            }
            catch
            {
                eventDataJson = eventDataEncoded;
            }

            using var outerDoc = JsonDocument.Parse(eventDataJson);
            var root = outerDoc.RootElement;

            string? eventType = null;
            if (root.TryGetProperty("EventType", out var et))
                eventType = et.GetString();

            string? eventParamsRaw = null;
            if (root.TryGetProperty("EventParams", out var ep))
                eventParamsRaw = ep.GetString();

            long resolvedAccountId = jwtPlayerId ?? 0;
            string? timeUtc = null;

            if (!string.IsNullOrWhiteSpace(eventParamsRaw))
            {
                try
                {
                    using var innerDoc = JsonDocument.Parse(eventParamsRaw);
                    var inner = innerDoc.RootElement;

                    if (inner.TryGetProperty("_AccountId", out var aid))
                    {
                        if (aid.ValueKind == JsonValueKind.Number)
                            resolvedAccountId = aid.GetInt64();
                        else if (aid.ValueKind == JsonValueKind.String && long.TryParse(aid.GetString(), out var parsedId))
                            resolvedAccountId = parsedId;
                    }

                    if (jwtPlayerId != null && resolvedAccountId != jwtPlayerId.Value)
                    {
                        Console.WriteLine($"[account/me switch detected] JWT player ID {jwtPlayerId.Value} does not match resolved account ID {resolvedAccountId}");
                        APIController.SendNonHileWebhook(jwtPlayerId.Value, $"[account/me switch detected] @everyone JWT player ID {jwtPlayerId.Value} does not match resolved account ID {resolvedAccountId}");
                        if (!PlayerDB.IsBanned(jwtPlayerId.Value))
                        {
                            var player = AuthStuff.GetCurrentPlayer(Request);
                            var mbd = new ModerationBlockDetails
                            {
                                IsBan = true,
                                ReportCategory = ReportCategory.Cheating,
                                Duration = 2147483647,
                                Message = "Cheating",
                                ModerationSetUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                            };
                            player.Player ??= new Player();
                            player.Player.PlayerExtra ??= new PlayerExtra();
                            PlayerDB.ApplyBan(jwtPlayerId.Value, "Cheating");
                            PlayerDB.Players.Update(player);
                            string wsEvent = Newtonsoft.Json.JsonConvert.SerializeObject(
                            WebsocketEvents.CreateWSEventString("ModerationQuitGame", new { }));
                            await NotificationsController.SendToPlayer(jwtPlayerId.Value, wsEvent);
                            await NotificationsController.SendBanned(jwtPlayerId.Value, mbd);
                        }
                    }

                    if (inner.TryGetProperty("_TimeUtc", out var tu))
                        timeUtc = tu.GetString();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EventParams parse failed:");
                    Console.WriteLine(eventParamsRaw);
                    Console.WriteLine(ex.Message);
                }
            }

            // Console.WriteLine($"Heartbeat | Type={eventType} | Account={resolvedAccountId} | Time={timeUtc}");

            if (resolvedAccountId <= 0)
                return Unauthorized("");

            var heartbeat = PlayerDB.GetPlayerHeartbeat(resolvedAccountId);
            return Ok(heartbeat);
        }

        [HttpPost("/upload")]
        public async Task<IActionResult> Upload()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var player = PlayerDB.Players.FindById(id.Value);
            if (player == null || PlayerDB.IsBanned(player.PlayerId)) return Unauthorized("");

            var file = Request.Form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return BadRequest("");

            if (!Request.Form.TryGetValue("fileType", out var fileTypeRaw) || !int.TryParse(fileTypeRaw.FirstOrDefault(), out int fileType))
                return BadRequest("");

            (string subDir, string extension) = fileType switch
            {
                0 => ("unknown", ".bin"),
                1 => ("room", ".room"),
                2 => ("htr", ".htr"),
                3 => ("Images", ".png"),
                //4 => ("video", ".mp4"),
                5 => ("inv", ".inv"),
                6 => ("meta", ".meta"),
                _ => ((string)null, (string)null)
            };

            if (subDir == null)
                return BadRequest($"{fileType}, boii");

            string basePath;
            if (fileType == 3)
            {
                basePath = Path.GetFullPath(Path.Join(Program.dataDir, subDir));
            }
            else
            {
                basePath = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", subDir));
            }

            Directory.CreateDirectory(basePath);

            var safeName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Join(basePath, safeName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            Console.WriteLine($":3, {safeName}");
            return Ok(new
            {
                filename = safeName
            });
        }

        [HttpGet("/api/messages/v2/get")]
        public async Task<IActionResult> GetMyMessages()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            return Ok(PlayerDB.GetMessages((long)id));
        }

        [HttpPost("/api/messages/v3/delete")]
        public async Task<IActionResult> DeleteMyMessages([FromBody] DeleteMessagesRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            if (request?.MessageIds == null || request.MessageIds.Count == 0)
                return BadRequest();

            PlayerDB.DeleteMessagesBulk((long)id, request.MessageIds);

            return NoContent();
        }

        public class DeleteMessagesRequest
        {
            public List<long> MessageIds { get; set; }
        }

        [HttpGet("/playersettings")]
        public async Task<IActionResult> GetMySettings()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
            return Ok(PlayerDB.GetPlayerSettings(id.Value));
        }


        [HttpPut("/playersettings")]
        public async Task<IActionResult> SetMySettings([FromForm] string key, [FromForm] string value)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            PlayerDB.SetPlayerSetting(key, value ?? "", (long)id);

            var version = AuthStuff.GetAndEnsureAppVersion(Request);
            if (version == "20250724")
                return Ok(new
                {
                    success = true,
                    errorId = (string?)null,
                    error = (string?)null
                });

            return NoContent();
        }

        [HttpDelete("/playersettings")]
        public IActionResult DeleteMySettings([FromForm] string key)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            if (string.IsNullOrWhiteSpace(key))
                return BadRequest("");

            PlayerDB.DeletePlayerSetting(key, (long)id);

            return Ok(new
            {
                success = true,
                errorId = (string?)null,
                error = (string?)null
            });
        }


        [HttpGet("/api/checklist/v1/current")]
        public async Task<IActionResult> GetMyChecklist()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/players/v2/progression/bulk")]
        public async Task<IActionResult> GetProgressionBulk([FromQuery(Name = "id")] List<long> ids)
        {
            if (ids == null || ids.Count == 0)
                return Ok(Array.Empty<object>());

            ids = ids.Distinct().ToList();

            var results = PlayerDB.GetProgressionBulk(ids);

            var foundIds = results.Select(r => r.PlayerId).ToHashSet();
            foreach (var id in ids.Where(i => !foundIds.Contains(i)))
            {
                results.Add(new PlayerProgressionDTO { PlayerId = id, Level = 1, XP = 0 });
            }

            /*foreach (var prog in results)
            {
                _ = NotiController.SendEvent(prog.PlayerId, "PlayerProgressionLevelUpdate", new
                {
                    PlayerId = prog.PlayerId,
                    Level = prog.Level,
                    XP = prog.XP
                });
            }*/

            return Ok(results.OrderBy(r => r.PlayerId));
        }

        [HttpGet("/api/playerReputation/v2/bulk")]
        public async Task<IActionResult> GetReputationBulk([FromQuery] List<long> id)
        {
            var authId = AuthStuff.GetPlayerId(Request);
            if (authId == null)
                return Unauthorized("");

            if (id == null || id.Count == 0)
                return Ok(new List<PlayerDBClasses.Reputation>());

            var results = PlayerDB.GetReputationBulk(id);
            return Ok(results);
        }

        private const string WebhookUrl = "https://discord.com/api/webhooks/1519900900372647937/5Q8tUiljiix2PclNDt1YtCUEPqf2H9c3iMB5dkVhf9q_4hamIpxzo2-hbYhvcJ5VRudx";

        private static async Task SendSanitizeWebhook(long accountId, string message, string instance)
        {
            var payload = new
            {
                username = "Vanadium",
                embeds = new[]
                {
                    new
                    {
                        title = "Vanadium Sanitize",
                        color = 3066993,
                        fields = new[]
                        {
                            new { name = "Account ID", value = accountId.ToString(), inline = true },
                            new { name = "Raw Message", value = message, inline = true },
                            new { name = "Instance", value = instance, inline = true }
                        }
                    }
                }
            };

            var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try { await _webhookClient.PostAsync(WebhookUrl, content); } catch { }
        }

        private static async Task<string> AppendCoachResponse(string message)
        {
            var response = await ChatController.RequestNvidiaAPIResponseAndReturnString(message);
            if (response == null)
                return message;

            return $"{message}\n\ncoach response: {response}";
        }

        [HttpPost("/v1/batch/rudderstack")]
        public IActionResult RudderstackShit()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpPost("/api/sanitize/v1")]
        public async Task<IActionResult> SanitizeV1([FromBody] SanitizeRequest request)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return StatusCode(403);

            var currentPlayerHeartbeat = PlayerDB.GetCurrentPlayer((long)playerId)?.Player?.PlayerExtra?.Heartbeat?.roomInstance;

            if (request.Value.ToLower().Contains("@coach"))
                request.Value = await AppendCoachResponse(request.Value);

            _ = SendSanitizeWebhook((long)playerId, request.Value, currentPlayerHeartbeat?.Name ?? "Unknown Room");

            if (string.IsNullOrEmpty(request?.Value))
            {
                return new JsonResult(string.Empty);
            }
            if (ServerConfig.AllowSwears)
            {
                return new JsonResult(request.Value);
            }
            if (request.Value.StartsWith("@Coach Videos"))
            {
                var filePath2 = Path.Combine(Environment.CurrentDirectory, "Data", "cdn", "video");
                string message = "Here is a list of all videos on the server:\n";
                if (Directory.Exists(filePath2))
                {
                    var videoFiles = Directory.GetFiles(filePath2, "*.mp4");
                    if (videoFiles.Length > 0)
                    {
                        foreach (var videoFile in videoFiles)
                        {
                            var fileName = Path.GetFileName(videoFile);
                            message += $"- {fileName}\n";
                        }
                    }
                    else
                    {
                        message += "No videos found.";
                    }
                }
                else
                {
                    message += "Video directory does not exist.";
                }
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                Id = "2",
                Msg = new
                {
                    FromPlayerId = 1,
                    Id = new Random().Next(1, 0x7ffffff),
                    SentTime = DateTime.UtcNow,
                    Type = 100,
                    Data = message,
                    RoomId = 1
                }
            });
            await NotificationsController.SendToPlayer((long)playerId, json);
            }
            string filePath = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "swears.txt");
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("");
            }
            string sanitizedText = request.Value;
            var target = PlayerDB.Players.FindById(playerId);
            if (target.PlayerRoles.Contains(PlayerRoles.Developer))
            {
                return new JsonResult(sanitizedText);
            }

            var swearWords = await System.IO.File.ReadAllLinesAsync(filePath);
            foreach (var word in swearWords.Select(w => w.Trim()).Where(w => !string.IsNullOrEmpty(w)))
            {
                string pattern = $@"(?<=[^a-zA-Z]|^)({Regex.Escape(word)})(?=[^a-zA-Z]|$)|(?<=[^a-zA-Z]|^)({Regex.Escape(word)})([^a-zA-Z\s]+)|([^a-zA-Z\s]+)({Regex.Escape(word)})(?=[^a-zA-Z]|$)|([^a-zA-Z\s]+)({Regex.Escape(word)})([^a-zA-Z\s]+)";
                sanitizedText = Regex.Replace(sanitizedText, pattern, m =>
                {
                    var groups = m.Groups;
                    if (groups[1].Success) return new string('*', groups[1].Length);
                    if (groups[2].Success) return new string('*', groups[2].Length) + groups[3].Value;
                    if (groups[5].Success) return groups[4].Value + new string('*', groups[5].Length);
                    return groups[6].Value + new string('*', groups[7].Length) + groups[8].Value;
                }, RegexOptions.IgnoreCase);
            }
            return new JsonResult(sanitizedText);
        }

        [HttpPost("/api/sanitize/v1/isPure")]
        public async Task<IActionResult> SanitizeV1IsPure([FromBody] SanitizeRequest request)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return StatusCode(403);
            var target = PlayerDB.Players.FindById(playerId);
            if (target.PlayerRoles.Contains(PlayerRoles.Developer))
            {
                return Ok(new { IsPure = true });
            }

            if (ServerConfig.AllowSwears || string.IsNullOrEmpty(request?.Value))
            {
                return Ok(new { IsPure = true });
            }

            string filePath = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "swears.txt");
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("");
            }

            var swearWords = await System.IO.File.ReadAllLinesAsync(filePath);

            foreach (var word in swearWords.Select(w => w.Trim()).Where(w => !string.IsNullOrEmpty(w)))
            {
                string pattern = $@"\b{Regex.Escape(word)}\b";
                if (Regex.IsMatch(request.Value, pattern, RegexOptions.IgnoreCase))
                {
                    return Ok(new { IsPure = false });
                }
            }

            return Ok(new { IsPure = true });
        }

        [HttpPut("/reports/{reportuuid}")]
        public async Task<IActionResult> InitializeReport(string reportuuid, [FromBody] ReportsDBClasses.ReportInitializationRequest request)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return StatusCode(403);

            if (request == null || string.IsNullOrEmpty(reportuuid)) return BadRequest();

            var report = ReportsDB.GetOrCreateReport(reportuuid, (long)playerId);
            if (report.PlayerId != (long)playerId) return StatusCode(403);

            if (request.Client != null)
            {
                report.TriggerId = request.Client.TriggerId;
                report.TriggerType = request.Client.TriggerType;
                report.TriggeredAt = request.Client.TriggeredAt;
            }

            ReportsDB.UpdateReport(report);
            return Ok(new { success = true });
        }

        [HttpPut("/reports/{reportuuid}/attachments/Trace.log")]
        public async Task<IActionResult> UploadTraceLog(string reportuuid)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return StatusCode(403);

            var report = ReportsDB.GetReport(reportuuid);
            if (report == null || report.PlayerId != (long)playerId) return StatusCode(403);

            string dir = Path.Combine(Program.dataDir, "TempReports", reportuuid);
            Directory.CreateDirectory(dir);
            string filePath = Path.Combine(dir, "Trace.log");

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                await Request.Body.CopyToAsync(fs);
            }

            report.TraceLogPath = filePath;
            ReportsDB.UpdateReport(report);

            return Ok(new { success = true });
        }

        [HttpPut("/reports/{reportuuid}/userinfo")]
        public async Task<IActionResult> UploadReportUserInfo(string reportuuid, [FromBody] ReportsDBClasses.ReportUserInfoRequest request)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return StatusCode(403);

            if (request == null) return BadRequest();

            var report = ReportsDB.GetReport(reportuuid);
            if (report == null || report.PlayerId != (long)playerId) return StatusCode(403);

            report.Title = request.Title;
            report.Description = request.Description;

            ReportsDB.UpdateReport(report);
            return Ok(new { success = true });
        }

        [HttpPut("/reports/{reportuuid}/attachments/game_screenshot.png")]
        public async Task<IActionResult> UploadGameScreenshot(string reportuuid)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return StatusCode(403);

            var report = ReportsDB.GetReport(reportuuid);
            if (report == null || report.PlayerId != (long)playerId) return StatusCode(403);

            string dir = Path.Combine(Program.dataDir, "TempReports", reportuuid);
            Directory.CreateDirectory(dir);
            string filePath = Path.Combine(dir, "game_screenshot.png");

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                await Request.Body.CopyToAsync(fs);
            }

            report.GameScreenshotPath = filePath;
            ReportsDB.UpdateReport(report);

            return Ok(new { success = true });
        }

        [HttpPut("/Reports/{reportuuid}/Complete")]
        public async Task<IActionResult> CompleteReport(string reportuuid)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return StatusCode(403);

            var report = ReportsDB.GetReport(reportuuid);
            if (report == null || report.PlayerId != (long)playerId) return StatusCode(403);

            if (string.IsNullOrEmpty(report.Title) || string.IsNullOrEmpty(report.TraceLogPath) || !System.IO.File.Exists(report.TraceLogPath))
            {
                return BadRequest(new { success = false });
            }

            string reporterText = "Unknown Reporter";
            var reporter = PlayerDB.Players.FindById((long)playerId);
            if (reporter?.Player != null)
            {
                reporterText = $"{reporter.Player.DisplayName} (@{reporter.Player.Username})";
            }

            string targetChannelId = "1507514835443650590";

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", ServerConfig.CountBotToken);

                using var form = new MultipartFormDataContent();

                var embedObj = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;
                embedObj["title"] = $"**{report.Title}**";
                embedObj["description"] = string.IsNullOrWhiteSpace(report.Description) ? "*No description provided.*" : report.Description;
                embedObj["color"] = 16731136;
                embedObj["footer"] = new { text = $"Uploaded by: {reporterText}" };
                embedObj["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                if (!string.IsNullOrEmpty(report.GameScreenshotPath) && System.IO.File.Exists(report.GameScreenshotPath))
                {
                    var screenshotBytes = await System.IO.File.ReadAllBytesAsync(report.GameScreenshotPath);
                    var screenshotContent = new ByteArrayContent(screenshotBytes);
                    screenshotContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    form.Add(screenshotContent, "files[1]", "game_screenshot.png");
                    embedObj["thumbnail"] = new { url = "attachment://game_screenshot.png" };
                }

                var logBytes = await System.IO.File.ReadAllBytesAsync(report.TraceLogPath);
                var logContent = new ByteArrayContent(logBytes);
                logContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
                form.Add(logContent, "files[0]", "Trace.log");

                var payloadJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    embeds = new[] { embedObj }
                });
                form.Add(new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload_json");

                var response = await client.PostAsync($"https://discord.com/api/v10/channels/{targetChannelId}/messages", form);

                if (response.IsSuccessStatusCode)
                {
                    report.IsCompleted = true;
                    ReportsDB.UpdateReport(report);

                    try
                    {
                        if (System.IO.File.Exists(report.TraceLogPath)) System.IO.File.Delete(report.TraceLogPath);
                        if (!string.IsNullOrEmpty(report.GameScreenshotPath) && System.IO.File.Exists(report.GameScreenshotPath)) System.IO.File.Delete(report.GameScreenshotPath);
                        Directory.Delete(Path.Combine(Program.dataDir, "TempReports", reportuuid));
                    }
                    catch { }

                    return Ok(new { success = true });
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Discord Error] failed to post bug report: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Reports Controller Error]: {ex.Message}");
            }

            return Ok(new { success = false });
        }

        [HttpGet("/api/rooms/v1/filters")]
        public IActionResult GetRoomTags()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var roomTags = new
            {
                PinnedFilters = new[]
                {
                    "recroomoriginal", "community", "featured", "quest", "pvp", "hangout", "game",
                    "art", "store", "tutorial", "fandom", "performance", "action", "horror"
                },
                PopularFilters = new[]
                {
                    "pvp", "quest", "game", "hangout", "art"
                },
                TrendingFilters = new[]
                {
                    "roleplay", "nomp", "rp", "casual", "fun", "action", "military", "sports"
                }
            };

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(roomTags),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("/api/rooms/v2/report")]
        public async Task<IActionResult> ReportRoom([FromForm] long roomId, [FromForm] string details, [FromForm] int reportCategory)
        {
            var reporterId = AuthStuff.GetPlayerId(Request);
            if (reporterId == null)
                return StatusCode(403);

            string categoryName = reportCategory switch
            {
                101 => "Sexual Content in a Public Room",
                102 => "Racist or discriminatory content",
                104 => "Inappropriate name, picture or description",
                103 => "Disruptive Trolling",
                11 => "Misleading purchases",
                6 => "Other",
                _ => $"Unknown Category ({reportCategory})"
            };

            string roomName = "Unknown Room";
            string roomOwnerText = "Unknown Owner";
            string thumbnailUrl = ServerConfig.BaseURL + "/cdn/images/DefaultRoom.png";

            var room = RoomDB.Rooms.FindById(roomId);
            if (room != null)
            {
                roomName = room.Name ?? "Unnamed Room";
                if (!string.IsNullOrWhiteSpace(room.ImageName))
                {
                    thumbnailUrl = $"{ServerConfig.BaseURL}/imageserver/{room.ImageName}";
                }

                var owner = PlayerDB.Players.FindById(room.CreatorAccountId);
                if (owner?.Player != null)
                {
                    roomOwnerText = $"{owner.Player.DisplayName} (@{owner.Player.Username})";
                }
            }

            string reporterText = "Unknown Reporter";
            var reporter = PlayerDB.Players.FindById((long)reporterId);
            if (reporter?.Player != null)
            {
                reporterText = $"{reporter.Player.DisplayName} (@{reporter.Player.Username})";
            }

            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"**{categoryName}**",
                        description = string.IsNullOrWhiteSpace(details) ? "*No details provided.*" : details,
                        color = 15158332,
                        thumbnail = new { url = thumbnailUrl },
                        fields = new[]
                        {
                            new { name = "Room Name", value = roomName, inline = true },
                            new { name = "Room Owner", value = roomOwnerText, inline = true },
                            new { name = "Reporter", value = reporterText, inline = false }
                        },
                        footer = new { text = "Vanadium" },
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    }
                }
            };

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", ServerConfig.CountBotToken);

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://discord.com/api/v10/channels/1507514835443650590/messages", content);
                if (response.IsSuccessStatusCode)
                {
                    return Ok(new { success = true });
                }
            }
            catch { }

            return Ok(new { success = false });
        }

        [Route("/api/PlayerReporting/v1/hile")]
        public async Task<IActionResult> PlayerReportingHile([FromForm] string? Message, [FromForm] HileType? Type, [FromForm] long? ReportedPlayer)
        {
            var authId = AuthStuff.GetPlayerId(Request);
            if (authId == null)
                return Unauthorized("");
            long playerId = (long)authId;

            bool isFullyIgnored = Type == HileType.ImageSignature
                || Type == HileType.AppData_Boot_InvalidSignature
                || Type == HileType.AppData_Boot_UnableToVerifySignatures
                || Type == HileType.Driver_Invalid_Signature
                || Type == HileType.Obscured;

            bool isLogOnly = Type == HileType.Photon_InstantiateTool;

            if (!isFullyIgnored)
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound("");

                string? webhookMessage = isLogOnly ? $"{Message} (not banned)" : Message;
                await SendHileWebhook(playerId, webhookMessage, Type, ReportedPlayer);

                if (!isLogOnly && !PlayerDB.IsBanned(playerId))
                {
                    var mbd = new ModerationBlockDetails
                    {
                        IsBan = true,
                        ReportCategory = ReportCategory.Cheating,
                        Duration = 2147483647,
                        Message = "Cheating ((Abusing cheats or exploits to disrupt other players experience. (ban_code: door) (DM a mod or admin to appeal)) Future bans may be longer or permanent).",
                        ModerationSetUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                    player.Player ??= new Player();
                    player.Player.PlayerExtra ??= new PlayerExtra();
                    player.Player.PlayerExtra.ModerationBlockDetails = mbd;
                    player.Player.PlayerExtra.Heartbeat = new Heartbeat
                    {
                        playerId = playerId,
                        isOnline = false,
                        roomInstance = null,
                        errorCode = 0
                    };
                    PlayerDB.Players.Update(player);
                    await NotiController.SendPresenceUpdate(playerId, player.Player.PlayerExtra.Heartbeat);
                    await NotiController.SendAccountUpdate(playerId, PlayerDB.GetAccountMe(playerId));
                    await NotificationsController.SendBanned(playerId, mbd);
                }
            }

            return Ok(false);
        }

        internal static async Task SendHileWebhook(
            long accountId,
            string? message,
            HileType? type,
            long? reportedPlayer,
            byte[]? imageBytes = null,
            string? imageFileName = null)
        {
            reportedPlayer ??= 0;

            var embed = new Dictionary<string, object?>
            {
                ["title"] = "Vanadium Hile API (game anticheat 1) bro cheating",
                ["color"] = 3066993,
                ["fields"] = new object[]
                {
                    new { name = "Account ID", value = accountId.ToString(), inline = true },
                    new { name = "Type", value = type?.ToString() ?? "Unknown", inline = true },
                    new { name = "Message", value = message ?? "No message provided", inline = false },
                    new { name = "Reported Player", value = reportedPlayer.ToString(), inline = false }
                }
            };

            if (imageBytes != null && imageFileName != null)
                embed["image"] = new { url = $"attachment://{imageFileName}" };

            var payload = new
            {
                username = "Vanadium",
                embeds = new[] { embed }
            };

            try
            {
                if (imageBytes != null && imageFileName != null)
                {
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent(
                        System.Text.Json.JsonSerializer.Serialize(payload),
                        Encoding.UTF8, "application/json"), "payload_json");

                    var fileContent = new ByteArrayContent(imageBytes);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    form.Add(fileContent, "files[0]", imageFileName);

                    await _webhookClient.PostAsync(
                        "https://discord.com/api/webhooks/1517032697371824178/UmlTVL9C_ur41T1q9sybTkK2F9pvNGp7WZ4GJIzxaSOo7T545aUAchPsK_AVDM0TDPEH",
                        form);
                }
                else
                {
                    var content = new StringContent(
                        System.Text.Json.JsonSerializer.Serialize(payload),
                        Encoding.UTF8, "application/json");
                    await _webhookClient.PostAsync(
                        "https://discord.com/api/webhooks/1517032697371824178/UmlTVL9C_ur41T1q9sybTkK2F9pvNGp7WZ4GJIzxaSOo7T545aUAchPsK_AVDM0TDPEH",
                        content);
                }
            }
            catch { }
        }

        internal static async Task SendNonHileWebhook(
            long accountId,
            string? message)
        {

            var embed = new Dictionary<string, object?>
            {
                ["title"] = $"ID = {accountId}",
                ["color"] = 3066993,
                ["fields"] = new object[]
                {
                    new { name = "Message", value = message ?? "No message provided", inline = false }
                }
            };

            var payload = new
            {
                username = "Vanadium",
                embeds = new[] { embed }
            };

            try
            {
                var content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(payload),
                    Encoding.UTF8, "application/json");
                await _webhookClient.PostAsync(
                    "https://discord.com/api/webhooks/1535007757286113353/gX1m4BxZVN8ZCcvHzUxB7CZO8KkQSSI6fmYWczk7jbh7FMV0bzpNCQg5ZWLaSo_sofDq",
                    content);
            }
            catch { }
        }

        public static readonly HttpClient _webhookClient = new();

        [HttpGet("api/config/v2")]
        public async Task<IActionResult> ApiConfigV2()
        {
            string path = Path.Join(Program.dataDir, "APIS", "ConfigV2.json");
            return System.IO.File.Exists(path) ? Content(System.IO.File.ReadAllText(path), "application/json") : NotFound();
        }

        [HttpGet("api/config/v1/backtrace")]
        public async Task<IActionResult> BacktraceTemp([FromQuery] string platformType, [FromQuery] bool allocate)
        {
            return Ok(new { success = true, error_message = (string?)null, value = (object?)null });
        }

        [HttpGet("/cdn/config/LoadingScreenTipData")]
        public async Task<IActionResult> GetLoadingScreenTips()
        {
            string path = Path.Join(Program.dataDir, "APIS", "loadingscreens.json");
            return System.IO.File.Exists(path) ? Content(System.IO.File.ReadAllText(path), "application/json") : NotFound();
        }

        [HttpGet("/cdn/config/{fileName}")]
        public async Task<IActionResult> GetConfig2025(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("failed to find");

            string basePath = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "config"));
            string filePath = Path.Join(basePath, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound("");

            try
            {
                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                return File(fileBytes, SafeGetContentType(fileName), fileName);
            }
            catch
            {
                return StatusCode(500);
            }
        }

        [HttpGet("/api/progressionEvents/active")]
        public async Task<IActionResult> progressionActiveEvenet()
        {
            return Ok(false);
        }

        [HttpGet("/outfits/me/saved")]
        [HttpGet("/api/avatar/v{ver}/saved")]
        public ActionResult GetOutfit()
        {

            long? id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var fullPlayer = PlayerDB.GetCurrentPlayer(id.Value);
            if (fullPlayer?.Player?.PlayerExtra == null)
                return NotFound("");

            var savedOutfits = fullPlayer.Player.PlayerExtra.SavedAvatars;

            string jsonResponse = System.Text.Json.JsonSerializer.Serialize(savedOutfits);

            return new ContentResult()
            {
                Content = jsonResponse,
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("/api/avatar/v{ver}/saved/set")]
        public async Task<IActionResult> SetSavedAvatar([FromBody] SavedAvatarRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            if (request == null)
                return BadRequest();

            var player = PlayerDB.Players.FindById(id.Value);
            if (player?.Player?.PlayerExtra == null)
                return NotFound("");

            player.Player.PlayerExtra.SavedAvatars ??= new List<PlayerDBClasses.SavedOutfit>();

            var existing = player.Player.PlayerExtra.SavedAvatars
                .FirstOrDefault(s => s.Slot == request.Slot);

            if (existing != null)
            {
                existing.OutfitSelections = request.OutfitSelections ?? existing.OutfitSelections;
                existing.FaceFeatures = request.FaceFeatures ?? existing.FaceFeatures;
                existing.SkinColor = request.SkinColor ?? existing.SkinColor;
                existing.HairColor = request.HairColor ?? existing.HairColor;
                existing.PreviewImageName = request.PreviewImageName ?? existing.PreviewImageName;
                existing.Name = request.Name ?? existing.Name;
            }
            else
            {
                player.Player.PlayerExtra.SavedAvatars.Add(new PlayerDBClasses.SavedOutfit
                {
                    Slot = request.Slot,
                    OutfitSelections = request.OutfitSelections ?? "",
                    FaceFeatures = request.FaceFeatures ?? "",
                    SkinColor = request.SkinColor ?? "",
                    HairColor = request.HairColor ?? "",
                    PreviewImageName = request.PreviewImageName,
                    Name = request.Name
                });
            }

            PlayerDB.Players.Update(player);

            return Ok(new { success = true });
        }

        /*[HttpGet("/api/equipment/v2/getUnlocked")]
		public async Task<IActionResult> Test2025Skins()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
            string path = Path.Join(Program.dataDir, "APIS", "2025Consumables", "Equipment.json");
            return PhysicalFile(path, "application/json");
        }*/

        [HttpGet("/sections/pagesource/{page}")]
        public IActionResult WatchPagesTypeShit(string page)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "2025Watch", $"{page}.json");

            if (!System.IO.File.Exists(path))
                return Ok();

            return PhysicalFile(path, "application/json");
        }

        [HttpGet("/api/freegifts/v1/sendmultiple")] // to-do gifts system
        public async Task<IActionResult> SendFreeGift()
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return Unauthorized("");

            return Ok(new { success = true });
        }

        [HttpGet("/api/roomcurrencies/v1/currencies")]
        public IActionResult idkWhatThisIs() => Ok();


        [HttpGet("/api/keepsakes/globalconfig")]
        public async Task<IActionResult> GetKeepsakeGlobalConfig()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            return Ok(new
            {
                KeepsakeFeatureEnabled = true,
                KeepsakeRoomLimit = 10,
                SocialXpBoostEnabled = false
            });
        }

        [HttpPost("/api/screensharereports/v1/report")]
        public async Task<IActionResult> ReportScreenShareIssue([FromForm] string imageName, [FromForm] long? reportedPlayerId, [FromForm] long? roomId, [FromForm] long? roomInstanceId, [FromForm] string roomInstanceType, [FromForm] string details)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var value = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(imageName))
                value["imageName"] = imageName;
            if (reportedPlayerId.HasValue)
                value["reportedPlayerId"] = reportedPlayerId.Value;
            if (roomId.HasValue)
                value["roomId"] = roomId.Value;
            if (roomInstanceId.HasValue)
                value["roomInstanceId"] = roomInstanceId.Value;
            if (!string.IsNullOrEmpty(roomInstanceType))
                value["roomInstanceType"] = roomInstanceType;
            if (!string.IsNullOrEmpty(details))
                value["details"] = details;

            if (value.Count == 0)
                return Ok(new { success = true });

            return Ok(new { success = true, value });
        }

        [HttpGet("/api/quickPlay/v1/getandclear")]
        public async Task<IActionResult> GetAndClearQuickPlay()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            return NoContent();
        }

        [HttpGet("/api/keepsakes/rooms/{roomId}")]
        public async Task<IActionResult> GetKeepsakesForRoom(ulong roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            return NoContent();
        }


        [HttpGet("/api/activities/charades/v1/words/Icebreakers")]
        public async Task<IActionResult> GetIcebreakers()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "Icebreakers.json");
            if (!System.IO.File.Exists(path))
                return Ok(Array.Empty<object>());

            return PhysicalFile(path, "application/json");
        }

        [HttpGet("/api/activities/charades/v1/words/Charades")]
        public async Task<IActionResult> GetCharades()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "Charades.json");
            if (!System.IO.File.Exists(path))
                return Ok(Array.Empty<object>());

            return PhysicalFile(path, "application/json");
        }

        // RoomConsumableController.cs replaces bracket response

        [HttpGet("/api/influencerpartnerprogram/influencers")]
        public async Task<IActionResult> GetAllInfluencers()
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return Unauthorized("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new
                {
                    InfluencerIds = PlayerDB.GetInfluencerIds(),
                    ContinuationToken = (string?)null
                }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("/api/storefronts/v1/toptoday")]
        public async Task<IActionResult> TopTodayStore()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpPost("/api/influencerpartnerprogram/support")]
        public async Task<IActionResult> SupportInfluencer()
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return Unauthorized("");

            string? SupportIdStr = HttpContext.Request.Form["influencerAccountId"].ToString();
            ulong SupportId = ulong.Parse(SupportIdStr);
            PlayerDB.SupportInfluencer(account.Value, (long)SupportId);

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new
                {
                    success = true,
                    error = "",
                    value = (string?)null
                }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("/api/influencerpartnerprogram/remove")]
        public async Task<IActionResult> RemoveInfluencer()
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return Unauthorized("");

            PlayerDB.SupportInfluencer(account.Value, 0);

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new
                {
                    success = true,
                    error = "",
                    value = (string?)null
                }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }
        
        [HttpGet("/api/influencerpartnerprogram/influencer")]
        public IActionResult GetInfluencer([FromQuery] long accountId)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return Unauthorized("");
            var player = PlayerDB.Players.FindById(accountId);
            return Ok(player?.Player?.PlayerExtra?.Influencer?.SupportingInfluencer ?? 0);
        }

        [HttpGet("/api/influencerpartnerprogram/myinfluencer")]
        public async Task<IActionResult> MyInfluencer()
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return Unauthorized("");

            var myInfluencer = PlayerDB.GetMyInfluencer(account.Value);
            if (myInfluencer == 0)
                return NotFound("");

            return Ok(myInfluencer);
        }

        [HttpGet("/api/communityboard/v2/current")]
        public async Task<IActionResult> CurrentCommunityBoard()
        {
            string filePath = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "communityboard.json");
            string content = System.IO.File.ReadAllText(filePath);
            return new ContentResult()
            {
                Content = content,
                ContentType = "application/json",
                StatusCode = 200
            };
        }


        [HttpGet("/cdn/data/{fileName}")]
        public async Task<IActionResult> GetCdnData(string fileName)
        {
            var account = AuthStuff.GetPlayerId(Request);
            	if (account == null) return Unauthorized("");
        
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("failed to find");

            fileName = Path.GetFileName(fileName);

            string basePath = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "htr"));
            Directory.CreateDirectory(basePath);

            string filePath = Path.Combine(basePath, fileName);

            string existingFilePath = System.IO.File.Exists(filePath) ? filePath : null;

            if (existingFilePath != null)
            {
                try
                {
                    var fileBytes = await System.IO.File.ReadAllBytesAsync(existingFilePath);
                    return File(fileBytes, SafeGetContentType(fileName), fileName);
                }
                catch
                {
                    return StatusCode(500, "Error reading file");
                }
            }

            string[] remoteUrls = new[]
            {
                $"http://eq-cdn.gabethefirst.com/data/{fileName}",
                $"https://cdn.cookedasset.com/data/{fileName}",
                $"https://vanadium.reloxa.xyz/data/{fileName}",
                $"https://cdn.enumrec.net/data/{fileName}"
            };

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "BestHTTP");
            httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            httpClient.DefaultRequestHeaders.Add("Referer", "https://rec.net/");
            httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

            foreach (var remoteUrl in remoteUrls)
            {
                try
                {
                    using var response = await httpClient.GetAsync(remoteUrl, HttpCompletionOption.ResponseHeadersRead);

                    if (response.IsSuccessStatusCode)
                    {
                        await using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await response.Content.CopyToAsync(fs);
                        }

                        var downloadedBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                        return File(downloadedBytes, SafeGetContentType(fileName), fileName);
                    }
                }
                catch
                {
                }
            }

            return NotFound("");
        }

        [HttpGet("/cdn/invention/{fileName}")]
        public async Task<IActionResult> GetInventionData(string fileName)
        {
            var account = AuthStuff.GetPlayerId(Request);
            	if (account == null) return Unauthorized("");
        
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("failed to find");

            string basePath = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "inv"));
            string filePath = Path.Join(basePath, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound("");

            try
            {
                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                return File(fileBytes, SafeGetContentType(fileName), fileName);
            }
            catch
            {
                return StatusCode(500);
            }
        }

        [HttpGet("/cdn/sigs/{fileName}")]
        public async Task<IActionResult> GetSig(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("failed to find");

            string basePath = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "sigs"));
            string filePath = Path.Join(basePath, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound("");

            try
            {
                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                return File(fileBytes, SafeGetContentType(fileName), fileName);
            }
            catch
            {
                return StatusCode(500);
            }
        }

        [HttpGet("/cdn/img/{fileName}")]
        public async Task<IActionResult> GetImage(string fileName)
        {
            var account = AuthStuff.GetPlayerId(Request);
            	if (account == null) return Unauthorized("");
        
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("failed to find");

            string basePath = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "img"));
            string filePath = Path.Join(basePath, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound("");

            try
            {
                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                return File(fileBytes, SafeGetContentType(fileName), fileName);
            }
            catch
            {
                return StatusCode(500);
            }
        }

        [HttpGet("/cdn/video/{fileName}")]
        public async Task<IActionResult> GetVideoData(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("failed to find");

            fileName = Path.GetFileName(fileName);

            string basePath = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "video"));
            string filePath = Path.Join(basePath, fileName);

            if (System.IO.File.Exists(filePath))
            {
                string firstLine;
                using (var sr = new StreamReader(filePath))
                    firstLine = (await sr.ReadLineAsync())?.Trim() ?? "";

                bool isYouTubeLink =
                    firstLine.StartsWith("https://www.youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("https://youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("https://youtu.be", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("http://www.youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("http://youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    firstLine.StartsWith("http://youtu.be", StringComparison.OrdinalIgnoreCase);

                if (isYouTubeLink)
                    return await StreamYouTubeVideo(firstLine);

                return PhysicalFile(filePath, "video/mp4", enableRangeProcessing: true);
            }

            return NotFound("");
        }

        private async Task<IActionResult> StreamYouTubeVideo(string youtubeUrl)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = $"--no-playlist -f bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best -g \"{youtubeUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null)
                    return StatusCode(500, "yt-dlp unavailable");

                string directUrl = (await process.StandardOutput.ReadLineAsync())?.Trim() ?? "";
                await process.WaitForExitAsync();

                if (string.IsNullOrWhiteSpace(directUrl))
                    return StatusCode(502, "Could not resolve stream URL");

                using var http = new HttpClient();

                var rangeHeader = Request.Headers["Range"].FirstOrDefault();
                if (!string.IsNullOrEmpty(rangeHeader))
                    http.DefaultRequestHeaders.Add("Range", rangeHeader);

                var upstreamResponse = await http.GetAsync(directUrl, HttpCompletionOption.ResponseHeadersRead);

                Response.Headers["Accept-Ranges"] = "bytes";
                Response.Headers["Content-Type"] = "video/mp4";
                Response.Headers["Cache-Control"] = "no-store";

                if (upstreamResponse.Headers.TryGetValues("Content-Length", out var cl))
                    Response.Headers["Content-Length"] = cl.First();

                if (upstreamResponse.Headers.TryGetValues("Content-Range", out var cr))
                    Response.Headers["Content-Range"] = cr.First();

                Response.StatusCode = (int)upstreamResponse.StatusCode;

                await using var stream = await upstreamResponse.Content.ReadAsStreamAsync();
                await stream.CopyToAsync(Response.Body);

                return new EmptyResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[YouTube Stream Error]: {ex.Message}");
                return StatusCode(500);
            }
        }
       

        [HttpGet("/api/customAvatarItems/v1/me")]
        public IActionResult stuffok() => Ok(new { results = Array.Empty<object>(), totalResults = 0 });

        [HttpGet("/cdn/room/{fileName}")]
        public async Task<IActionResult> GetRoomData(string fileName)
        {
           
        
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest();

            fileName = Path.GetFileName(fileName);

            bool isMeta = Path.GetExtension(fileName).Equals(".meta", StringComparison.OrdinalIgnoreCase);

            string roomPath = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "room"));
            string metaPath = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "meta"));

            Directory.CreateDirectory(roomPath);
            Directory.CreateDirectory(metaPath);

            string primaryPath = Path.Join(isMeta ? metaPath : roomPath, fileName);
            string secondaryPath = Path.Join(isMeta ? roomPath : metaPath, fileName);

            string filePath = System.IO.File.Exists(primaryPath) ? primaryPath
                        : System.IO.File.Exists(secondaryPath) ? secondaryPath
                        : primaryPath;

            try
            {
                if (!System.IO.File.Exists(filePath))
                {
                    using var client = new HttpClient();
                    var response = await client.GetAsync($"https://vanadium.reloxa.xyz/room/{fileName}");

                    if (!response.IsSuccessStatusCode)
                        return NotFound("");

                    await using var remoteStream = await response.Content.ReadAsStreamAsync();
                    await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await remoteStream.CopyToAsync(fileStream);
                }

                var fileInfo = new FileInfo(filePath);
                var now = DateTimeOffset.UtcNow;

                string md5Base64;
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    await using var readStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    md5Base64 = Convert.ToBase64String(await md5.ComputeHashAsync(readStream));
                }

                string requestId = Guid.NewGuid().ToString();
                string azureRef = $"{now:yyyyMMddTHHmmssZ}-{Guid.NewGuid().ToString("N")[..32]}";
                string eTag = $"\"0x{fileInfo.LastWriteTimeUtc.Ticks:X}\"";

                Response.Headers["Date"] = now.ToString("R");
                Response.Headers["Cache-Control"] = "public, max-age=31536000";
                Response.Headers["Last-Modified"] = fileInfo.LastWriteTimeUtc.ToString("R");
                Response.Headers["ETag"] = eTag;
                Response.Headers["x-ms-request-id"] = requestId;
                Response.Headers["x-ms-version"] = "2021-04-10";
                Response.Headers["x-ms-version-id"] = fileInfo.LastWriteTimeUtc.ToString("o");
                Response.Headers["x-ms-is-current-version"] = "true";
                Response.Headers["x-ms-creation-time"] = fileInfo.CreationTimeUtc.ToString("R");
                Response.Headers["x-ms-blob-content-md5"] = md5Base64;
                Response.Headers["x-ms-lease-status"] = "unlocked";
                Response.Headers["x-ms-lease-state"] = "available";
                Response.Headers["x-ms-blob-type"] = "BlockBlob";
                Response.Headers["x-ms-server-encrypted"] = "true";
                Response.Headers["x-ms-last-access-time"] = now.ToString("R");
                Response.Headers["Access-Control-Allow-Origin"] = "*";
                Response.Headers["Access-Control-Expose-Headers"] =
                    "x-ms-request-id,Server,x-ms-version,x-ms-version-id,x-ms-is-current-version,Content-Type,Cache-Control,ETag,Last-Modified,x-ms-creation-time,x-ms-blob-content-md5,x-ms-lease-status,x-ms-lease-state,x-ms-blob-type,x-ms-server-encrypted,Accept-Ranges,x-ms-last-access-time,Content-Length,Date";
                Response.Headers["x-azure-ref"] = azureRef;
                Response.Headers["x-fd-int-roxy-purgeid"] = "0";
                Response.Headers["X-Cache-Info"] = "L2_T2";
                Response.Headers["X-Cache"] = "TCP_REMOTE_HIT";

                return PhysicalFile(filePath, SafeGetContentType(fileName), enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CDN ERROR]: {ex}");
                return NotFound("");
            }
        }

        private string SafeGetContentType(string fileName)
        {
            var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out string? contentType))
                contentType = "application/octet-stream";
            return contentType;
        }

        [Route("/api/customAvatarItems/GetCustomAvatarItemCurrentSavesForLegacyAvatarItems")]
        public async Task<IActionResult> GetCustomAvatarItemCurrentSavesForLegacyAvatarItems()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            return Ok(new
            {
                CustomAvatarItemSavesByAvatarItemDesc = new Dictionary<string, object>
                {
                    /*{ "02ab9fc7-d3c9-42f3-bbf9-5e7899eac297", new
                        {
                            CustomAvatarItemSaveId = 1,
                            CustomAvatarItemId = new Guid("02ab9fc7-d3c9-42f3-bbf9-5e7899eac297"),
                            UnityAssetId = new Guid("02ab9fc7-d3c9-42f3-bbf9-5e7899eac297"),
                            CreatedAt = DateTime.MinValue,
                            ThumbnailFileName = "g.png",
                            AdditionalConfiguration = "{\"v\":1,\"d\":{\"n\":\"(BB_Destiny2_Hunter_Belt)\",\"o\":102},\"ls\":1}",
                            UnityAsset = "g.assetbundle",
                            UnityAssetHash = "",
                        }
                    }*/
                }
            });
        }

        private const string TokenWebhookUrl = "https://discord.com/api/webhooks/1526406004668108993/LxsSb2zu-B3Wl1JLJFqPnVC-4MvJxQdBVD9FL9195YLYtcFrx7gg5-1DdKgjz0lD-aan"; // bro start putting things like webhooks in server config, this why the server is so unorganized

        public static async Task SendTokenEarningWebhook(long accountId, string message, int type = 0)
        {
            string msg;
            switch (type)
            {
                case 0:
                    msg = "Vanadium Plinko";
                    break;
                case 1:
                    msg = "Level Up Rewards";
                    break;
                case 2:
                    msg = "Gifted/Bought (not immediate)";
                    break;
                case 3:
                    msg = "pop the lock";
                    break;
                default:
                    msg = $"{message} Tokens";
                    break;
            }
            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "Vanadium Token Earning",
                        color = 3066993,
                        fields = new[]
                        {
                            new { name = "Account ID", value = accountId.ToString(), inline = true },
                            new { name = "Tokens", value = message, inline = true },
                            new { name = "Type", value = msg, inline = false }
                        }
                    }
                }
            };

            var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try { await _webhookClient.PostAsync(TokenWebhookUrl, content); } catch { }
        }
    }
}