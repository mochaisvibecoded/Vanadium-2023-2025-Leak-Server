using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Auth
{
    public static class AuthStuff
    {
        private static readonly string Secret = /*Environment.GetEnvironmentVariable("JWT_SECRET") ?? */"asdahuiwefuhawefipoklefjauiegofwkplijhbgyuTiefhugytefui";
        private static readonly string Issuer = "ajnshuyauhj";
        private static readonly HttpClient _http = new HttpClient();


        public static string Encode(long accountId, string xrnsigKey, string ver)
        {        
            /*long accountId = accountIdOld; // nhaaahhhhh what is windows xp doing Bro
			if (accountId == 1453 || accountId == 1533 || accountId == 2250)
			{
    			accountId = 49;
			}
            if (accountId == 1457)
			{
    			accountId = 1427;
			}
            if (accountId == 1293)
			{
    			accountId = 786;
			}*/

            if (ver.Contains(".01"))
                ver = ver.Replace(".01", "");
            
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var player = PlayerDB.Players.FindById(accountId);
            var roles = new List<string> { "gameClient", "limitsv2" };

            if (player?.PlayerRoles != null)
            {
                if (player.PlayerRoles.Contains(PlayerRoles.Developer))
                    roles.Add("developer");
                if (player.PlayerRoles.Contains(PlayerRoles.Moderator))
                    roles.Add("moderator");
                if (player.PlayerRoles.Contains(PlayerRoles.Keepsake))
                    roles.Add("keepsake");
                if (player.PlayerRoles.Contains(PlayerRoles.influencer))
                    roles.Add("influencer");
                if (player.PlayerRoles.Contains(PlayerRoles.betastudio))
                    roles.Add("betastudio");
                if (player?.Player?.IsJunior == true)
                    roles.Add("junior");
            }

            var claims = new List<Claim>
            {
                new Claim("sub", accountId.ToString()),
                new Claim("pid", accountId.ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("vanadium.key", xrnsigKey),
                new Claim("ver", ver)
            };

            if (roles.Count > 0)
                claims.Add(new Claim("role", JsonSerializer.Serialize(roles), JsonClaimValueTypes.JsonArray));

            var token = new JwtSecurityToken(
                issuer: Issuer,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(12),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static string GenRefreshToken(long accountId)
        {
            var player = PlayerDB.Players.FindById(accountId);
            if (player == null)
                return "";

            var refreshToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');

            player.RefreshToken = refreshToken;
            player.RefreshTokenExpires = DateTime.UtcNow.AddHours(12);
            PlayerDB.Players.Update(player);

            return refreshToken;
        }

        public static long? ValidateRefreshToken(string refreshToken)
        {
            var player = PlayerDB.Players.FindOne(p => p.RefreshToken == refreshToken);
            if (player == null)
                return null;

            if (player.RefreshTokenExpires < DateTime.UtcNow)
                return null;

            return player.PlayerId;
        }

        public static long? GetPlayerId(HttpRequest request)
        {
            var auth = request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer "))
                return null;

            var tokenStr = auth.Substring("Bearer ".Length).Trim();

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));

                handler.ValidateToken(tokenStr, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out var validated);

                var jwt = (JwtSecurityToken)validated;

                var sub = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                var pid = jwt.Claims.FirstOrDefault(c => c.Type == "pid")?.Value;

                if (sub == null || pid == null || sub != pid)
                    return null;

                if (!long.TryParse(sub, out var accountId))
                    return null;

                var player = PlayerDB.Players.FindById(accountId);
                if (player == null)
                    return null;

                return accountId;
            }
            catch
            {
                return null;
            }
        }

        public static string? GetAndEnsureAppVersion(HttpRequest request)
        {
            var auth = request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer "))
                return null;

            var tokenStr = auth.Substring("Bearer ".Length).Trim();

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(tokenStr);
                var values = jwt.Claims.FirstOrDefault(c => c.Type == "ver")?.Value;
                var id = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                //Console.WriteLine($"[AuthStuff App Version] App version for player {id}:{values}Type: {values?.GetType()}");
                var ver = double.TryParse(values, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var validVer) ? validVer : (double?)null;
                var player = GetCurrentPlayer(request);
                if (player != null)
                {
                    player.Player.PlayerExtra.Heartbeat.appVersion = ver.HasValue ? ver.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : Classes.ServerConfig.GameVersion.ToString();
                    PlayerDB.Players.Update(player);
                }
                return values;
            }
            catch
            {
                return null;
            }
        }

        public static string? GetXRNSIG(HttpRequest request)
        {
            var auth = request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer "))
                return null;

            var tokenStr = auth.Substring("Bearer ".Length).Trim();

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(tokenStr);
                return jwt.Claims.FirstOrDefault(c => c.Type == "vanadium.key")?.Value;
            }
            catch
            {
                return null;
            }
        }

        public static FullPlayer? GetCurrentPlayer(HttpRequest request)
        {
            var id = GetPlayerId(request);
            return id.HasValue ? PlayerDB.Players.FindById(id.Value) : null;
        }

        public static FullPlayer? GetOnlinePlayer(HttpRequest request)
        {
            var id = GetPlayerId(request);
            if (!id.HasValue)
                return null;

            if (!Controllers.NotificationsController.PlayerConnections.TryGetValue(id.Value, out var connections) || connections.Count == 0)
                return null;

            return PlayerDB.Players.FindById(id.Value);
        }
    }
}