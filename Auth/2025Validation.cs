using System.Net.Http.Headers;
using System.Text.Json;
using Vanadium.Classes;
using Vanadium.Classes.DBs;

namespace Vanadium.Auth
{
    public static class Build2025Validator
    {
        private const string GuildId = "1034585922186002432";
        //private const string boosterRoleId = "1511181684438204457";

        private static readonly HttpClient _http = new();

        public static bool Is2025Build(string? ver)
        {
            if (string.IsNullOrWhiteSpace(ver)) return false;
            string v = ver.Trim();
            return v == "20250724" || v == "20250724.01";
        }

        public static async Task<(bool allowed, string? errorKey, string? errorDesc)> ValidateAsync(long playerId)
        {
            var player = PlayerDB.Players.FindById(playerId); // i dont care how cluttered ts is bro, it works
            Console.WriteLine($"Validating 2025 build access for player {playerId} ({player?.Player?.DiscordUserId})");
            if (player == null)
                return (false, "invalid_client", "No discord account linked.");
            Console.WriteLine($"Player found: {player.PlayerId}");

            Console.WriteLine($"PlayerRoles: {(player.PlayerRoles == null ? "null" : $"[{string.Join(", ", player.PlayerRoles)}]")}");
            if (player.PlayerRoles?.Contains(Classes.DBs.DBClasses.PlayerDBClasses.PlayerRoles.Developer) ?? false)
                return (true, null, null);
            Console.WriteLine($"Player is not a developer, checking discord access... Roles: {string.Join(", ", player.PlayerRoles ?? new List<Classes.DBs.DBClasses.PlayerDBClasses.PlayerRoles>())}");
            if (Controllers.WebsiteController.ReadRRPlusIds().Contains(playerId))
                return (true, null, null);

            string? discordUserId = player.Player?.DiscordUserId;
            if (string.IsNullOrWhiteSpace(discordUserId))
                return (false, "invalid_client", "No discord account linked.");

            bool linked = player.Player?.DiscordLinked ?? false;
            if (!linked)
                return (false, "invalid_client", "No discord account linked.");

            string token = ServerConfig.CountBotToken;
            if (string.IsNullOrWhiteSpace(token))
                return (false, "invalid_client", "No access to play 2025 build.");

            bool inGuild = await IsUserInGuildAsync(token, discordUserId);
            if (!inGuild)
                return (false, "invalid_client", "No access to play 2025 build.");

            bool hasAccess = await UserHasAccessAsync(token, discordUserId);
            if (!hasAccess)
                return (false, "invalid_client", "No access to play 2025 build.");

            return (true, null, null);
        }

        private static async Task<bool> IsUserInGuildAsync(string botToken, string userId)
        {
            try
            {
                using var req = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://discord.com/api/v10/guilds/{GuildId}/members/{userId}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);

                var resp = await _http.SendAsync(req);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> UserHasAccessAsync(string botToken, string userId)
        {
            try
            {
                using var req = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://discord.com/api/v10/guilds/{GuildId}/members/{userId}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);

                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return false;

                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("premium_since", out var premiumSince) && premiumSince.ValueKind != JsonValueKind.Null) // is null when not a booster
    			return true;

				return false;
                
            }
            catch
            {
                return false;
            }
        }
    }
}