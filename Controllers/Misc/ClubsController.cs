using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Vanadium.Auth;
using Vanadium.Classes.DBs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Vanadium.Classes.DBs.DBClasses.ClubsDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    public class ClubsController : ControllerBase
    {
        private static int ParseVisibility(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return -1;
            return raw.ToLowerInvariant() switch
            {
                "public" => (int)ClubVisibility.Public,
                "unlisted" => (int)ClubVisibility.Unlisted,
                "private" => (int)ClubVisibility.Private,
                "0" => (int)ClubVisibility.Private,
                "1" => (int)ClubVisibility.Public,
                "2" => (int)ClubVisibility.Unlisted,
                _ => -1
            };
        }

        private static int ParseJoinability(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return -1;
            return raw.ToLowerInvariant() switch
            {
                "open" => (int)ClubJoinability.Open,
                "approvalrequired" => (int)ClubJoinability.ApprovalRequired,
                "closed" => (int)ClubJoinability.Closed,
                "0" => (int)ClubJoinability.Open,
                "1" => (int)ClubJoinability.ApprovalRequired,
                "2" => (int)ClubJoinability.Closed,
                _ => -1
            };
        }
        
        [HttpGet("/club/{clubId}/members/{userId}")]
		public IActionResult GetClubMember(long clubId, long userId)
		{
		    var id = AuthStuff.GetPlayerId(Request);
		    if (id == null) return Unauthorized("");
		    var member = ClubsDB.GetClubMembers(clubId).FirstOrDefault(m => m.AccountId == userId);
		    if (member == null) return Ok(new {});
		    return Ok(member);
		}

        [HttpGet("/club/search")]
        public IActionResult SearchClubs(
            [FromQuery] string? query,
            [FromQuery] string? category,
            [FromQuery] int sort = 0,
            [FromQuery] int count = 30,
            [FromQuery] int skip = 0)
        {
            count = Math.Clamp(count, 1, 100);
            var result = ClubsDB.Search(query, category, sort, count, skip);
            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(result),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("/club/create")]
        public IActionResult CreateClub(
            [FromForm] string name,
            [FromForm] string? description,
            [FromForm] string? category)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var club = ClubsDB.CreateClub(
                id.Value,
                name.Trim(),
                description?.Trim() ?? "",
                (category ?? "Social").Trim());

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new
                {
                    success = true,
                    error = "",
                    value = ClubsDB.ToDetails(club, id.Value)
                }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("/club/categoryTags")]
        public IActionResult GetClubCategoryTags()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var tags = new List<string>
            {
                "Social",
                "Creative",
                "Competitive",
                "Casual",
                "Educational",
                "Entertainment",
                "Lifestyle"
            };

            return Ok(tags);
        }

        [HttpGet("/club/mine/created")]
        public IActionResult GetMyCreatedClubs()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var clubs = ClubsDB.GetClubsCreatedBy(id.Value)
                .Select(ClubsDB.ToSummary)
                .ToList();

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(clubs),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("/club/mine/member")]
        public IActionResult GetMyMemberClubs()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var clubs = ClubsDB.GetClubsJoinedBy(id.Value)
                .Where(c => c.Visibility != 2)
                .Select(ClubsDB.ToSummary)
                .ToList();

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(clubs),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("/club/account/{player}/created")]
        public IActionResult GetOthersCreatedClubs(long player)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var clubs = ClubsDB.GetClubsCreatedBy(player)
                .Where(c => c.Visibility != 2)
                .Select(ClubsDB.ToSummary)
                .ToList();

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(clubs),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("/club/{clubId}")]
        [HttpGet("/club/{clubId}/details")]
        public IActionResult GetClubDetails(long clubId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            return Ok(new ClubDetailsDTO
            {
                Club = ClubsDB.ToSummary(club),
                CustomTags = club.CustomTags,
                AdditionalImages = club.AdditionalImages,
                CoownerPermissions = club.Permissions.FirstOrDefault(p => p.Type == (int)ClubMemberType.CoOwner),
                ModeratorPermissions = club.Permissions.FirstOrDefault(p => p.Type == (int)ClubMemberType.Moderator),
                MemberPermissions = club.Permissions.FirstOrDefault(p => p.Type == (int)ClubMemberType.Member),
                MyMembershipType = club.Members
                    .FirstOrDefault(m => m.AccountId == id.Value)?.MemberType ?? 0
            });
        }

        [HttpGet("/club/{clubId}/members")]
        public IActionResult GetClubMembers(long clubId, [FromQuery] int? membershipType)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            var members = ClubsDB.GetClubMembers(clubId);
            if (membershipType.HasValue)
                members = members.Where(m => m.MembershipType == membershipType.Value).ToList();

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { value = members, success = true, error_id = (int?)null, error = (string?)null }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpDelete("/club/{clubId}")]
        public IActionResult DeleteClub(long clubId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            if (club.CreatorAccountId != id.Value)
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to delete club {clubId} but is not the creator.");
                return StatusCode(403);
            }

            ClubsDB.DeleteClub(clubId);

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = (string?)null, value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/modify")]
        public IActionResult ModifyClub(
            long clubId,
            [FromForm] string? name,
            [FromForm] string? description,
            [FromForm] string? category)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var existing = ClubsDB.GetClub(clubId);
            if (existing == null) return NotFound("");

            if (!ClubsDB.CanEditDetails(clubId, id.Value))
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to modify club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            var club = ClubsDB.Modify(
                clubId,
                (name ?? existing.Name).Trim(),
                (description ?? existing.Description ?? "").Trim(),
                (category ?? existing.Category).Trim(),
                id.Value);

            if (club == null) return Unauthorized("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = (string?)null, value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/modifydetails")]
        public IActionResult ModifyClubDetails(
            long clubId,
            [FromForm] string? visibility,
            [FromForm] string? joinability,
            [FromForm] string? allowJuniors,
            [FromForm] string? customTags)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            if (!ClubsDB.CanEditDetails(clubId, id.Value))
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to modify club {clubId} but does not have permission. (2)");
                return StatusCode(403);
            }

            int? vis = visibility != null ? ParseVisibility(visibility) : (int?)null;
            int? join = joinability != null ? ParseJoinability(joinability) : (int?)null;
            if (join == 1) return StatusCode(501);
            bool? juniors = null;
            if (allowJuniors != null)
                juniors = allowJuniors.ToLowerInvariant() is "true" or "1";

            List<string>? tags = null;
            if (customTags != null)
                tags = customTags
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

            var club = ClubsDB.ModifyDetails(clubId, vis, join, juniors, tags, id.Value);
            if (club == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/minlevel")]
        public IActionResult SetMinLevel(long clubId, [FromForm] int minLevel)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            if (!ClubsDB.CanEditDetails(clubId, id.Value))
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to set min level for club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            var club = ClubsDB.SetMinLevel(clubId, minLevel, id.Value);
            if (club == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = (string?)null, value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/mainimage")]
        public IActionResult SetMainImage(long clubId, [FromForm] string imageName)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var existing = ClubsDB.GetClub(clubId);
            if (existing == null) return NotFound("");

            if (!ClubsDB.CanEditDetails(clubId, id.Value))
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to set the main image for club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            var club = ClubsDB.SetMainImage(clubId, imageName, id.Value);
            if (club == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpDelete("/club/{clubId}/mainimage")]
        public IActionResult DeleteMainImage(long clubId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var existing = ClubsDB.GetClub(clubId);
            if (existing == null) return NotFound("");

            if (!ClubsDB.CanEditDetails(clubId, id.Value))
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to delete the main image for club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            if (existing.AdditionalImages.Count == 0)
                return StatusCode(405);

            var club = ClubsDB.DeleteMainImage(clubId, id.Value);
            if (club == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/additionalimage/{index}")]
        public IActionResult SetAdditionalImage(long clubId, int index, [FromForm] string imageName)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var existing = ClubsDB.GetClub(clubId);
            if (existing == null) return NotFound("");

            if (!ClubsDB.CanEditDetails(clubId, id.Value))
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to set additional image {index} for club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            var club = ClubsDB.SetAdditionalImage(clubId, index, imageName, id.Value);
            if (club == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/clubhouse")]
        public IActionResult SetClubhouse(long clubId, [FromForm] long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var existing = ClubsDB.GetClub(clubId);
            if (existing == null) return NotFound("");

            if (!ClubsDB.IsOwnerOrCoOwner(clubId, id.Value) && existing.CreatorAccountId != id.Value)
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to set clubhouse for club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            var club = ClubsDB.SetClubhouse(clubId, roomId, id.Value);
            if (club == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpDelete("/club/{clubId}/clubhouse")]
        public IActionResult RemoveClubhouse(long clubId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var existing = ClubsDB.GetClub(clubId);
            if (existing == null) return NotFound("");

            if (!ClubsDB.IsOwnerOrCoOwner(clubId, id.Value) && existing.CreatorAccountId != id.Value)
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to remove clubhouse for club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            var club = ClubsDB.RemoveClubhouse(clubId, id.Value);
            if (club == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("/club/{clubId}/hasDisabledClubChat")]
        public IActionResult HasDisabledClubChat(long clubId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            return Ok(false);
        }

        [HttpPut("/club/{clubId}/members/requesttojoin")]
        public async Task<IActionResult> RequestToJoin(long clubId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var existing = ClubsDB.GetClub(clubId);
            if (existing == null) return NotFound("");

            if (existing.Joinability == (int)ClubJoinability.Closed) return BadRequest(new { success = false, error = "The club is invite only", error_id = "Club.InviteOnly", value = (object?)null });

            var club = ClubsDB.JoinClub(clubId, id.Value);
            if (club == null) return NotFound("");

            var joined = club.Members.FirstOrDefault(m => m.AccountId == id.Value);
            if (joined != null)
            {
                await NotificationsController.BroadcastClubMembershipUpdate(joined.MemberId, id.Value, clubId, joined.MemberType, joined.JoinedAt);
                await NotificationsController.BroadcastCreatorClubSubscriptionUpdate(club.CreatorAccountId, clubId, joined.MemberType);
            }

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("/club/{clubId}/members/leave")]
        public async Task<IActionResult> RequestLeaveClub(long clubId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var existing = ClubsDB.GetClub(clubId);
            if (existing == null) return NotFound("");

            var leaving = existing.Members.FirstOrDefault(m => m.AccountId == id.Value);

            var club = ClubsDB.LeaveClub(clubId, id.Value);
            if (club == null) return NotFound("");

            if (leaving != null)
            {
                await NotificationsController.BroadcastClubMembershipUpdate(leaving.MemberId, id.Value, clubId, 0, DateTime.UtcNow);
                await NotificationsController.BroadcastCreatorClubSubscriptionUpdate(existing.CreatorAccountId, clubId, 0);
            }

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/permissions/{membershipType}")]
        public IActionResult SetPermissions(long clubId, int membershipType, [FromForm] bool approveMember, [FromForm] bool banUnban, [FromForm] bool createEvent, [FromForm] bool editDetails, [FromForm] bool editPermissionSettings, [FromForm] bool postAnnouncement)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var existing = ClubsDB.GetClub(clubId);
            if (existing == null) return NotFound("");

            if (!ClubsDB.IsOwnerOrCoOwner(clubId, id.Value))
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to set permissions for club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            var club = ClubsDB.SetPermissions(clubId, membershipType, approveMember, banUnban, createEvent, editDetails, editPermissionSettings, postAnnouncement);
            if (club == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = ClubsDB.ToDetails(club, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("/club/home/me")]
        public IActionResult GetMyHomeClub()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null) return Unauthorized("");

            int homeClubId = (int)player.Player.PlayerExtra.HomeClubhouseId;
            if (homeClubId == 0 || homeClubId == null) return NotFound("");

            var club = ClubsDB.GetClub(homeClubId);
            if (club == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(ClubsDB.ToSummary(club)),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/home/me")]
        public IActionResult SetMyHomeClub([FromForm] long clubId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null) return Unauthorized("");

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            player.Player.PlayerExtra.HomeClubhouseId = clubId;
            PlayerDB.Players.Update(player);

            return Ok(new { success = true, error = (string?)null, value = (object?)null });
        }

        [HttpDelete("/club/home/me")]
        public IActionResult DeleteMyHomeClub([FromForm] long clubId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null) return Unauthorized("");

            player.Player.PlayerExtra.HomeClubhouseId = 0;
            PlayerDB.Players.Update(player);

            return Ok(new { success = true, error = (string?)null, value = (object?)null });
        }

        [HttpGet("/announcements/mine")]
        public IActionResult GetMyAnnouncements()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var result = ClubsDB.GetAnnouncementsForMyClubs(id.Value);

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = result }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("/announcements/club/{clubId}")]
        public IActionResult GetClubAnnouncements(long clubId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var result = ClubsDB.GetClubAnnouncements(clubId, id.Value);
            if (result == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = result }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("/announcements/club/{clubId}")]
        public IActionResult CreateAnnouncement(
            long clubId,
            [FromForm] string title,
            [FromForm] string body,
            [FromForm] string? imageName,
            [FromForm] string? meta)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            if (string.IsNullOrWhiteSpace(title) || title.Length > 50)
                return BadRequest(new { success = false, error = "Title must be between 1 and 50 characters." });

            if (string.IsNullOrWhiteSpace(body) || body.Length > 1000)
                return BadRequest(new { success = false, error = "Body must be between 1 and 1000 characters." });

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            if (!ClubsDB.CanPostAnnouncement(clubId, id.Value))
                return StatusCode(403);

            var announcement = ClubsDB.CreateAnnouncement(clubId, id.Value, title, body, imageName, meta);
            if (announcement == null) return StatusCode(500);

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = announcement.AnnouncementId }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/announcements/club/{clubId}/{announcementId}")]
        public IActionResult EditAnnouncement(
            long clubId,
            long announcementId,
            [FromForm] string title,
            [FromForm] string body,
            [FromForm] string? imageName,
            [FromForm] string? meta)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            if (string.IsNullOrWhiteSpace(title) || title.Length > 50)
                return BadRequest(new { success = false, error = "Title must be between 1 and 50 characters." });

            if (string.IsNullOrWhiteSpace(body) || body.Length > 1000)
                return BadRequest(new { success = false, error = "Body must be between 1 and 1000 characters." });

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            var announcement = ClubsDB.EditAnnouncement(clubId, announcementId, id.Value, title, body, imageName, meta);
            if (announcement == null) return StatusCode(403);

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = "", value = announcement.AnnouncementId }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpDelete("/announcements/club/{clubId}/{announcementId}")]
        public IActionResult DeleteAnnouncement(long clubId, long announcementId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            bool deleted = ClubsDB.DeleteAnnouncement(clubId, announcementId, id.Value);
            if (!deleted) return StatusCode(403);

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = (string?)null, value = (object?)null }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("/announcements/club/{clubId}/{announcementId}/read")]
        public IActionResult MarkAnnouncementRead(long clubId, long announcementId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            bool ok = ClubsDB.MarkAnnouncementRead(clubId, announcementId, id.Value);
            if (!ok) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = (string?)null, value = (object?)null }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/members/{targetId}/role")]
        public async Task<IActionResult> SetMemberRole(long clubId, long targetId, [FromForm] int membershipType)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            if (!ClubsDB.IsOwnerOrCoOwner(clubId, id.Value) && club.CreatorAccountId != id.Value)
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to set role in club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            var target = club.Members.FirstOrDefault(m => m.AccountId == targetId);
            if (target == null) return NotFound("");

            bool ok = ClubsDB.SetMemberRole(clubId, targetId, membershipType, id.Value);
            if (!ok) return NotFound("");

            await NotificationsController.BroadcastClubMembershipUpdate(target.MemberId, targetId, clubId, membershipType, target.JoinedAt);

            var updated = ClubsDB.GetClub(clubId);
            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = (string?)null, value = updated != null ? ClubsDB.ToDetails(updated, id.Value) : null }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/members/{targetId}/kick")]
        public async Task<IActionResult> KickMember(long clubId, long targetId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            var target = club.Members.FirstOrDefault(m => m.AccountId == targetId);

            var result = ClubsDB.KickMember(clubId, targetId, id.Value);
            if (result == null)
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to kick {targetId} from club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            if (target != null)
                await NotificationsController.BroadcastClubMembershipUpdate(target.MemberId, targetId, clubId, 0, DateTime.UtcNow);

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = (string?)null, value = ClubsDB.ToDetails(result, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/members/{targetId}/ban")]
        public async Task<IActionResult> BanMember(long clubId, long targetId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            var target = club.Members.FirstOrDefault(m => m.AccountId == targetId);

            bool ok = ClubsDB.BanMember(clubId, targetId, id.Value);
            if (!ok)
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to ban {targetId} from club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            if (target != null)
                await NotificationsController.BroadcastClubMembershipUpdate(target.MemberId, targetId, clubId, 0, DateTime.UtcNow);

            var updated = ClubsDB.GetClub(clubId);
            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = (string?)null, value = updated != null ? ClubsDB.ToDetails(updated, id.Value) : null }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/members/{targetId}/unban")]
        public IActionResult UnbanMember(long clubId, long targetId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            bool ok = ClubsDB.UnbanMember(clubId, targetId, id.Value);
            if (!ok)
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to unban {targetId} from club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            var updated = ClubsDB.GetClub(clubId);
            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = (string?)null, value = updated != null ? ClubsDB.ToDetails(updated, id.Value) : null }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPut("/club/{clubId}/members/{targetId}/approve")]
        public async Task<IActionResult> ApproveMember(long clubId, long targetId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var club = ClubsDB.GetClub(clubId);
            if (club == null) return NotFound("");

            var result = ClubsDB.ApproveMember(clubId, targetId, id.Value);
            if (result == null)
            {
                APIController.SendNonHileWebhook(id.Value, $"Attempted to approve {targetId} in club {clubId} but does not have permission.");
                return StatusCode(403);
            }

            var member = result.Members.FirstOrDefault(m => m.AccountId == targetId);
            if (member != null)
            {
                await NotificationsController.BroadcastClubMembershipUpdate(member.MemberId, targetId, clubId, member.MemberType, member.JoinedAt);
                await NotificationsController.BroadcastCreatorClubSubscriptionUpdate(result.CreatorAccountId, clubId, member.MemberType);
            }

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { success = true, error = (string?)null, value = ClubsDB.ToDetails(result, id.Value) }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }
    }
}