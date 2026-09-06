using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration.UserSecrets;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Classes.Rooms;
using Vanadium.Utils;
using System.Text;
using System.Text.Json;
using static Vanadium.Classes.DBs.DBClasses.EventDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
namespace Vanadium.Controllers
{
    [Route("/")]
    public class ModernController : ControllerBase
    {
        public class OutfitBulkRequest
        {
            public List<long>? AccountIds { get; set; }
            public string? UnityAssetTarget { get; set; }
            public string? UnityAssetVersion { get; set; }
        }
        
        // [16:38:04] [96.2.32.187] GET: https://reloxa.xyz/crm/me/config/v2 404
        // [16:38:05] [96.2.32.187] POST: https://reloxa.xyz/statsigUserProperties 404
        // [16:38:06] [96.2.32.187] POST: https://reloxa.xyz/api/avatar/v1/lockeditems/bulk 404
        
        [HttpGet("crm/me/config/v2")]
        public IActionResult crmconfig()
        {
        	return Ok(ServerConfig.Bracket);
        }
        
        [HttpPost("statsigUserProperties")]
        public IActionResult statsig()
        {
        	return Ok(new { Success = true, value = new { splitTests = (object?)null, shouldSampleGameserver = false } });
        }
        
        [HttpPost("api/avatar/v1/lockeditems/bulk")]
        public IActionResult lockeditems()
        {
        	return Ok(ServerConfig.Bracket);
        }

        [HttpGet("api/versioncheck/islandedversions")]
        public async Task<IActionResult> GetIslandedVersions()
        {
            return Ok(ServerConfig.Bracket);
        }

        [Route("api/PlayerReporting/v1/referee")] // ok so theres options, get, post, and patch used by game for some reason
        public async Task<IActionResult> PlayerReportingReferee()
        {
            return Ok(false);
        }
        
        [HttpPost("purchase/v1/cleanuppending")] // should be in storefront controller, but this controller is specifically for modern 2025/2026.
        public async Task<IActionResult> cleanuppendingstorefront()
        {
            return Ok(new { success = true, error = (string?)null, error_id = (string?)null, value = (string?)null });
        }
        
        [HttpGet("crm/me/config/v3")]
        public async Task<IActionResult> ConfigCRM()
        {
            return Ok(ServerConfig.Bracket);
        }
        
        [HttpGet("gameai/user/access")]
        [HttpGet("makerai/user/access")]
        [Route("identifyampltiude")]
        public async Task<IActionResult> identifyamplitudeIdkWhatthisis() // idk what this is bro
        {
            return Ok(new { success = true, value = (object?)null, error = (object?)null, error_id = (object?)null });
        }
        
        [Route("api/players/v1/playerPhotoTaggingSetting")]
        public async Task<IActionResult> PlayerPhotoTaggingSettings()
        {
            return Ok(new { success = true, gay = "i love femboy thighs"});
        }
        
        [HttpGet("/sections/bulk")]
        public IActionResult GetSectionsBulkShit([FromQuery] List<string> id)
        {
            return Ok();
        }

        [HttpGet("outfits/me")]
        public async Task<IActionResult> GetMyAvatarV2(long? playerId = null)
        {
            if (playerId.HasValue)
            {
                var player = PlayerDB.Players.FindById(playerId.Value);
                if (player == null)
                    return NotFound();

                return Ok(player.Player?.PlayerExtra.Avatar);
            }

            var currentPlayer = AuthStuff.GetCurrentPlayer(Request);
            if (currentPlayer == null)
                return Unauthorized("");

            Avatar? avatar = currentPlayer.Player?.PlayerExtra?.Avatar;

            return Ok(new
            {
                LegacyData = new
                {
                    SelectionsV1 = avatar?.OutfitSelections,
                    SelectionsV2 = avatar?.OutfitSelectionsV2,
                    FaceFeatures = avatar?.FaceFeatures,
                    SkinColor = avatar?.SkinColor,
                    HairColor = avatar?.HairColor
                },
                Selections = avatar?.CustomAvatarItems?.Select(i => new
                {
                    i.BodyPart,
                    i.CustomAvatarItemId,
                    BakedUnityAssetFileName = (string?)null,
                    AdditionalConfiguration = (object?)null
                }).ToArray() ?? Array.Empty<object>(),
                DataVersion = avatar?.DataVersion ?? 10,
                CustomizationSettings = avatar?.CustomizationSettings,
                ThumbnailFileName = (string?)null,
                Name = (string?)null,
                Accessibility = 0,
                Slot = 0
            });
        }
        
        [HttpPut("outfits/me")] // 2024 - will (might actually) break avatar if on 2025 tho
        public async Task<IActionResult> PutOutfitMe([FromBody] SavedOutfitRequest request)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized("");
                
            var version = AuthStuff.GetAndEnsureAppVersion(Request);
            if (version == "20250724") return BadRequest("");

            if (request == null)
                return BadRequest("");

            PlayerDB.SaveOutfit(player.PlayerId, request);

            return Ok(new { success = true });
        }

