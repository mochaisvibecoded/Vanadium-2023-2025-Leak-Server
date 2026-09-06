using Microsoft.AspNetCore.Mvc;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using Vanadium.Utils.NotiController;
using System.Text.Json;

namespace Vanadium.Controllers
{
    [ApiController]
    [Route("api/playerdb")]
    public class PlayerDBManagerController : ControllerBase // DISABLED SO LOGAN AND HIS SKID TEAM DONT TRY TO NUKE THE SERVER
    {
    	/*
        [HttpGet("players")]
        public async Task<IActionResult> GetAllPlayers([FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int take = 50)
        {
            try
            {
                var allPlayers = PlayerDB.Players.FindAll().ToList();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    string searchLower = search.ToLower();
                    allPlayers = allPlayers.Where(p =>
                        p.Player?.Username?.ToLower().Contains(searchLower) == true ||
                        p.Player?.DisplayName?.ToLower().Contains(searchLower) == true ||
                        p.PlayerId.ToString().Contains(searchLower)
                    ).ToList();
                }

                var total = allPlayers.Count;
                var results = allPlayers.Skip(skip).Take(take).Select(p => new
                {
                    p.PlayerId,
                    Username = p.Player?.Username,
                    DisplayName = p.Player?.DisplayName,
                    CreatedAt = p.Player?.CreatedAt,
                    LastLoginAt = p.Player?.LastLoginAt,
                    IsJunior = p.Player?.IsJunior,
                    Level = p.Player?.Level
                }).ToList();

                return Ok(new { Results = results, Total = total });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpGet("player/{playerId}")]
        public async Task<IActionResult> GetPlayer(long playerId)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound(new { Error = "Player not found" });

                return Ok(MapPlayerToDTO(player));
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPut("player/{playerId}")]
        public async Task<IActionResult> UpdatePlayer(long playerId, [FromBody] UpdatePlayerRequest request)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound(new { Error = "Player not found" });

                if (player.Player == null)
                    player.Player = new Player();

                if (!string.IsNullOrWhiteSpace(request.Username))
                    player.Player.Username = request.Username;

                if (!string.IsNullOrWhiteSpace(request.DisplayName))
                    player.Player.DisplayName = request.DisplayName;

                if (!string.IsNullOrWhiteSpace(request.Bio))
                    player.Player.Bio = request.Bio;

                if (!string.IsNullOrWhiteSpace(request.ProfileImage))
                    player.Player.ProfileImage = request.ProfileImage;

                if (request.IsJunior.HasValue)
                    player.Player.IsJunior = request.IsJunior.Value;

                if (request.AvailableUsernameChanges.HasValue)
                    player.Player.AvailableUsernameChanges = request.AvailableUsernameChanges.Value;

                if (request.Level.HasValue)
                    player.Player.Level = request.Level.Value;

                if (request.XP.HasValue)
                    player.Player.XP = request.XP.Value;

                if (request.CreatedAt.HasValue)
                    player.Player.CreatedAt = request.CreatedAt.Value;

                if (request.LastLoginAt.HasValue)
                    player.Player.LastLoginAt = request.LastLoginAt.Value;

                if (request.Birthday.HasValue)
                    player.Player.Birthday = request.Birthday.Value;

                if (!string.IsNullOrWhiteSpace(request.Email))
                    player.Player.Email = request.Email;

                if (!string.IsNullOrWhiteSpace(request.Password))
                    player.Password = request.Password;

                PlayerDB.Players.Update(player);

                var updatedAccount = PlayerDB.GetAccountMe(playerId);
                if (updatedAccount != null)
                {
                    await NotiController.SendAccountUpdate(playerId, updatedAccount);
                    await NotiController.SendEvent(playerId, NotiController.EventTypes.SelfAccountUpdate, updatedAccount);
                }

                return Ok(new { Message = "Player updated successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPut("player/{playerId}/reputation")]
        public async Task<IActionResult> UpdatePlayerReputation(long playerId, [FromBody] UpdateReputationRequest request)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound(new { Error = "Player not found" });

                if (player.Player?.Reputation == null)
                    player.Player.Reputation = new Reputation();

                var rep = player.Player.Reputation;
                rep.AccountId = playerId;

                if (request.CheerGeneral.HasValue)
                    rep.CheerGeneral = request.CheerGeneral.Value;
                if (request.CheerHelpful.HasValue)
                    rep.CheerHelpful = request.CheerHelpful.Value;
                if (request.CheerCreative.HasValue)
                    rep.CheerCreative = request.CheerCreative.Value;
                if (request.CheerGreatHost.HasValue)
                    rep.CheerGreatHost = request.CheerGreatHost.Value;
                if (request.CheerSportsman.HasValue)
                    rep.CheerSportsman = request.CheerSportsman.Value;
                if (request.CheerCredit.HasValue)
                    rep.CheerCredit = request.CheerCredit.Value;
                if (request.Notoriety.HasValue)
                    rep.Noteriety = request.Notoriety.Value;
                if (request.SubscriberCount.HasValue)
                    rep.SubscriberCount = request.SubscriberCount.Value;
                if (request.IsCheerful.HasValue)
                    rep.IsCheerful = request.IsCheerful.Value;

                PlayerDB.Players.Update(player);

                await NotiController.SendReputationUpdate(playerId, player.Player.Reputation);
                var friends = PlayerDB.Players.FindAll()
                    .Where(p => p.Player?.Relationships != null &&
                                p.Player.Relationships.Any(r => r.PlayerId == playerId && r.RelationshipType == 5))
                    .Select(p => p.PlayerId)
                    .ToList();
                foreach (var fid in friends)
                    await NotiController.SendReputationUpdate(fid, new { AccountId = playerId, Reputation = player.Player.Reputation });

                return Ok(new { Message = "Reputation updated successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }
        [HttpGet("player/{playerId}/settings")]
        public async Task<IActionResult> GetPlayerSettings(long playerId)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound(new { Error = "Player not found" });

                var settings = player.Player?.PlayerExtra?.Settings ?? new List<Setting>();
                return Ok(settings);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPost("player/{playerId}/settings")]
        public async Task<IActionResult> AddPlayerSetting(long playerId, [FromBody] SettingRequest request)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound(new { Error = "Player not found" });

                if (string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Value))
                    return BadRequest(new { Error = "Key and Value are required" });

                PlayerDB.SetPlayerSetting(request.Key, request.Value, playerId);

                return Ok(new { Message = "Setting added/updated successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpDelete("player/{playerId}/settings/{settingKey}")]
        public async Task<IActionResult> DeletePlayerSetting(long playerId, string settingKey)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound(new { Error = "Player not found" });

                if (player.Player?.PlayerExtra?.Settings != null)
                {
                    var setting = player.Player.PlayerExtra.Settings.FirstOrDefault(s => s.Key == settingKey);
                    if (setting != null)
                    {
                        player.Player.PlayerExtra.Settings.Remove(setting);
                        PlayerDB.Players.Update(player);
                    }
                }

                return Ok(new { Message = "Setting deleted successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPost("player/{playerId}/platforms")]
        public async Task<IActionResult> AddPlayerPlatform(long playerId, [FromBody] AddPlatformRequest request)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound(new { Error = "Player not found" });

                if (player.PlatformIds == null)
                    player.PlatformIds = new List<mPlatformID>();

                var existingPlatform = player.PlatformIds.FirstOrDefault(p => p.Platform == request.Platform);
                if (existingPlatform != null)
                {
                    existingPlatform.PlatformId = request.PlatformId;
                }
                else
                {
                    player.PlatformIds.Add(new mPlatformID
                    {
                        Platform = request.Platform,
                        PlatformId = request.PlatformId
                    });
                }

                PlayerDB.Players.Update(player);
                return Ok(new { Message = "Platform added successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpDelete("player/{playerId}/platforms/{platform}")]
        public async Task<IActionResult> RemovePlayerPlatform(long playerId, int platform)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound(new { Error = "Player not found" });

                if (player.PlatformIds != null)
                {
                    var platformToRemove = player.PlatformIds.FirstOrDefault(p => (int)p.Platform == platform);
                    if (platformToRemove != null)
                    {
                        player.PlatformIds.Remove(platformToRemove);
                        PlayerDB.Players.Update(player);
                    }
                }

                return Ok(new { Message = "Platform removed successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPut("player/{playerId}/roles")]
        public async Task<IActionResult> UpdatePlayerRoles(long playerId, [FromBody] UpdateRolesRequest request)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound(new { Error = "Player not found" });

                player.PlayerRoles = new List<PlayerRoles>();

                if (request.IsScreenshare)
                    player.PlayerRoles.Add(PlayerRoles.Screenshare);
                if (request.IsModerator)
                    player.PlayerRoles.Add(PlayerRoles.Moderator);
                if (request.IsDeveloper)
                    player.PlayerRoles.Add(PlayerRoles.Developer);

                PlayerDB.Players.Update(player);

                var updatedAccount = PlayerDB.GetAccountMe(playerId);
                if (updatedAccount != null)
                    await NotiController.SendAccountUpdate(playerId, updatedAccount);

                return Ok(new { Message = "Roles updated successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPost("player/{playerId}/authtoken")]
        public async Task<IActionResult> GenerateNewAuthToken(long playerId)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                if (player == null)
                    return NotFound(new { Error = "Player not found" });

                player.AuthToken = Guid.NewGuid().ToString();
                PlayerDB.Players.Update(player);

                return Ok(new { AuthToken = player.AuthToken, Message = "New auth token generated" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpDelete("player/{playerId}")]
        public async Task<IActionResult> DeletePlayer(long playerId)
        {
            try
            {
                var success = PlayerDB.Players.Delete(playerId);
                if (!success)
                    return NotFound(new { Error = "Player not found" });

                return Ok(new { Message = "Player deleted successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }
        
        [HttpGet("test")]
        public async Task<IActionResult> test()
        {
            try
                    {
                        string filePath = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "communityboard.json");

                        string jsonData = System.IO.File.ReadAllText(filePath);
                        var boardData = JsonSerializer.Deserialize<object>(jsonData);

                        var cbPayload = new {
                        	Id = "CommunityBoardUpdate",
                            Msg = boardData
                        };

                        NotificationsController.SendToAll(JsonSerializer.Serialize(cbPayload));

                        return Ok("Community board reloaded successfully.");
                    }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        private object MapPlayerToDTO(FullPlayer player)
        {
            var p = player.Player ?? new Player();
            var rep = p.Reputation ?? new Reputation();

            return new
            {
                player.PlayerId,
                player.AuthToken,
                player.Password,
                player.PlayerRoles,
                player.PlatformIds,
                Player = new
                {
                    p.Username,
                    p.DisplayName,
                    p.Bio,
                    p.AvailableUsernameChanges,
                    p.IsJunior,
                    p.Level,
                    p.XP,
                    p.ProfileImage,
                    p.BannerImage,
                    p.Email,
                    p.CreatedAt,
                    p.LastLoginAt,
                    p.Birthday,
                    Reputation = new
                    {
                        rep.CheerGeneral,
                        rep.CheerHelpful,
                        rep.CheerCreative,
                        rep.CheerGreatHost,
                        rep.CheerSportsman,
                        rep.CheerCredit,
                        rep.Noteriety,
                        rep.SubscriberCount,
                        rep.IsCheerful
                    },
                    Settings = p.PlayerExtra?.Settings ?? new List<Setting>()
                }
            };
        }

        public class UpdatePlayerRequest
        {
            public string? Username { get; set; }
            public string? DisplayName { get; set; }
            public string? Bio { get; set; }
            public string? ProfileImage { get; set; }
            public string? Email { get; set; }
            public string? Password { get; set; }
            public bool? IsJunior { get; set; }
            public int? AvailableUsernameChanges { get; set; }
            public int? Level { get; set; }
            public int? XP { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? LastLoginAt { get; set; }
            public DateTime? Birthday { get; set; }
        }

        public class UpdateReputationRequest
        {
            public int? CheerGeneral { get; set; }
            public int? CheerHelpful { get; set; }
            public int? CheerCreative { get; set; }
            public int? CheerGreatHost { get; set; }
            public int? CheerSportsman { get; set; }
            public int? CheerCredit { get; set; }
            public double? Notoriety { get; set; }
            public int? SubscriberCount { get; set; }
            public bool? IsCheerful { get; set; }
        }

        public class SettingRequest
        {
            public required string Key { get; set; }
            public required string Value { get; set; }
        }

        public class AddPlatformRequest
        {
            public Platforms Platform { get; set; }
            public ulong PlatformId { get; set; }
        }

        public class UpdateRolesRequest
        {
            public bool IsScreenshare { get; set; }
            public bool IsModerator { get; set; }
            public bool IsDeveloper { get; set; }
        }
        */
    }
}
