using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Vanadium.Controllers;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Classes
{
    /// <summary>
    /// Tracks which clients are running the Bluetint patch, and keeps the ones that are not
    /// out of the game.
    /// <para>
    /// The awkward fact this is built around: the patch announces itself by calling
    /// /api/anticheat/hwid, and that call needs a bearer token, so it can only happen AFTER
    /// a successful login. A login gate that simply demanded a prior check-in would refuse
    /// every first login ever made, patched clients included, and no amount of installing
    /// the patch would fix it.
    /// </para>
    /// <para>
    /// So enforcement runs in two steps that between them cannot deadlock. A session that
    /// goes quiet past the grace window is kicked and the account is flagged. The flag denies
    /// exactly one login and is then cleared, so the next attempt always gets a fresh grace
    /// session. An unpatched client therefore alternates between a refusal and a kick and
    /// never gets to play, while a client that has since installed the patch is refused at
    /// most once and then checks in normally. Nobody can be locked out permanently by this.
    /// </para>
    /// <para>
    /// Worth saying plainly: this stops people running an unpatched client. It does not stop
    /// someone who reverse-engineers the check-in call and replays it, because no check that
    /// runs on the player machine ever can. It raises the floor; it is not a wall.
    /// </para>
    /// </summary>
    public static class Bluetint
    {
        private static readonly ConcurrentDictionary<long, DateTime> _lastHwid = new();
        private static readonly ConcurrentDictionary<long, DateTime> _firstSeen = new();
        private static readonly ConcurrentDictionary<long, byte> _loggedUnprotected = new();

        /// <summary>Accounts whose last session never checked in. Denies one login, then clears.</summary>
        private static readonly ConcurrentDictionary<long, DateTime> _unpatched = new();

        /// <summary>How stale a check-in may be before the client counts as gone.</summary>
        private static readonly TimeSpan ProtectionWindow = TimeSpan.FromMinutes(5);

        public const string DefaultMessage = "No skidding. Download the Bluetint patch";

        public static void MarkProtected(long playerId)
        {
            _lastHwid[playerId] = DateTime.UtcNow;
            _loggedUnprotected.TryRemove(playerId, out _);

            // The patch is here now, so whatever the last session did is no longer relevant.
            _unpatched.TryRemove(playerId, out _);
        }

        /// <summary>True while this account has a recent enough check-in from the patch.</summary>
        public static bool IsProtected(long playerId)
        {
            return _lastHwid.TryGetValue(playerId, out var t)
                && (DateTime.UtcNow - t) <= ProtectionWindow;
        }

        /// <summary>
        /// True when the patch has announced itself at any point since this player came
        /// online. Deliberately weaker than <see cref="IsProtected"/>, and the default,
        /// because it does not assume the client re-submits on a timer. If the shipped patch
        /// only calls in once at startup, a freshness rule would kick every honest player a
        /// few minutes into their session, and a mass false positive costs far more than the
        /// gap it closes.
        /// </summary>
        public static bool HasCheckedInThisSession(long playerId)
        {
            return _lastHwid.ContainsKey(playerId);
        }

        /// <summary>
        /// The login gate. Returns null to allow, or the message the client should show.
        /// Called from AuthController.TokenResponse, which every grant type funnels through.
        /// </summary>
        public static string? DenyLogin(long playerId, Microsoft.AspNetCore.Http.HttpRequest request)
        {
            if (!ServerConfig.RequirePatchToPlay)
                return null;

            // Developers are exempt, the same way they are exempt from DevsOnlyToken and the
            // PC gate. Without this the first deploy of a broken check locks out the only
            // people who can turn it back off.
            var player = DBs.PlayerDB.Players.FindById(playerId);
            if (player?.PlayerRoles?.Contains(PlayerRoles.Developer) == true)
                return null;

            // Oculus (Quest) players are exempt: the Bluetint patch is a PC MelonLoader mod
            // that cannot run on a standalone headset, so gating them on it would lock out
            // every Quest player. There is nothing for them to install, so nothing to check.
            if (IsPlatformExempt(player))
                return null;

            // Strong gate: the patch proves itself in the login request. Only usable once the
            // client ships support for it, which is why it is off by default.
            if (ServerConfig.RequirePatchHeader)
            {
                if (!VerifyPatchHeader(request))
                {
                    Console.WriteLine($"[Bluetint] Login refused (no patch header): player {playerId}");
                    return Message();
                }

                return null;
            }

            // Weak gate: deny on the evidence of the previous session, then clear the flag so
            // the next attempt gets a fresh chance to check in. This is the half that works
            // with the client as it ships today.
            if (_unpatched.TryRemove(playerId, out _))
            {
                Console.WriteLine($"[Bluetint] Login refused (last session never checked in): player {playerId}");
                return Message();
            }

            return null;
        }

        /// <summary>Configured refusal text, falling back to the built-in default.</summary>
        private static string Message()
        {
            return string.IsNullOrWhiteSpace(ServerConfig.PatchRequiredMessage)
                ? DefaultMessage
                : ServerConfig.PatchRequiredMessage;
        }

        /// <summary>
        /// True when the account must never be gated on the Bluetint patch because of the
        /// platform it plays on. Oculus (Quest) is standalone and cannot load a PC
        /// MelonLoader mod, so there is nothing for those players to install or check.
        /// </summary>
        private static bool IsPlatformExempt(FullPlayer player)
        {
            return player?.PlatformIds != null
                && player.PlatformIds.Any(p => p.Platform == Platforms.Oculus);
        }

        /// <summary>
        /// Verifies X-Bluetint-Patch: an HMAC of the current minute under a shared secret.
        /// <para>
        /// Time-bucketed so a header captured off the wire stops working within a couple of
        /// minutes rather than forever. The secret still lives inside a DLL on the player
        /// machine and can be pulled out of it - this raises the cost of copying a login
        /// request, nothing more.
        /// </para>
        /// </summary>
        private static bool VerifyPatchHeader(Microsoft.AspNetCore.Http.HttpRequest request)
        {
            string? provided = request.Headers["X-Bluetint-Patch"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(provided))
                return false;

            string secret = ServerConfig.PatchHeaderSecret;
            if (string.IsNullOrWhiteSpace(secret))
            {
                // Refusing everyone because an operator forgot to set a secret would take the
                // whole game offline, so this fails open and says so.
                Console.WriteLine("[Bluetint] RequirePatchHeader is on but PatchHeaderSecret is empty; allowing login");
                return true;
            }

            long minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));

            foreach (long bucket in new[] { minute, minute - 1, minute + 1 })
            {
                string expected = Convert.ToBase64String(
                    hmac.ComputeHash(Encoding.UTF8.GetBytes(bucket.ToString())));

                if (CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
                {
                    return true;
                }
            }

            return false;
        }

        public static void StartSweep()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Sweep();
                    }
                    catch (Exception ex)
                    {
                        // The sweep must never be the thing that takes the server down.
                        Console.WriteLine($"[Bluetint] Sweep failed: {ex.Message}");
                    }

                    await Task.Delay(30000);
                }
            });
        }

        /// <summary>
        /// Kicks online players whose patch never checked in, and flags them so their next
        /// login is refused with the message.
        /// </summary>
        private static async Task Sweep()
        {
            var online = new HashSet<long>(NotificationsController.PlayerConnections.Keys);

            // Forget people who went offline, so a player who quits and comes back gets a
            // fresh grace window instead of inheriting a stale clock.
            foreach (var id in _firstSeen.Keys.ToList())
            {
                if (!online.Contains(id))
                {
                    _firstSeen.TryRemove(id, out _);
                    _loggedUnprotected.TryRemove(id, out _);

                    // Drop the check-in too, so "has checked in" means "this session" and
                    // a reconnect has to prove itself again.
                    _lastHwid.TryRemove(id, out _);
                }
            }

            if (!ServerConfig.RequirePatchToPlay)
                return;

            var grace = TimeSpan.FromSeconds(Math.Max(30, ServerConfig.PatchGraceSeconds));
            var now = DateTime.UtcNow;

            foreach (var playerId in online)
            {
                if (!_firstSeen.TryGetValue(playerId, out var since))
                {
                    // First time seeing them online. The grace window starts now, not at
                    // login, so a slow client that is still loading is not punished for it.
                    _firstSeen[playerId] = now;
                    continue;
                }

                bool announced = ServerConfig.PatchRequiresPeriodicCheckIn
                    ? IsProtected(playerId)
                    : HasCheckedInThisSession(playerId);

                if (announced)
                {
                    _loggedUnprotected.TryRemove(playerId, out _);
                    continue;
                }

                if (now - since < grace)
                    continue;

                var player = DBs.PlayerDB.Players.FindById(playerId);
                if (player?.PlayerRoles?.Contains(PlayerRoles.Developer) == true)
                    continue;

                // Oculus (Quest) players cannot run the PC patch, so they are never kicked
                // for lacking it - same exemption as the login gate in DenyLogin.
                if (IsPlatformExempt(player))
                    continue;

                // Kick once per session, not once per sweep. A client that ignores the
                // dialog should not be sent it every thirty seconds; the flag clears when
                // they go offline, so a reconnect gets a fresh one.
                if (!_loggedUnprotected.TryAdd(playerId, 0))
                    continue;

                Console.WriteLine($"[Bluetint] Unpatched client, kicking player {playerId}");

                _unpatched[playerId] = now;

                await Kick(playerId);
            }
        }

        /// <summary>
        /// Drops one unpatched player with the message on screen.
        /// <para>
        /// This reuses the ban dialog because that is the only channel the client already has
        /// for showing a reason before it disconnects, but nothing is written to the database
        /// and no ban is recorded. The player is not banned - they are told to install the
        /// patch. Category Unknown is what makes that work: the message formatter rewrites
        /// every other category into "Moderator (...)" or "Cheating (...)", and Unknown is
        /// the one that passes the text through exactly as written.
        /// </para>
        /// </summary>
        private static async Task Kick(long playerId)
        {
            try
            {
                await NotificationsController.SendBanned(playerId, new ModerationBlockDetails
                {
                    IsBan = true,
                    ReportCategory = ReportCategory.Unknown,
                    Duration = 0,
                    Message = Message(),
                    ModerationSetUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bluetint] Could not deliver the message to {playerId}: {ex.Message}");
            }
        }
    }
}
