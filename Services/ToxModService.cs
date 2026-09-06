using Vanadium.Classes.DBs;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Services
{
    public static class ToxModService
    {
        private static readonly long[] EscalationLadder = { 3600, 86400, 259200, 604800 }; // 1h, 24h, 3d, 7d

        public static readonly Dictionary<string, string> OffenseCategories = new()
        {
            { "SevereToxicity", "severe toxicity" },
            { "HateSpeech", "hate speech" },
            { "Harassment", "harassment" },
            { "SexualContent", "sexual content" },
            { "ViolentThreats", "violent threats" },
            { "UnderageUser", "underage user activity" },
            { "Other", "a violation of the Code of Conduct" }
        };

        public static long GetEscalationDuration(int priorStrikes)
        {
            if (priorStrikes < 0) priorStrikes = 0;
            if (priorStrikes >= EscalationLadder.Length) priorStrikes = EscalationLadder.Length - 1;
            return EscalationLadder[priorStrikes];
        }

        public static Task<ToxModOffense> IssueVoiceBan(FullPlayer player, string? category, string? note, long? durationSeconds, string? issuedBy)
        {
            player.Player ??= new Player();
            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.ModerationBlockDetails ??= new ModerationBlockDetails();
            var data = player.Player.PlayerExtra.ToxMod ??= new ToxModData();
            var mbd = player.Player.PlayerExtra.ModerationBlockDetails;

            if (string.IsNullOrEmpty(category) || !OffenseCategories.ContainsKey(category))
                category = "Other";

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long duration = durationSeconds.HasValue && durationSeconds.Value > 0
                ? durationSeconds.Value
                : GetEscalationDuration(data.Strikes);

            var offense = new ToxModOffense
            {
                UnixTime = now,
                Category = category,
                DurationSeconds = duration,
                Note = note,
                IssuedBy = issuedBy
            };

            data.Strikes++;
            data.LastOffenseUnixTime = now;
            data.ActiveBanExpiresUnixTime = now + duration;
            data.ActiveBanCategory = category;
            data.History.Add(offense);

            mbd.IsVoiceModAutoban = true;
            mbd.Duration = (int)Math.Min(duration, int.MaxValue);
            mbd.TimeoutStartedAt = DateTime.UtcNow;
            mbd.Message = $"ToxMod detected {OffenseCategories[category]} in your voice chat. " +
                          $"Your voice chat has been disabled for {FormatDuration(duration)}.";
            mbd.ModerationSetUnixTime = now;

            // Delivered through the moderationBlockDetails endpoint the client polls, not a
            // websocket push: the ModerationKick event shows an accept/deny punishment dialog,
            // while a voice ban must apply silently via SettingsModel.IsVoiceChatBanned.
            PlayerDB.Players.Update(player);
            return Task.FromResult(offense);
        }

        public static Task LiftVoiceBan(FullPlayer player, bool resetStrikes)
        {
            var extra = player.Player?.PlayerExtra;
            if (extra == null)
                return Task.CompletedTask;

            var data = extra.ToxMod;
            if (data != null)
            {
                data.ActiveBanExpiresUnixTime = 0;
                data.ActiveBanCategory = null;
                if (resetStrikes)
                    data.Strikes = 0;
            }

            var mbd = extra.ModerationBlockDetails;
            if (mbd != null && mbd.IsVoiceModAutoban == true)
            {
                mbd.IsVoiceModAutoban = false;
                mbd.Duration = 0;
                mbd.TimeoutStartedAt = null;
                mbd.Message = "";
            }

            PlayerDB.Players.Update(player);
            return Task.CompletedTask;
        }

        public static async Task<bool> EnsureCurrent(FullPlayer player)
        {
            var extra = player.Player?.PlayerExtra;
            var data = extra?.ToxMod;
            if (extra == null || data == null)
                return false;

            var mbd = extra.ModerationBlockDetails;
            bool wireBanned = mbd?.IsVoiceModAutoban == true;

            if (!wireBanned)
            {
                if (data.ActiveBanExpiresUnixTime == 0)
                    return false;
                data.ActiveBanExpiresUnixTime = 0;
                data.ActiveBanCategory = null;
                PlayerDB.Players.Update(player);
                return true;
            }

            if (data.ActiveBanExpiresUnixTime == 0 ||
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() < data.ActiveBanExpiresUnixTime)
                return false;

            await LiftVoiceBan(player, resetStrikes: false);
            return true;
        }

        public static object BuildStatus(FullPlayer player)
        {
            var extra = player.Player?.PlayerExtra;
            var data = extra?.ToxMod;
            var mbd = extra?.ModerationBlockDetails;
            bool banned = mbd?.IsVoiceModAutoban == true;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long expiresAt = data?.ActiveBanExpiresUnixTime ?? 0;

            return new
            {
                playerId = player.PlayerId,
                voiceBanned = banned,
                strikes = data?.Strikes ?? 0,
                activeBan = banned ? new
                {
                    category = data?.ActiveBanCategory,
                    expiresUnixTime = expiresAt,
                    remainingSeconds = expiresAt > now ? expiresAt - now : 0,
                    message = mbd?.Message
                } : null,
                history = data?.History ?? new List<ToxModOffense>()
            };
        }

        public static string FormatDuration(long seconds)
        {
            if (seconds >= 86400)
            {
                long days = (seconds + 86399) / 86400;
                return days == 1 ? "1 day" : $"{days} days";
            }
            if (seconds >= 3600)
            {
                long hours = (seconds + 3599) / 3600;
                return hours == 1 ? "1 hour" : $"{hours} hours";
            }
            long minutes = Math.Max(1, (seconds + 59) / 60);
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }
    }
}
