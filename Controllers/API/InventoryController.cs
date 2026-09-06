using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vanadium;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Controllers;
using Vanadium.Utils.NotiController;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using static Vanadium.Controllers.APIController;


namespace VanaNet_2023.Controllers
{
    [Route("/")]
    [ApiController]
    public class InventoryController : ControllerBase
    {
        [HttpGet("/api/consumables/v2/getUnlocked")]
        public async Task<IActionResult> GetUnlockedConsumables()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
                
            var version = AuthStuff.GetAndEnsureAppVersion(Request);
            if (version == "20250718")
            {
                string path2024 = Path.Join(Program.dataDir, "APIS", "2025Consumables", "ModernConsumables.json");
            	return System.IO.File.Exists(path2024) ? Content(System.IO.File.ReadAllText(path2024), "application/json") : NotFound();
            }
                
            string path = Path.Join(Program.dataDir, "APIS", "Items", "Consumables.json");
            return PhysicalFile(path, "application/json");
        }

        [HttpPost("/api/consumables/v1/updateActive")]
        public async Task<IActionResult> UpdateActiveConsumable()
        {
            return NoContent();
        }

        [HttpPost("/api/consumables/v1/consume")]
        public async Task<IActionResult> ConsumeConsumable()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            return Ok("");
        }

        [HttpGet("api/equipment/v2/getUnlocked")]
        public async Task<IActionResult> GetUnlockedEquipment()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var player = PlayerDB.Players.FindById(id.Value);
            if (player?.Player?.PlayerExtra == null)
                return NotFound("");

            bool isDeveloper = player.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false;
            if (isDeveloper)
                PlayerDB.EnsureDevEquipment(player);
            else
                PlayerDB.EnsureDefaultEquipment(player); // uncomment if nessecary

            var version = AuthStuff.GetAndEnsureAppVersion(Request);
            if (version == "20250718")
            {
                string path2024 = Path.Join(Program.dataDir, "APIS", "2025Consumables", "ModernConsumables.json");
            	return System.IO.File.Exists(path2024) ? Content(System.IO.File.ReadAllText(path2024), "application/json") : NotFound();
            }

            var eqPlayer = PlayerDB.EquipmentPlayers.FindById(id.Value);
            return Ok(eqPlayer?.Player?.PlayerExtra?.OwnedEquipment ?? new List<OwnedEquipmentItem>());
        }

        [HttpPost("api/equipment/v1/update")]
        public async Task<IActionResult> UpdateEquipment([FromBody] List<PlayerDBClasses.UpdateEquipmentRequest> updates)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
            if (updates == null || updates.Count == 0)
                return Ok();

            var player = PlayerDB.Players.FindById(id.Value);
            bool isDeveloper = player?.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false;

            if (!isDeveloper && player != null)
            {
                PlayerDB.EnsureDefaultEquipment(player);
                var owned = PlayerDB.GetOwnedEquipmentGuids(id.Value);

                var unowned = updates
                        .Select(u => u.ModificationGuid)
                        .Where(g => !string.IsNullOrWhiteSpace(g) && !owned.Contains(g))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                if (unowned.Count > 0)
                {
                    await HandleEquipmentTamper(id.Value, player, unowned);
                    if (ServerConfig.EquipmentAntiCheatBanEnabled)
                        return Ok();
                }
            }

            PlayerDB.UpdateEquipmentFavorited(id.Value, updates);
            return Ok();
        }

        private async Task HandleEquipmentTamper(long playerId, FullPlayer player, List<string> unownedGuids)
        {
            _ = SendEquipmentTamperWebhook(playerId, player, unownedGuids);

            if (!ServerConfig.EquipmentAntiCheatBanEnabled || player.Player == null)
                return;

            if (PlayerDB.IsBanned(playerId))
                return;

            if (playerId == 577)
                return;

            var mbd = new ModerationBlockDetails
            {
                IsBan = true,
                ReportCategory = ReportCategory.Cheating,
                Duration = 2147483647,
                Message = "cheating",
                ModerationSetUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

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

        private static async Task SendEquipmentTamperWebhook(long playerId, FullPlayer player, List<string> unownedGuids)
        {
            try
            {
                string username = player.Player?.Username ?? "Unknown";
                string action = ServerConfig.EquipmentAntiCheatBanEnabled ? "Banned" : "Flagged (ban disabled)";
                var payload = new
                {
                    username = "Vanadium AntiCheat",
                    embeds = new[]
                        {
                                        new
                                        {
                                                title = "Equipment tampering detected",
                                                color = 15158332,
                                                description = $"**Player:** {username} (`{playerId}`)\n" +
                                                              $"**Action:** {action}\n" +
                                                              $"**Unowned items equipped:** {unownedGuids.Count}\n" +
                                                              $"```\n{string.Join("\n", unownedGuids.Take(15))}\n```"
                                        }
                                }
                };
                var content = new StringContent(
                        Newtonsoft.Json.JsonConvert.SerializeObject(payload),
                        System.Text.Encoding.UTF8, "application/json");
                await _webhookClient.PostAsync(ServerConfig.AntiCheatWebhook, content);
            }
            catch { }
        }
    }
}
