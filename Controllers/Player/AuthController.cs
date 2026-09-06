using Microsoft.AspNetCore.Mvc;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Auth;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;

namespace Vanadium.Controllers
{
    [ApiController]
    [Route("/auth")]
    public class AuthController : ControllerBase
    {
        private static readonly HttpClient _webhookClient = new HttpClient();
        private const string WebhookUrl = "https://discord.com/api/webhooks/1515954214885654538/-OjeNp_0GYNuO-H7ztAU37_TtxxSfqI_cVuv9p-Lk-VzVw_OkD9Ptomfe3eSgZwxryMv";

        private static async Task SendCreatedAccountWebhook(long accountId, DateTime createdAt, string platformId, Platforms platform)
        {
            var payload = new
            {
                username = "Vanadium",
                embeds = new[]
                {
                    new
                    {
                        title = "Vanadium New Account",
                        color = 3066993,
                        fields = new[]
                        {
                            new { name = "Account ID", value = accountId.ToString(), inline = true },
                            new { name = "Platform", value = platform.ToString(), inline = true },
                            new { name = "Platform ID", value = platformId, inline = true },
                            new { name = "Created At", value = createdAt.ToString("u"), inline = false }
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try { await _webhookClient.PostAsync(WebhookUrl, content); } catch { }
        }

        private static async Task SendSteamBlockedWebhook(Platforms platform, string platformId)
        {
            var payload = new
            {
                username = "Vanadium",
                embeds = new[]
                {
                    new
                    {
                        title = "Vanadium New Account Failed (steam id blocked)",
                        color = 15158332,
                        fields = new[]
                        {
                            new { name = "Platform", value = platform.ToString(), inline = true },
                            new { name = "Platform ID", value = platformId, inline = true },
                            new { name = "Created At", value = DateTime.UtcNow.ToString("u"), inline = false }
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try { await _webhookClient.PostAsync(WebhookUrl, content); } catch { }
        }

        private static async Task SendOculusBlockedWebhook(Platforms platform, string platformId)
        {
            var payload = new
            {
                username = "Vanadium",
                embeds = new[]
                {
                    new
                    {
                        title = "Vanadium Login Failed (oculus nonce rejected)",
                        color = 15158332,
                        fields = new[]
                        {
                            new { name = "Platform", value = platform.ToString(), inline = true },
                            new { name = "Platform ID", value = platformId, inline = true },
                            new { name = "Timestamp", value = DateTime.UtcNow.ToString("u"), inline = false },
                            new { name = "Info", value = "Meta did not confirm the nonce was issued to this user id", inline = false }
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try { await _webhookClient.PostAsync(WebhookUrl, content); } catch { }
        }

        private static async Task SendTokenLoginWebhook(long accountId, string grantType, object requestData)
        {
            var player = PlayerDB.Players.FindById(accountId);
            bool isDeveloper = player?.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false;
            bool isRootAccount = accountId == 1;

            if (!isDeveloper && !isRootAccount)
                return;

            var requestJson = JsonSerializer.Serialize(requestData, new JsonSerializerOptions { WriteIndented = true });

            var payload = new
            {
                username = "Vanadium",
                embeds = new[]
                {
                    new
                    {
                        title = $"Player {accountId} logged in",
                        color = 5814783,
                        fields = new[]
                        {
                            new { name = "Grant Type", value = grantType, inline = true },
                            new { name = "Account ID", value = accountId.ToString(), inline = true },
                            new { name = "Is Developer", value = isDeveloper.ToString(), inline = true },
                            new { name = "Request Body", value = $"```json\n{requestJson}\n```", inline = false }
                        },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try { await _webhookClient.PostAsync(WebhookUrl, content); } catch { }
        }

        private static async Task SendSpoofAttemptWebhook(long claimedAccountId, Platforms platform, string platformId)
        {
            var payload = new
            {
                username = "Vanadium",
                embeds = new[]
                {
                    new
                    {
                        title = "Unfamiliar login hash",
                        color = 15158332,
                        fields = new[]
                        {
                            new { name = "Claimed Account ID", value = claimedAccountId.ToString(), inline = true },
                            new { name = "Platform", value = platform.ToString(), inline = true },
                            new { name = "Platform ID", value = platformId, inline = true },
                            new { name = "Timestamp", value = DateTime.UtcNow.ToString("u"), inline = false },
                            new { name = "Info", value = "Someone either tried to spoof or logged in after playing another revival", inline = false }
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try { await _webhookClient.PostAsync(WebhookUrl, content); } catch { }
        }

        private static async Task SendSkidWebhook(Platforms platform, ulong platformId)
        {
            var payload = new
            {
                username = "Vanadium",
                embeds = new[]
                {
                    new
                    {
                        title = "Blacklisted Platform Id Tryna Skid - cached login api GET",
                        color = 15158332,
                        fields = new[]
                        {
                            new { name = "Platform", value = platform.ToString(), inline = true },
                            new { name = "Platform ID", value = platformId.ToString(), inline = true },
                            new { name = "Timestamp", value = DateTime.UtcNow.ToString("u"), inline = false }
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try { await _webhookClient.PostAsync(WebhookUrl, content); } catch { }
        }

        internal async Task SendE12354Webhook(
            string? request,
            string? ip,
            string? headersPretty)
        {

            var embed = new Dictionary<string, object?>
            {
                ["title"] = $"Gotchu bru",
                ["color"] = 3066993,
                ["fields"] = new object[]
                {
                    new { name = "Headers", value = headersPretty ?? Request.Headers.ToString(), inline = false },
                    new { name = "IP", value = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown", inline = false },
                    new { name = "Request Body", value = request ?? "No request provided", inline = true }
                },
            };

            var payload = new
            {
                embeds = new[] { embed }
            };

            try
            {
                var content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(payload),
                    Encoding.UTF8, "application/json");
                await APIController._webhookClient.PostAsync(
                    "https://discord.com/api/webhooks/1544973415109234738/jlsJiqhHxQ145xDjhl5c68D614mKgzJCIdGTuPwvsFmdicxR26ujube-bR2hgbQrNjKq",
                    content);
            }
            catch { }
        }
        
        [HttpGet("oculus/nonce")]
		public IActionResult QuestSupportFor2025() 
		{
    		var random = new Random();
    		const string randomstuffatp = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    		var randomString = new string(Enumerable.Repeat(randomstuffatp, 64).Select(s => s[random.Next(s.Length)]).ToArray());
   		 	return Ok($"\"{randomString}\"");
		}
        
        [HttpGet("cachedlogin/forplatformids")]
        public IActionResult GetPlayersForPlatformIds([FromQuery] List<string> id)
        {
            if (id == null || id.Count == 0)
                return Ok(Array.Empty<object>());

            var results = PlayerDB.Players.FindAll()
                .Where(p => p.PlatformIds != null &&
                            p.PlatformIds.Any(pid => id.Contains(pid.PlatformId, StringComparer.OrdinalIgnoreCase)))
                .ToList();

            var dtos = results.Select(p => PlayerDB.GetAccountsBulk(new List<long> { p.PlayerId }).FirstOrDefault())
                .Where(p => p != null)
                .ToList();

            return Ok(dtos);
        }

        [HttpGet("eac/challenge")]
        public async Task<IActionResult> C_hallenge()
        {
            return Ok("\"AQAAAHsg7mW5FQEE9HVl9EKMWXrqDzQxUCdgV/IPuQfbRgTx+cGnQqhhAgv1RvpihEC77gQ29JdoGFn2806Q+QPEj7nYg9C8pynbaiSVO8rKLJPvROsHuSXVJpQMv3TD8KyK3Y+n5bb86vAb5kRdZGD//uC8HY+D9jJLlEfTUlU=\"");
        }

        [HttpGet("cachedlogin/forplatformid/{platform}/{platformId}")]
        [HttpPost("cachedlogin/forplatformid/{platform}/{platformId}")]
        public async Task<IActionResult> GetCachedLogins(Platforms platform, string platformId)
        {
        	/*if (ServerConfig.BlacklistedPlatformIds.Contains(platformId))
                {
                	Console.WriteLine($"[AUTH] AutoAcc skid detected platform={platform} platformId={platformId} in blacklisted platform ids list");
                    _ = SendSkidWebhook(platform, platformId);
                	return Ok("");
                }*/
                
            /*if (platform == Platforms.Steam && !ServerConfig.IsSteamIdWhitelisted(platformId))
            {
                return Ok(new
                {
                    error = "Accounts.UserIsBlacklisted",
                    message_message = "Blacklisted from using Vanadium.",
                    success = false
                });
            }*/

            if (PlayerDB.GetLogins(platform, platformId, out var accounts) && accounts.Count > 0)
                return Ok(accounts);

            if (ServerConfig.AutoAccount)
            {   
            
                /*if (PlayerDB.IsSteamPlayerInSupportedGame(platformId).Result == false)
                {
                    Console.WriteLine($"[AUTH] AutoAcc player not playing Rec Room or Spacewar platform={platform} platformId={platformId} not in supported game");
                    _ = SendSteamBlockedWebhook(platform, platformId);
                    return Ok("");
                }
                var cachedLogin = new List<CachedLogins>
                {
                    new CachedLogins
                    {
                        accountId = 0,
                        lastLoginTime = DateTime.UtcNow,
                        platform = platform,
                        platformId = null
                    }
                };
                return Ok(cachedLogin);*/
                Console.WriteLine($"[AUTH] AutoAcc no accounts for platform={platform} platformId={platformId} so we creating one");
                var newPlayer = PlayerDB.CreateAccount(platform, platformId, false);
                _ = SendCreatedAccountWebhook(newPlayer.PlayerId, newPlayer.Player.CreatedAt, platformId, platform);
                Console.WriteLine($"[AUTH] AutoAcc created account with id={newPlayer.PlayerId}");
				
                var cachedLogin = new List<CachedLogins>
                {
                    new CachedLogins
                    {
                        accountId = newPlayer.PlayerId,
                        lastLoginTime = DateTime.UtcNow,
                        platform = platform,
                        platformId = platformId.ToString()
                    }
                };
                return Ok(cachedLogin);
            }

            return Unauthorized("");
        }

        [HttpGet("/auth/role/developer/{playerId}")]
        public async Task<IActionResult> IsDeveloper(long playerId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var target = PlayerDB.Players.FindById(playerId);
            if (target == null)
                return NotFound("");

            return Ok(target.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false);
        }

        [HttpGet("/auth/role/moderator/{playerId}")]
        public async Task<IActionResult> IsModerator(long playerId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var target = PlayerDB.Players.FindById(playerId);
            if (target == null)
                return NotFound("");

            return Ok(target.PlayerRoles?.Contains(PlayerRoles.Moderator) ?? false);
        }

        [HttpGet("/auth/role/keepsake/{playerId}")]
        public async Task<IActionResult> HasScreenSharing(long playerId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var target = PlayerDB.Players.FindById(playerId);
            if (target == null)
                return Ok(false);

            return Ok(target.PlayerRoles?.Contains(PlayerRoles.Keepsake) ?? false);
        }

        [HttpGet("/auth/role/developer")]
        public async Task<IActionResult> IsDeveloper2025([FromQuery] long id)
        {
            var requesterId = AuthStuff.GetPlayerId(Request);
            if (requesterId == null)
                return Unauthorized("");

            var target = PlayerDB.Players.FindById(id);
            if (target == null)
                return NotFound("");

            return Ok(target.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false);
        }

        [HttpGet("/auth/role/moderator")]
        public async Task<IActionResult> IsModerator2025([FromQuery] long id)
        {
            var requesterId = AuthStuff.GetPlayerId(Request);
            if (requesterId == null)
                return Unauthorized("");

            var target = PlayerDB.Players.FindById(id);
            if (target == null)
                return NotFound("");

            return Ok(target.PlayerRoles?.Contains(PlayerRoles.Moderator) ?? false);
        }

        [HttpGet("/auth/role/keepsake")]
        public async Task<IActionResult> HasScreenSharing2025([FromQuery] long id)
        {
            var requesterId = AuthStuff.GetPlayerId(Request);
            if (requesterId == null)
                return Unauthorized("");

            var target = PlayerDB.Players.FindById(id);
            if (target == null)
                return Ok(false);

            return Ok(target.PlayerRoles?.Contains(PlayerRoles.Keepsake) ?? false);
        }

        [HttpPost("auth/cachedlogin/forplatformids")]
        public async Task<IActionResult> PostCachedLoginForPlatformIds([FromForm] List<ulong> id)
        {
            var accounts = new List<CachedLogins>();
            foreach (var platformId in id)
            {
                if (PlayerDB.GetLogins(Platforms.Steam, platformId.ToString(), out var foundAccounts))
                    accounts.AddRange(foundAccounts);
            }
            return Ok(accounts.OrderByDescending(a => a.lastLoginTime).ToList());
        }

        private static void PreWarmDorm(long accountId)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var player = PlayerDB.Players.FindById(accountId);
                    if (player?.Player == null)
                        return;

                    long dormRoomId = player.Player.PlayerExtra.DormRoomId;

                    if (dormRoomId <= 0)
                    {
                        var dormRoom = RoomDB.GetPlayerDormRoom(accountId);
                        if (dormRoom == null)
                        {
                            dormRoomId = RoomDB.CloneDormRoom(accountId);
                            player.Player.PlayerExtra.DormRoomId = dormRoomId;
                            PlayerDB.UpdateDormRoomId(accountId, dormRoomId);
                        }
                        else
                        {
                            dormRoomId = dormRoom.RoomId;
                            PlayerDB.UpdateDormRoomId(accountId, dormRoomId);
                        }
                    }

                    Sessions.CreateDorm(accountId, player.Player.Username, dormRoomId);
                }
                catch
                {
                }
            });
        }

        private static async Task SendModernVersionWebhook(long accountId, double version)
        {
            var payload = new
            {
                username = "Vanadium",
                embeds = new[]
                {
                    new
                    {
                        title = "User logged in on modern version!",
                        color = 0,
                        fields = new[]
                        {
                            new { name = "Info", value = $"`{accountId}` logged in on version `{version.ToString(System.Globalization.CultureInfo.InvariantCulture)}`", inline = false }
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try { await _webhookClient.PostAsync(ServerConfig.AntiCheatWebhook, content); } catch { }
        }

        private static readonly string TokenScope = "offline_access rn"; // i don't think i need this

        private async Task<IActionResult?> Check2025ShitAsync(long playerId, string? ver)
        {
            if (!Build2025Validator.Is2025Build(ver)) return null;
            var (allowed, errorKey, errorDesc) = await Build2025Validator.ValidateAsync(playerId);
            if (allowed) return null;
            return Unauthorized(new { error = errorKey, error_description = errorDesc });
        }

        private IActionResult TokenResponse(long accountId, string grantType, object requestData, DeviceClasses? deviceClass = null, double? ver = null)
        {
            // The patch gate. Every grant type funnels through here, so this is the one place
            // it has to be checked - and the refusal is shaped like the other login denials
            // above so the client shows the message instead of a generic failure.
            string? patchDenial = Bluetint.DenyLogin(accountId, Request);

            if (patchDenial != null)
            {
                return Ok(new
                {
                    error = "invalid_grant",
                    error_description = patchDenial,
                    access_token = (string?)null,
                    key = (string?)null,
                    refresh_token = (string?)null
                });
            }

            var refreshToken = AuthStuff.GenRefreshToken(accountId);

            PlayerDB.StoreLoginDeviceInfo(accountId, deviceClass, (int?)ver);

            if (grantType != "refresh_token")
                PreWarmDorm(accountId);

            _ = SendTokenLoginWebhook(accountId, grantType, requestData);

            if (ver.HasValue && ver.Value > 20230616)
                _ = SendModernVersionWebhook(accountId, ver.Value);

            string xrnsigKey = RNSIGHandler.IssuePlayerKey(accountId);

            return Ok(new
            {
                access_token = AuthStuff.Encode(accountId, xrnsigKey, ver?.ToString()),
                error = "",
                error_description = "",
                refresh_token = refreshToken,
                scope = TokenScope,
                key = xrnsigKey
            });
        }

        [HttpPost("connect/token")]
		public async Task<IActionResult> ConnectToken([FromForm] string grant_type, [FromForm] long? account_id, [FromForm] string? client_id, [FromForm] string? client_secret, [FromForm] string? username, [FromForm] string? password, [FromForm] Platforms? platform, [FromForm] string? platform_id, [FromForm] string? device_id, [FromForm] DeviceClasses? device_class, [FromForm] DateTime? time, [FromForm] string? ver, [FromForm] string? build_key, [FromForm] string? asid, [FromForm] string? eac_challenge, [FromForm] string? eac_response, [FromForm] string? platform_auth, [FromForm] string? refresh_token)

{
    double validVer;

    if (!double.TryParse(ver, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out validVer))
    {
        validVer = 20250724;
    }

    var requestData = new
    {
        grant_type,
        account_id,
        client_id,
        platform = platform?.ToString(),
        platform_id,
        device_id,
        device_class = device_class?.ToString(),
        time,
        ver = validVer,
        build_key,
        asid,
        eac_challenge,
        eac_response
    };
    
    if (time == null || build_key == null)
            {
                SendE12354Webhook(JsonSerializer.Serialize(requestData), HttpContext.Connection.RemoteIpAddress?.ToString(), ImageController.GetHeadersPretty(Request));
                return StatusCode(500);
            }
        
        	// DevsOnlyToken uh this meaning uh only people that is devvings role can play :P  
        
			if (ServerConfig.DevsOnlyToken)
			{
    			if (account_id == null || !(PlayerDB.Players.FindById(account_id.Value)?.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false))
        			return Ok(new { error = "invalid_grant", error_description = "", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });
			}
            
            // dont let skids on or something blah blah
            
            // READ THIS !!!!! BITCH
            
            // after talking to "No Turning Back" (also known as: e12354, larpboi69, or sonimcrine) if we turn on pc source gets leaked (no we aint going open source)
            
            // if (platform == Platforms.Steam && ServerConfig.LetPCPlay == false) // if (platform != Platforms.Oculus && ServerConfig.LetPCPlay == false)
			if (platform != Platforms.Oculus) // if (platform != Platforms.Oculus && ServerConfig.LetPCPlay == false)
			{
    			if (account_id == null || !(PlayerDB.Players.FindById(account_id.Value)?.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false))
                {
                    if (!(ServerConfig.PCMaximumIdForPlay > 0 && account_id.HasValue && account_id.Value > ServerConfig.PCMaximumIdForPlay))
                        goto now_login;
                    return Ok(new { error = "invalid_grant", error_description = "", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });
                }
        			
			}
            
            // end of checks thgbfs

    now_login:
    switch (grant_type)
    {
        case "cached_login":
        {
            if (platform == null || string.IsNullOrWhiteSpace(platform_id))
            {
                return BadRequest(new { error = "missing platform info" });
            }

            if (platform == Platforms.Steam)
            {
                if (await PlayerDB.ValidateSteamUserTicketAsync(platform_auth, ulong.Parse(platform_id)) == false)
                {
                    _ = SendSteamBlockedWebhook(platform.Value, platform_id);
                    return Ok(new { error = "invalid_grant", error_description = "platform verification failed", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });
                }
            }

            if (platform == Platforms.Oculus)
            {
                if (await PlayerDB.ValidateOculusUserNonceAsync(platform_auth, platform_id) == false)
                {
                    _ = SendOculusBlockedWebhook(platform.Value, platform_id);
                    return Ok(new { error = "invalid_grant", error_description = "platform verification failed", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });
                }
            }

            PlayerDB.GetLogins(platform.Value, platform_id, out var cachedLogins);
            var platformAccountIds = cachedLogins.Select(c => c.accountId).ToHashSet();

            if (platformAccountIds.Count == 0)
            {
                if (!ServerConfig.AutoAccount)
                {
                    return Ok(new { error = "invalid_grant", error_description = "an error occured", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });
                }

                var newPlayer = PlayerDB.CreateAccount(platform.Value, platform_id, false);
                /*var deny0 = await Check2025ShitAsync(newPlayer.PlayerId, ver);
                if (deny0 != null) return deny0;*/
                return TokenResponse(newPlayer.PlayerId, grant_type, requestData, device_class, validVer);
            }

            var platformAccounts = PlayerDB.Players.FindAll()
                .Where(p => platformAccountIds.Contains(p.PlayerId))
                .ToList();

            if (platformAccounts.Count == 0)
            {
                return Ok(new { error = "invalid_grant", error_description = "an error occured", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });
            }

            FullPlayer targetPlayer;

            if (account_id != null)
            {
                targetPlayer = platformAccounts.FirstOrDefault(p => p.PlayerId == account_id.Value);

                if (targetPlayer == null)
                {
                    Console.WriteLine($"[AUTH] SPOOF ATTEMPT: account_id={account_id} not owned by platform={platform} platform_id={platform_id}");
                    _ = SendSpoofAttemptWebhook(account_id.Value, platform.Value, platform_id);
                    return Ok(new { error = "invalid_grant", error_description = "an error occured", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });
                }
            }
            else
            {
                targetPlayer = platformAccounts.OrderByDescending(p => p.Player.LastLoginAt).First();
            }

            Console.WriteLine($"[AUTH] cached_login accepted for account_id={targetPlayer.PlayerId}");
            /*var deny1 = await Check2025ShitAsync(targetPlayer.PlayerId, ver);
            if (deny1 != null) return deny1;*/ 
            return TokenResponse(targetPlayer.PlayerId, grant_type, requestData, device_class, validVer);
        }

        case "create_account":
        {
            if (!ServerConfig.CA_Auth)
                return Ok("hello goodbye stop pleasseeeee im tired of skids");
            if (platform == null || string.IsNullOrWhiteSpace(platform_id))
                return Ok(new { error = "invalid_grant", error_description = "invalid platform", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });

            if (platform == Platforms.Oculus)
            {
                if (await PlayerDB.ValidateOculusUserNonceAsync(platform_auth, platform_id) == false)
                {
                    Console.WriteLine($"[AUTH] create_account rejected: oculus nonce not valid for platformId={platform_id}");
                    _ = SendOculusBlockedWebhook(platform.Value, platform_id);
                    return Ok(new { error = "invalid_grant", error_description = "platform verification failed", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });
                }
            }

            PlayerDB.GetLogins(platform.Value, platform_id, out var existingLogins);
            var existingAccountIds = existingLogins.Select(c => c.accountId).ToList();
            var existingAccount = PlayerDB.Players.FindAll()
                .FirstOrDefault(p => existingAccountIds.Contains(p.PlayerId));

            if (existingAccount != null)
            {
                /*var deny2 = await Check2025ShitAsync(existingAccount.PlayerId, ver);
                if (deny2 != null) return deny2;*/
                return TokenResponse(existingAccount.PlayerId, grant_type, requestData, device_class, validVer);
            }

            if (platform == Platforms.Steam)
            {
                if (await PlayerDB.ValidateSteamUserTicketAsync(platform_auth, ulong.Parse(platform_id)) == false)
                {
                    Console.WriteLine($"[AUTH] AutoAcc player ticket not valid for Rec Room or Spacewar platform={platform} platformId={platform_id}");
                    _ = SendSteamBlockedWebhook(platform.Value, platform_id);
                    return Ok(new { error = "invalid_grant", error_description = "invalid platform", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });
                }
            }

            var newPlayer = PlayerDB.CreateAccount(platform.Value, platform_id, false);

            _ = SendCreatedAccountWebhook(newPlayer.PlayerId, newPlayer.Player.CreatedAt, platform_id, platform.Value);

            /*var deny3 = await Check2025ShitAsync(newPlayer.PlayerId, ver);
            if (deny3 != null) return deny3;*/
            return TokenResponse(newPlayer.PlayerId, grant_type, requestData, device_class, validVer);
        }

        case "refresh_token":
        {
            if (string.IsNullOrWhiteSpace(refresh_token))
                return BadRequest(new { error = "invalid_request", error_description = "refresh_token is required." });

            var player = PlayerDB.Players.FindOne(p => p.RefreshToken == refresh_token);

            if (player == null || player.RefreshTokenExpires < DateTime.UtcNow)
            {
                Console.WriteLine($"[AUTH] refresh_token REJECTED for token='{refresh_token}'");
                return Unauthorized(new { error = "invalid_grant", error_description = "Refresh token is invalid or expired" });
            }

            player.RefreshToken = null;
            player.RefreshTokenExpires = DateTime.MinValue;
            PlayerDB.Players.Update(player);

            Console.WriteLine($"[AUTH] refresh_token ACCEPTED for playerId={player.PlayerId}");
            return TokenResponse(player.PlayerId, grant_type, requestData);
        }

        case "password":
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return Ok(new { error = "invalid_grant", error_description = "invalid_username_or_password", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });

            var player = PlayerDB.Players.FindAll()
                .FirstOrDefault(p => string.Equals(p.Player?.Username, username, StringComparison.OrdinalIgnoreCase));

            if (player == null || string.IsNullOrEmpty(player.Password))
                return Ok(new { error = "invalid_grant", error_description = "invalid_username_or_password", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });

            if (!PlayerDB.PasswordManager.VerifyPassword(password, player.Password))
                return Ok(new { error = "invalid_grant", error_description = "invalid_username_or_password", access_token = (string?)null, key = (string?)null, refresh_token = (string?)null });

            if (platform != null && !string.IsNullOrWhiteSpace(platform_id))
            {
                player.PlatformIds ??= new List<mPlatformID>();

                bool alreadyLinked = player.PlatformIds.Any(p =>
                    p.Platform == platform.Value &&
                    string.Equals(p.PlatformId, platform_id, StringComparison.OrdinalIgnoreCase));

                if (!alreadyLinked)
                {
                    player.PlatformIds.Add(new mPlatformID { Platform = platform.Value, PlatformId = platform_id });
                    PlayerDB.Players.Update(player);
                    Console.WriteLine($"[AUTH] password: linked platform={platform} platformId={platform_id} to playerId={player.PlayerId}");
                }
            }

            Console.WriteLine($"[AUTH] password grant accepted for playerId={player.PlayerId}");
            /*var denyPw = await Check2025ShitAsync(player.PlayerId, ver);
            if (denyPw != null) return denyPw;*/
            return TokenResponse(player.PlayerId, grant_type, requestData, device_class, validVer);
        }

        default:
            Console.WriteLine($"[AUTH] unsupported grant_type='{grant_type}'");
            return BadRequest(new { error = "unsupported_grant_type" });
    }
}
    }
}
