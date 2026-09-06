using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Classes.WebSocket;
using Vanadium.Auth;
using Vanadium.Hubs;
using Vanadium.Utils.NotiController;
using System.Text.RegularExpressions;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    [Route("/")]
    public class AccountController : ControllerBase
    {

        public class VerifyDiscordConfirmRequest { public string? pin { get; set; } }

        [HttpPost("beta/verifydiscord/confirm")] // lowk
        public async Task<IActionResult> BetaVerifyDiscordConfirm([FromBody] VerifyDiscordConfirmRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.pin) || !Program.PendingInGameCodes.TryRemove(req.pin.Trim(), out var data))
                return BadRequest(new { success = false, error = "Invalid or expired PIN." });

            var player = PlayerDB.Players.FindById(data.AccountId);
            if (player == null)
                return BadRequest(new { success = false, error = "Account not found." });

            player.Player.DiscordUserId = data.DiscordUserId.ToString();
			player.Player.DiscordLinked = true;
			PlayerDB.Players.Update(player);

            return Ok(new { success = true });
        }
    
    	/* Fixed reputation + cheering due to progression overhaul in a way */
        
    	[HttpPost("api/PlayerCheer/v1/create")]
		public async Task<IActionResult> CreatePlayerCheer([FromForm] long PlayerIdTo, [FromForm] int CheerCategory, [FromForm] long RoomId, [FromForm] bool Anonymous)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null)
				return Unauthorized("");

			if (id.Value == PlayerIdTo)
				return Ok(new { Message = "You can not cheer yourself silly!", success = false });

			var target = PlayerDB.Players.FindById(PlayerIdTo);
			if (target == null)
				return NotFound("");

			if (Anonymous)
				await NotificationsController.SendPlayerCheerAnonymous(PlayerIdTo, id.Value, CheerCategory);
			else
				await NotificationsController.SendPlayerCheer(PlayerIdTo, id.Value, CheerCategory);
			return Ok(new { success = true });
		}

		// disabled SetSelectedCheer due to it breaking rr account birthdays!?!? idk but changing it doesnt let you spawn inventions or request to join someone
		
        /*[HttpPost("api/PlayerCheer/v1/SetSelectedCheer")]
		public async Task<IActionResult> SetSelectedCheer([FromForm(Name = "CheerCategory")] long cheerCategoryValue)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null)
				return Unauthorized("");

			var player = PlayerDB.Players.FindById(id.Value);
			if (player == null || player.Player == null)
				return NotFound("");

			var requestedCheer = (CheerCategory)cheerCategoryValue;
			bool isDeveloper = player.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false;

			if (requestedCheer == CheerCategory.RecRoomDeveloper && !isDeveloper && player.PlayerId != 216 && player.PlayerId != 936)
				return StatusCode(403);

			player.Player.Reputation ??= new Reputation();
			player.Player.Reputation.SelectedCheer = requestedCheer;
			player.Player.Reputation.IsCheerful = true;
			PlayerDB.Players.Update(player);

			var rep = player.Player.Reputation;
			var reupation = new // game doesnt wanna respect IsCheerful true so fuck it
			{
				AccountId = player.PlayerId,
				Notoriety = rep.Noteriety,
				IsCheerful = true,
				CheerGeneral = rep.CheerGeneral,
				CheerHelpful = rep.CheerHelpful,
				CheerGreatHost = rep.CheerGreatHost,
				CheerSportsman = rep.CheerSportsman,
				CheerCreative = rep.CheerCreative,
				CheerCredit = rep.CheerCredit,
				SelectedCheer = (int)rep.SelectedCheer
			};

			var json = JsonConvert.SerializeObject(new WebsocketEvents.Response { Id = NotiEventTypes.ReputationUpdate, Msg = reupation });
			await Task.WhenAll(NotificationsController.SendToAll(json), NotificationsController.SendToPlayer(id.Value, json));
			await NotificationsController.RefreshAccount(id.Value);

			return Ok(new { success = true, selectedCheer = (int)rep.SelectedCheer });
		}*/
        
        /* End of rep/cheer */
        
        [HttpGet("accounts/{playerId}/receives/GameplayInvites")]
        public IActionResult PlayerRecievesGameplayInvites(long playerId)
        {
        	var id = AuthStuff.GetPlayerId(Request);
			if (id == null)
				return Unauthorized("");
                
                return Ok(true);
        }
        
        [HttpGet("acc/account/bulk")]
        public async Task<IActionResult> GetAccountsBulk([FromQuery] List<long> id)
        {
			return BuildAccountsBulkResponse(id);
		}

		[Route("acc/account/bulk/email")]
		[Route("acc/account/bulk/phone")]
        public IActionResult GetiOSBulk() => Ok(ServerConfig.Bracket);

		[HttpPost("acc/account/bulk")]
		[Consumes("application/x-www-form-urlencoded")]
		public async Task<IActionResult> PostAccountsBulk([FromForm] List<long> id)
		{
			return BuildAccountsBulkResponse(id);
		}

		private IActionResult BuildAccountsBulkResponse(List<long> id)
		{
			if (id == null || id.Count == 0)
				return Ok(new List<PlayerDTO>());

            if (id[0] == 0)
                return Ok(new List<PlayerDTO>
                {
                    new PlayerDTO
                    {
                        accountId = 0,
                        createdAt = DateTime.UtcNow,
                        displayName = "@",
                        displayEmoji = null,
                        isJunior = false,
                        platforms = 0,
                        profileImage = "DefaultPFP.png",
                        username = "@",
                        personalPronouns = 0,
                        identityFlags = 0
                    }
                });

            return Ok(PlayerDB.GetAccountsBulk(id));
        }
        
        [HttpPut("acc/account/me/profileimage")]
		public async Task<IActionResult> PostAccountMeProfileImage([FromForm] string imageName)
		{
				var id = AuthStuff.GetPlayerId(Request);
				if (id == null)
						return Unauthorized("");

				if (string.IsNullOrWhiteSpace(imageName))
						return BadRequest("come on bro");

                if (PlayerDB.IsBanned(id.Value))
            {
                        APIController.SendNonHileWebhook(id.Value, "not a hile, but a banned user tried to change their profile image");
                        PlayerDB.UpdatePlayerHeartbeat(id.Value, null, online: false);
						return StatusCode(403, "recapi activate");
                
            }

				var player = PlayerDB.Players.FindById(id.Value);
				if (player == null)
						return NotFound("");

				player.Player.ProfileImage = imageName;
				PlayerDB.Players.Update(player);

				await NotificationsController.RefreshAccount(id.Value);

				var updated = PlayerDB.GetAccountMe(id.Value);

				return Ok(new
				{
						success = true,
				});
		}
        
        [HttpGet("api/banappeal/generateCode")]
        public IActionResult MakeBanAppealCode()
        {
				var id = AuthStuff.GetPlayerId(Request);
				if (id == null)
					return Unauthorized("");

            return Ok(new { value = "appeal at discord.gg/rec-room" });
        }
        
        [HttpPut("acc/account/me/bannerimage")]
		public async Task<IActionResult> PostAccountMeBannerImage([FromForm] string imageName)
		{
				var id = AuthStuff.GetPlayerId(Request);
				if (id == null)
						return Unauthorized("");

				if (string.IsNullOrWhiteSpace(imageName))
						return BadRequest("hahaha you are bad at this");

				var player = PlayerDB.Players.FindById(id.Value);
				if (player == null)
						return NotFound("Player not found.");

				player.Player.BannerImage = imageName;
				PlayerDB.Players.Update(player);

				await NotificationsController.RefreshAccount(id.Value);

				var updated = PlayerDB.GetAccountMe(id.Value);

				return Ok(new
				{
						success = true
				});
		}

        [HttpPut("acc/account/me/bio")]
		public async Task<IActionResult> UpdateAccountMeBio([FromForm] string bio)
		{
				var id = AuthStuff.GetPlayerId(Request);
				if (id == null)
						return Unauthorized("");

				bio = bio ?? "";

				if (!ServerConfig.AllowSwears && !string.IsNullOrWhiteSpace(bio))
				{
						var swearPath = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "swears.txt");
						if (System.IO.File.Exists(swearPath))
						{
								var swearWords = System.IO.File.ReadAllLines(swearPath);
								foreach (var word in swearWords.Select(w => w.Trim()).Where(w => !string.IsNullOrEmpty(w)))
								{
										if (Regex.IsMatch(bio, $@"(?<![a-zA-Z]){Regex.Escape(word)}(?![a-zA-Z])", RegexOptions.IgnoreCase))
												return Ok(new { error = "Bio contains prohibited text.", success = false, value = (object?)null });
								}
						}
				}

				var player = PlayerDB.Players.FindById(id.Value);
				if (player == null || player.Player == null)
						return NotFound("");

				player.Player.Bio = bio;
				PlayerDB.Players.Update(player);

				await NotificationsController.RefreshAccount(id.Value);

				return Ok(new { success = true });
		}

        [HttpGet("acc/account/{playerId}/bio")]
        public async Task<IActionResult> GetAccountBio(long playerId)
        {
            var player = PlayerDB.Players.FindById(playerId);
            if (player == null || player.Player == null)
                return NotFound("");

            return Ok(new
            {
                accountId = playerId,
                bio = player.Player.Bio ?? null
            });
        }

		[HttpPut("/acc/account/me/username")]
		public async Task<IActionResult> UpdateUsername([FromForm] string username)
		{
				var account = AuthStuff.GetPlayerId(Request);
				if (account == null)
						return StatusCode(403);

				var player = PlayerDB.Players.FindById(account.Value);
				if (player == null || player.Player == null)
						return StatusCode(404);

				if (player.Player.NameLocked)
				{
						return Ok(new
						{
								error = "Your name is locked and is unable to be changed.",
								success = false,
								value = (object?)null
						});
				}

				if (string.IsNullOrWhiteSpace(username))
				{
						return Ok(new
						{
								error = "Name contains prohibited text.",
								success = false,
								value = (object?)null
						});
				}

				username = username.Trim();

				username = Regex.Replace(username, @"[\u200B-\u200D\uFEFF]", "");

				if (!Regex.IsMatch(username, @"^[A-Za-z0-9]+(?:[._-][A-Za-z0-9]+)*$"))
				{
						return Ok(new
						{
								error = "Name contains prohibited text.",
								success = false,
								value = (object?)null
						});
				}

				if (!ServerConfig.AllowSwears)
				{
						var swearPath = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "swears.txt");

						if (System.IO.File.Exists(swearPath))
						{
								var swearWords = System.IO.File.ReadAllLines(swearPath);

								foreach (var word in swearWords.Select(w => w.Trim()).Where(w => !string.IsNullOrEmpty(w)))
								{
										string pattern = $@"\b{Regex.Escape(word)}\b";

										if (Regex.IsMatch(username, pattern, RegexOptions.IgnoreCase))
										{
												return Ok(new
												{
														error = "Name contains prohibited text.",
														success = false,
														value = (object?)null
												});
										}
								}
						}
				}

				var taken = PlayerDB.Players.FindAll()
						.Any(p => p.PlayerId != account.Value &&
											p.Player != null &&
											string.Equals(p.Player.Username, username, StringComparison.OrdinalIgnoreCase));

				if (taken)
				{
						return Ok(new
						{
								errorCode = "Accounts.UsernameAlreadyInUse",
								error = "This username is already in use.",
								success = false,
								value = (object?)null
						});
				}

				if (player.Player.LastUsernameChangeAt.HasValue &&
						(DateTime.UtcNow - player.Player.LastUsernameChangeAt.Value).TotalHours >= 2)
				{
						player.Player.AvailableUsernameChanges = 3;
						player.Player.LastUsernameChangeAt = null;
				}

				if (player.Player.AvailableUsernameChanges <= 0)
				{
					APIController.SendNonHileWebhook(account.Value, "User tried to change their username but has no available changes left. current username: " + player.Player.Username + ", attempted new username: " + username);
						return Ok(new
						{
								error = "Please wait a while before changing your username!",
								success = false,
								value = (object?)null
						});
				}

				player.Player.Username = username;
				player.Player.DisplayName = username;
				player.Player.AvailableUsernameChanges -= 1;
				player.Player.LastUsernameChangeAt ??= DateTime.UtcNow;

				PlayerDB.Players.Update(player);

				await NotificationsController.RefreshAccount(account.Value);

				return Ok(new
				{
                	success = true,
				});
		}

        [HttpPut("/acc/account/me/displayname")]
		public async Task<IActionResult> UpdateDisplayName([FromForm] string displayName)
		{
				var account = AuthStuff.GetPlayerId(Request);
				if (account == null)
						return StatusCode(403);

				var player = PlayerDB.Players.FindById(account.Value);
				if (player == null || player.Player == null)
						return StatusCode(404);

				if (player.Player.NameLocked)
						return Ok(new { error = "Your name is locked and is unable to be changed.", success = false, value = (object?)null });

				if (string.IsNullOrWhiteSpace(displayName))
						return Ok(new { error = "Name contains prohibited text.", success = false, value = (object?)null });

				displayName = displayName.Trim();
				displayName = Regex.Replace(displayName, @"[\u200B-\u200D\uFEFF]", "");

				if (!ServerConfig.AllowSwears)
				{
						var swearPath = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "swears.txt");
						if (System.IO.File.Exists(swearPath))
						{
								var swearWords = System.IO.File.ReadAllLines(swearPath);
								foreach (var word in swearWords.Select(w => w.Trim()).Where(w => !string.IsNullOrEmpty(w)))
								{
										if (Regex.IsMatch(displayName, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase))
												return Ok(new { error = "Name contains prohibited text.", success = false, value = (object?)null });
								}
						}
				}

				player.Player.DisplayName = displayName;
				PlayerDB.Players.Update(player);

				await NotificationsController.RefreshAccount(account.Value);

				return Ok(new { error = (object?)null, success = true });
		}
        
        [HttpPut("/acc/account/me/personalpronouns")]
		public async Task<IActionResult> UpdatePersonalPronouns([FromForm] int pronounFlags)
		{
				var account = AuthStuff.GetPlayerId(Request);
				if (account == null)
						return StatusCode(403);

				var player = PlayerDB.Players.FindById(account.Value);
				if (player == null || player.Player == null)
						return StatusCode(404);

				player.Player.PronounFlags = pronounFlags;

				PlayerDB.Players.Update(player);

				await NotificationsController.RefreshAccount(account.Value);

				var updated = PlayerDB.GetAccountMe(account.Value);

				return Ok(new
				{
						success = true
				});
		}

		[HttpPut("/acc/account/me/identityflags")]
		public async Task<IActionResult> UpdateIdentityFlags([FromForm] int identityFlags)
		{
				var account = AuthStuff.GetPlayerId(Request);
				if (account == null)
						return StatusCode(403);

				var player = PlayerDB.Players.FindById(account.Value);
				if (player == null || player.Player == null)
						return StatusCode(404);

				player.Player.IdentityFlags = identityFlags;

				PlayerDB.Players.Update(player);

				await NotificationsController.RefreshAccount(account.Value);

				var updated = PlayerDB.GetAccountMe(account.Value);

				return Ok(new
				{
						success = true
				});
		}

        [HttpPut("/acc/account/me/emoji")]
        public async Task<IActionResult> UpdateDisplayEmoji([FromForm] string? displayEmoji)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            var player = PlayerDB.Players.FindById(account.Value);
            if (player == null || player.Player == null)
                return StatusCode(404);

            player.Player.DisplayEmoji = string.IsNullOrWhiteSpace(displayEmoji) ? null : displayEmoji;

            PlayerDB.Players.Update(player);

            await NotificationsController.RefreshAccount(account.Value);

            return Ok(new
            {
                success = true
            });
        }

        [HttpGet("acc/account/me")]
        public async Task<IActionResult> GetAccountMe()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var player = PlayerDB.Players.FindById(id.Value);
            if (player == null)
                return NotFound("");

            var account = PlayerDB.GetAccountMe(id.Value);
            return account != null ? Ok(account) : NotFound();
        }

        [HttpPost("acc/account/me/email")]
        public async Task<IActionResult> AccAccountMeEmail()
        {
            return Ok(new
            {
                email = "vanadium@van.net",
                success = true
            });
        }

        private static long EnsurePlayerClub(FullPlayer target)
        {
            long clubId = target.Player.PlayerExtra.PlayerClubId;

            if (clubId > 0 && ClubsDB.GetClub(clubId) != null)
                return clubId;

            var club = ClubsDB.CreatePlayerClub(target.PlayerId);
            target.Player.PlayerExtra.PlayerClubId = club.ClubId;
            PlayerDB.Players.Update(target);
            return club.ClubId;
        }

        [HttpPost("subscription/{PlayerId}")]
        public async Task<IActionResult> Subscribe(ulong? PlayerId)
        {
            var user = AuthStuff.GetPlayerId(Request);
            if (user == null)
                return Unauthorized("");

            var target = PlayerDB.Players.FindById(PlayerId);
            if (target == null || target.Player?.PlayerExtra == null)
                return NotFound("");

            long clubId = EnsurePlayerClub(target);
            var club = ClubsDB.JoinClub(clubId, user.Value);

            var repUpdate = WebsocketEvents.CreateReputationUpdateResponse(target.PlayerId);
            await NotificationsController.SendToAll(JsonConvert.SerializeObject(repUpdate));

            if (club != null)
            {
                var joined = club.Members.FirstOrDefault(m => m.AccountId == user.Value);
                if (joined != null)
                {
                    await NotificationsController.BroadcastClubMembershipUpdate(joined.MemberId, user.Value, clubId, joined.MemberType, joined.JoinedAt);
                    await NotificationsController.BroadcastCreatorClubSubscriptionUpdate(target.PlayerId, clubId, joined.MemberType);
                }
            }

            return Ok(new
            {
                error = (object?)null,
                success = true,
                value = 0
            });
        }

        [HttpGet("acc/parentalcontrols/me")]
        public IActionResult ParentalControlShit()
        {
        
        		var id = AuthStuff.GetPlayerId(Request);
				if (id == null)
						return Unauthorized("");

				var player = PlayerDB.Players.FindById(id.Value);
				if (player == null)
						return NotFound("");
				
            
        	return Ok(new { accountId = player, disallowInAppPurchases = false });
        }

        [HttpGet("acc/accountprivacysettings/{playerId}")]
        public async Task<IActionResult> PrivacySettingsAcc(long playerId)
        {
            var user = AuthStuff.GetPlayerId(Request);
            if (user == null)
                return Unauthorized("");
            return Ok(new { accountId = playerId, isRecentHistoryVisible = true });
        }

        [HttpGet("/config/categories")]
        public async Task<IActionResult> ConfigCategories(long PlayerId)
        {
            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "categories.json");
            if (!System.IO.File.Exists(path)) return Ok(ServerConfig.Bracket);

            return PhysicalFile(path, "application/json");
        }

        [HttpGet("/preferences")]
        public async Task<IActionResult> Preferences(long PlayerId)
        {
            return Ok(new { mutedCategories = Array.Empty<string>() });
        }

        [HttpDelete("subscription/{PlayerId}")]
        public async Task<IActionResult> Unsubscribe(long PlayerId)
        {
            var user = AuthStuff.GetPlayerId(Request);
            if (user == null)
                return Unauthorized("");

            var target = PlayerDB.Players.FindById(PlayerId);
            if (target == null || target.Player?.PlayerExtra == null)
                return NotFound();

            long clubId = EnsurePlayerClub(target);

            var clubBefore = ClubsDB.GetClub(clubId);
            var leaving = clubBefore?.Members.FirstOrDefault(m => m.AccountId == user.Value);

            ClubsDB.LeaveClub(clubId, user.Value);

            var repUpdate = WebsocketEvents.CreateReputationUpdateResponse(target.PlayerId);
            await NotificationsController.SendToAll(JsonConvert.SerializeObject(repUpdate));

            if (leaving != null)
            {
                await NotificationsController.BroadcastClubMembershipUpdate(leaving.MemberId, user.Value, clubId, 0, DateTime.UtcNow);
                await NotificationsController.BroadcastCreatorClubSubscriptionUpdate(target.PlayerId, clubId, 0);
            }

            return Ok(new
            {
                error = (object?)null,
                success = true,
                value = 1
            });
        }

        [HttpGet("subscription/subscriberCount/{targetPlayerId}")]
        public async Task<IActionResult> GetSubscriberCount(long targetPlayerId)
        {
            var target = PlayerDB.Players.FindById(targetPlayerId);
            if (target == null || target.Player?.PlayerExtra == null)
                return NotFound("");

            long clubId = EnsurePlayerClub(target);
            int count = ClubsDB.GetSubscriberCount(clubId);

            return Content(count.ToString(), "text/plain");
        }
        
        [Route("/acc/account/me/haspassword")]
		public async Task<IActionResult> HasPassword()
		{
    		var id = AuthStuff.GetPlayerId(Request);
    		if (id == null)
        		return Unauthorized("");

    		var player = PlayerDB.Players.FindById(id.Value);
    		if (player == null)
        		return NotFound("");

    		return Ok(!string.IsNullOrEmpty(player.Password));
		}

        [HttpPost("auth/account/me/changepassword")]
        public async Task<IActionResult> ChangePassword([FromForm] string newPassword)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var player = PlayerDB.Players.FindById(id.Value);
            if (player == null)
                return Ok(new { success = false, error = "Something went wrong trying to change your password." });

            if (string.IsNullOrWhiteSpace(newPassword))
                return Ok(new { success = false, error = "Password cannot be empty." });

            var (valid, theerror) = PlayerDB.PasswordManager.ValidatePassword(newPassword);
            if (!valid)
                return Ok(new { success = false, error = theerror });

            var result = PlayerDB.PasswordManager.ChangePassword(id.Value, newPassword);
            if (!result)
                return Ok(new { success = false, error = "Something went wrong trying to change your password." });

            return Ok(new { success = true });
        }

        [HttpGet("subscription/details/{targetPlayerId}")]
        public async Task<IActionResult> GetSubscriptionDetails(long targetPlayerId)
        {
            var target = PlayerDB.Players.FindById(targetPlayerId);
            if (target == null || target.Player?.PlayerExtra == null)
                return NotFound("");

            long clubId = EnsurePlayerClub(target);
            int count = ClubsDB.GetSubscriberCount(clubId);

            return Ok(new
            {
                accountId = targetPlayerId,
                clubId = clubId,
                subscriberCount = count
            });
        }
    }
}