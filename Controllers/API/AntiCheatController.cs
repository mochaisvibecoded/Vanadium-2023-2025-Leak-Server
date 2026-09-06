using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Vanadium.Controllers
{
    [ApiController]
    public class AntiCheatController : ControllerBase
    {
        private static readonly HttpClient _http = new HttpClient();
        private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(10);
        private const string EvasionMessage = "cheating";

        /// <summary>Shown to an account blocked because the machine it is on is banned.</summary>
        private const string HardwareBanMessage = "cheating";

        public class ReportBody
        {
            public string Type { get; set; }
            public string Reason { get; set; }
            public string ModVersion { get; set; }
            public ulong SteamId { get; set; }
            public long? RoomId { get; set; }
        }

        [HttpPost("/api/anticheat/report")]
        public async Task<IActionResult> Report([FromBody] ReportBody body)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");

            if (body == null || string.IsNullOrWhiteSpace(body.Type) || string.IsNullOrWhiteSpace(body.Reason))
                return BadRequest(new { error = "Missing type/reason." });

            string type = body.Type.Trim().ToLower();
            if (type != "cheat" && type != "injection")
                type = "other";

            string reason = body.Reason.Length > 500 ? body.Reason.Substring(0, 500) : body.Reason;
            string username = player.Player?.Username ?? "Unknown";
            /*ulong steamId = player.PlatformIds?.FirstOrDefault()?.PlatformId ?? body.SteamId;

            var detection = AntiCheatDB.Record(player.PlayerId, username, steamId, type, reason,
                body.ModVersion, body.RoomId, DedupWindow, out bool isNew);

            if (isNew)
                await RelayToDiscord(detection);*/

            return Ok(new { success = true });
        }

        public class HwidBody
        {
            public string Hwid { get; set; }
        }

        [HttpPost("/api/anticheat/hwid")]
        public async Task<IActionResult> SubmitHwid([FromBody] HwidBody body)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            string hwid = body?.Hwid?.Trim();
            if (string.IsNullOrEmpty(hwid) || hwid.Length < 16 || hwid.Length > 128)
                return BadRequest(new { error = "Invalid hwid." });

            Bluetint.MarkProtected(playerId.Value);

            if (PlayerDB.IsBanned(playerId.Value))
                return Ok(new { banned = true });

            var bannedSiblings = HwidDB.LinkAndGetBannedSiblings(hwid, playerId.Value);

            // A hardware ban outranks the evasion check below: the machine itself is
            // blocked, so it does not matter whether any account on it is banned yet.
            if (HwidDB.IsHwidBanned(hwid))
            {
                PlayerDB.ApplyBan(playerId.Value, HardwareBanMessage);

                var blocked = PlayerDB.GetCurrentPlayer(playerId.Value);
                var blockedDetails = blocked?.Player?.PlayerExtra?.ModerationBlockDetails;
                if (blockedDetails != null)
                    await NotificationsController.SendBanned(playerId.Value, blockedDetails);

                Console.WriteLine($"[AntiCheat] Hardware ban hit: player {playerId.Value} on banned hwid");
                return Ok(new { banned = true, message = HardwareBanMessage });
            }

            if (bannedSiblings.Count > 0)
            {
                PlayerDB.ApplyBan(playerId.Value, EvasionMessage);

                var player = PlayerDB.GetCurrentPlayer(playerId.Value);
                var mbd = player?.Player?.PlayerExtra?.ModerationBlockDetails;
                if (mbd != null)
                    await NotificationsController.SendBanned(playerId.Value, mbd);

                Console.WriteLine($"[AntiCheat] HWID evasion: player {playerId.Value} re-banned (banned siblings: {string.Join(",", bannedSiblings)})");
                return Ok(new { banned = true, message = EvasionMessage });
            }

            return Ok(new { banned = false });
        }

        public class HwidBanBody
        {
            /// <summary>The machine to ban. Takes precedence over PlayerId when both are set.</summary>
            public string Hwid { get; set; }

            /// <summary>Ban every machine this account has been seen on.</summary>
            public long? PlayerId { get; set; }

            public string Reason { get; set; }

            /// <summary>Omit or set to null for a permanent ban.</summary>
            public int? DurationDays { get; set; }
        }

        /// <summary>
        /// Bans a machine, and with it the accounts linked to that machine.
        /// <para>
        /// Moderator-gated rather than key-gated because this is the heaviest tool in the
        /// moderation set and every use of it needs a name attached. A shared machine puts
        /// unrelated people behind one hwid, so the response reports exactly which accounts
        /// were caught - the moderator finds out now, not from an appeal in a week.
        /// </para>
        /// </summary>
        [HttpPost("/api/anticheat/hwid/ban")]
        public IActionResult BanHwid([FromBody] HwidBanBody body)
        {
            var moderator = RequireModerator();
            if (moderator == null)
                return Unauthorized("");

            if (body == null || string.IsNullOrWhiteSpace(body.Reason))
                return BadRequest(new { error = "A reason is required." });

            if (body.DurationDays is <= 0)
                return BadRequest(new { error = "DurationDays must be positive, or null for permanent." });

            DateTime? expiresAt = body.DurationDays == null
                ? null
                : DateTime.UtcNow.AddDays(body.DurationDays.Value);

            string issuedBy = $"{moderator.Player?.Username ?? "moderator"} ({moderator.PlayerId})";

            if (!string.IsNullOrWhiteSpace(body.Hwid))
            {
                var bannedAccounts = HwidDB.BanHwid(body.Hwid.Trim(), body.Reason, issuedBy, expiresAt);

                if (bannedAccounts == null)
                    return NotFound(new { error = "That machine has never checked in." });

                return Ok(new
                {
                    success = true,
                    hwids = new[] { body.Hwid.Trim() },
                    bannedAccounts,
                    expiresAt
                });
            }

            if (body.PlayerId == null)
                return BadRequest(new { error = "Provide either Hwid or PlayerId." });

            var (hwids, accounts) = HwidDB.BanMachinesOfPlayer(body.PlayerId.Value, body.Reason, issuedBy, expiresAt);

            if (hwids.Count == 0)
                return NotFound(new { error = "No machines on record for that player." });

            return Ok(new { success = true, hwids, bannedAccounts = accounts, expiresAt });
        }

        public class HwidUnbanBody
        {
            public string Hwid { get; set; }
        }

        /// <summary>
        /// Lifts a hardware ban. The accounts banned alongside it stay banned: some of them
        /// were banned on their own behaviour first, and reinstating those here would undo
        /// moderation nobody asked to undo.
        /// </summary>
        [HttpPost("/api/anticheat/hwid/unban")]
        public IActionResult UnbanHwid([FromBody] HwidUnbanBody body)
        {
            var moderator = RequireModerator();
            if (moderator == null)
                return Unauthorized("");

            if (string.IsNullOrWhiteSpace(body?.Hwid))
                return BadRequest(new { error = "Hwid is required." });

            string liftedBy = $"{moderator.Player?.Username ?? "moderator"} ({moderator.PlayerId})";

            if (!HwidDB.UnbanHwid(body.Hwid.Trim(), liftedBy))
                return NotFound(new { error = "No active hardware ban on that machine." });

            return Ok(new { success = true });
        }

        /// <summary>
        /// What a moderator looks at before deciding. Accepts either a machine or an account
        /// and returns the same shape, because the question is the same either way: who else
        /// is behind this.
        /// </summary>
        [HttpGet("/api/anticheat/hwid/lookup")]
        public IActionResult LookupHwid([FromQuery] string hwid, [FromQuery] long? playerId)
        {
            var moderator = RequireModerator();
            if (moderator == null)
                return Unauthorized("");

            var records = new List<HwidDBClasses.HwidRecord>();

            if (!string.IsNullOrWhiteSpace(hwid))
            {
                var single = HwidDB.GetRecord(hwid.Trim());
                if (single != null)
                    records.Add(single);
            }
            else if (playerId != null)
            {
                records.AddRange(HwidDB.RecordsForPlayer(playerId.Value));
            }
            else
            {
                return BadRequest(new { error = "Provide either hwid or playerId." });
            }

            var now = DateTime.UtcNow;

            return Ok(records.Select(r => new
            {
                hwid = r.Hwid,
                players = r.PlayerIds.Select(id => new
                {
                    playerId = id,
                    username = PlayerDB.GetCurrentPlayer(id)?.Player?.Username,
                    banned = PlayerDB.IsBanned(id)
                }),
                firstSeen = r.FirstSeen,
                lastSeen = r.LastSeen,
                banned = r.BanIsActive(now),
                banReason = r.BanReason,
                bannedBy = r.BannedBy,
                bannedAt = r.BannedAt,
                banExpiresAt = r.BanExpiresAt
            }));
        }

        /// <summary>
        /// The gate on every endpoint above. Returns null unless the caller is a moderator or
        /// a developer - a hardware ban is not something an ordinary authenticated client
        /// gets to issue against anyone.
        /// </summary>
        private FullPlayer RequireModerator()
        {
            var requester = AuthStuff.GetCurrentPlayer(Request);
            if (requester?.PlayerRoles == null)
                return null;

            bool allowed = requester.PlayerRoles.Contains(PlayerRoles.Moderator)
                || requester.PlayerRoles.Contains(PlayerRoles.Developer);

            return allowed ? requester : null;
        }
        
        public class CertificateBody
        {
            public string Key { get; set; }
        }

        [HttpPost("/bluetint/certificate/validate")]
        public IActionResult ValidateCertificate([FromBody] CertificateBody body)
        {
            string key = body?.Key;
            if (string.IsNullOrWhiteSpace(key))
                return Unauthorized("");

            try
            {
                string certPath = Path.Join(Environment.CurrentDirectory, ".BluetintConfig", "Certificate", "cert.pem");
                if (!System.IO.File.Exists(certPath))
                    return Unauthorized("");

                string pemText = System.IO.File.ReadAllText(certPath);
                using var privateRsa = System.Security.Cryptography.RSA.Create();
                privateRsa.ImportFromPem(pemText.AsSpan());

                using var submittedRsa = System.Security.Cryptography.RSA.Create();

                string cleaned = key.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "+");
                byte[] submittedKeyBytes = Convert.FromBase64String(cleaned);
                submittedRsa.ImportSubjectPublicKeyInfo(submittedKeyBytes, out _);

                byte[] expectedPublicKey = privateRsa.ExportSubjectPublicKeyInfo();
                byte[] submittedPublicKey = submittedRsa.ExportSubjectPublicKeyInfo();

                if (!expectedPublicKey.SequenceEqual(submittedPublicKey))
                    return Unauthorized(new { error = "Key mismatch", expected = Convert.ToBase64String(expectedPublicKey), submitted = Convert.ToBase64String(submittedPublicKey) });

                return Ok();
            }
            catch (Exception ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
        }

        [HttpGet("/bluetint/config/{config?}")]
        public IActionResult Rules(string? config)
        {
                if (string.IsNullOrEmpty(config))
                        return NotFound("");
                        
                string path = Path.Join(Environment.CurrentDirectory, ".BluetintConfig", $"{config}.json");

                if (System.IO.File.Exists(path))
                        return Content(System.IO.File.ReadAllText(path), "application/json");
                else
                        return NotFound("");
        }
        
        private static async Task RelayToDiscord(AntiCheatDBClasses.Detection d)
        {
            try
            {
                string title = d.Type == "cheat" ? "🚨 Cheater detected" : "💉 DLL injection detected";
                int color = d.Type == "cheat" ? 0xE74C3C : 0xE67E22;

                var payload = new
                {
                    username = "Bluetint Anti-Cheat",
                    embeds = new[]
                    {
                        new
                        {
                            title,
                            color,
                            fields = new object[]
                            {
                                new { name = "Reason", value = string.IsNullOrEmpty(d.Reason) ? "n/a" : d.Reason, inline = false },
                                new { name = "Player", value = $"{d.Username} ({d.PlayerId})", inline = true },
                                new { name = "Steam ID", value = d.SteamId.ToString(), inline = true },
                                new { name = "Room", value = d.RoomId?.ToString() ?? "n/a", inline = true },
                                new { name = "Mod", value = string.IsNullOrEmpty(d.ModVersion) ? "n/a" : d.ModVersion, inline = true }
                            },
                            timestamp = d.DetectedAt.ToString("o")
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                await _http.PostAsync(ServerConfig.AntiCheatWebhook, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AntiCheat] Discord relay failed: {ex.Message}");
            }
        }
    }
}