        [HttpGet("api/referee/files")]
        public async Task<IActionResult> RefereeFiles()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("sampling")]
        public async Task<IActionResult> Sampling()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "EventTypes.json");
            if (!System.IO.File.Exists(path)) return Ok(Array.Empty<object>());
            return PhysicalFile(path, "application/json");
        }

        [HttpGet("api/consumables/v1/all")]
        public async Task<IActionResult> AllConsumables()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "2025Consumables", "ModernUnlockableConsumables.json");
            if (!System.IO.File.Exists(path)) return Ok(Array.Empty<object>());
            return PhysicalFile(path, "application/json");
        }

        [HttpGet("cdn/config/Prod/AvatarEffectConfig_v1.json")]
        public async Task<IActionResult> AvatarEffectConfigV1()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "AvatarEffectConfigV1.json");
            if (!System.IO.File.Exists(path)) return Ok(ServerConfig.Bracket);

            return PhysicalFile(path, "application/json");
        }
        
        [HttpGet("aimoderation/roomie/enabledForPlayer")]
        public async Task<IActionResult> IsRoomieEnabled()
        {
            return Ok(true);
        }
        
        [HttpPost("realtime-session/create")]
        public async Task<IActionResult> CreateSessionForRoomieOrMakerAI()
        {
            var currentPlayer = AuthStuff.GetCurrentPlayer(Request);
            if (currentPlayer == null)
                return Unauthorized("");

            /*return Ok(new
            {
                success = true,
                error = "",
                error_id = "",
                value = new
                {
                    ClientSecret = "",
                    SessionId = "sess_"
                }
            });*/
        	// dmcaed
            await Task.Delay(Timeout.Infinite);
            return null;
            // parent of c5d30f8 (Update Controllers/ModernController.cs)
        }
        
        [HttpGet("roomieai/user/facts")]
        public async Task<IActionResult> RoomieUserFacts()
        {
        
            var currentPlayer = AuthStuff.GetCurrentPlayer(Request);
            if (currentPlayer == null)
                return Unauthorized("");
        
            return Ok(); // dmcaed
        }

        [HttpGet("roomieai/user/access")]
        public async Task<IActionResult> GetUserRoomieAccess()
        {
            return Ok(new
            {
    value = new
    {
        MaxEnergyFromSubscriptions = 8000,
        EnergyLeft = 4793,
        NextSubscriptionEnergyRechargeAt = (string?)null,
        OutputAudioEnabled = true
    },
    success = true,
    error_id = (string?)null,
    error = (string?)null
});
        }

        [HttpGet("gameai/room/{roomId}/spendsummary")]
        public async Task<IActionResult> GetUserRoomSpendSummary([FromRoute] long roomId)
        {
            return Ok(new
            {
                value = (object?)null,
                success = false,
                error_id = "AI.ManagementAccessDenied",
                error = "You don't have permission to manage AI in this room."
            });
        }

        [HttpGet("makerai/user/balances")]
        public async Task<IActionResult> GetMakerAiSpend()
        {
            return Ok(new
            {
                UsageDollars = 0,
                UsersMaxUsageDollars = 0,
                RRPlusUsageDollars = 0.0,
                UsersMaxRRPlusUsageDollars = 20,
                TimeBalanceStatus = "Empty",
                TimeExpiresAt = "0001-01-01T00:00:00",
                UsageBalanceStatus = "Good",
                UsagePercent = 0,
                RRPlusUsageBalanceStatus = "Good",
                RRPlusUsagePercent = 0
            });
        }
        
        // voice shit does not rlly matter
        [HttpGet("voice/config")]
        [HttpGet("api/config/v1/azurespeech")]
        public async Task<IActionResult> VoiceCrappingsFor2023and2025()
        {
            return Ok(new { voiceConnectionId = ":3c", success = true, regions = ServerConfig.Bracket });
        }

        [HttpPost("outfits/bulk")]
        public async Task<IActionResult> GetAvatarBulkV2([FromBody] OutfitBulkRequest? request)
        {
            var currentPlayer = AuthStuff.GetCurrentPlayer(Request);
            if (currentPlayer == null)
                return Unauthorized("");

            var accountIds = request?.AccountIds;
            if (accountIds == null || accountIds.Count == 0)
                return Ok(new { OutfitsByAccountId = new Dictionary<string, object>() });

            var avatars = new Dictionary<string, object>();
            foreach (var accountId in accountIds)
            {
                var player = PlayerDB.Players.FindById(accountId);
                if (player == null)
                    continue;

                Avatar? avatar = player.Player?.PlayerExtra?.Avatar;
                if (avatar == null)
                    continue;

                avatars[accountId.ToString()] = new {
                    LegacyData = new
                    {
                        SelectionsV1 = avatar.OutfitSelections,
                        SelectionsV2 = avatar.OutfitSelectionsV2,
                        avatar.FaceFeatures,
                        avatar.SkinColor,
                        avatar.HairColor
                    },
                    Selections = Array.Empty<object>(),
                    DataVersion = 9,
                    CustomizationSettings = (string?)null,
                    ThumbnailFileName = (string?)null,
                    Name = (string?)null,
                    Accessibility = 0,
                    Slot = 0
                };
            }

            return Ok(new { OutfitsByAccountId = avatars });
        }

        [HttpGet("purchasecampaign/allcurrent/v2")]
        public async Task<IActionResult> GetPurchaseCampaign()
        {
            string path = Path.Join(Program.dataDir, "APIS", "PurchaseCampaign.json");
            return System.IO.File.Exists(path) ? Content(System.IO.File.ReadAllText(path), "application/json") : NotFound();
        }

        [HttpGet("match/tachyon")]
        public async Task<IActionResult> MatchTachyon([FromQuery] long accountId)
        {
            var player = PlayerDB.Players.FindById(accountId);
            if (player == null)
                return Ok(0);
            
            return Ok(player.Player?.PlayerExtra?.Heartbeat.roomInstance.roomInstanceId ?? 0);
        }
    }
}