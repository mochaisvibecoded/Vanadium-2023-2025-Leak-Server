using System.Text.RegularExpressions;
using LiteDB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration.UserSecrets;
using Newtonsoft.Json;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Classes.WebSocket;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text.Json;
using System.Text;
using Vanadium.Utils;
using static Vanadium.Classes.DBs.DBClasses.RoomDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using Vanadium.Utils.NotiController;

namespace Vanadium.Controllers
{
    [ApiController]
    [Route("/roomserver")]
    public partial class RoomController : ControllerBase
    {
    
    	private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, List<DateTime>> _cloneRateLimits = new();
    
        private IActionResult RoomError(string errorId, string message, int status = 200)
            => StatusCode(status, new { success = false, error_id = errorId, error = message, value = (object?)null });

        private bool CanEdit(Room room, long playerId)
            => room.CreatorAccountId == playerId ||
               (room.Roles?.Any(r => r.AccountId == playerId &&
                   (r.Role == Role.CoOwner || r.Role == Role.Creator || r.Role == Role.TemporaryCoOwner)) ?? false);

        private static bool IsBaseRoom(Room r)
            => r.Tags != null && r.Tags.Any(t => t.Tag != null && t.Tag.Equals("base", StringComparison.OrdinalIgnoreCase));

        private bool CanViewSaves(Room room, long playerId)
    		=> room.CreatorAccountId == playerId ||
       			(room.Roles?.Any(r => r.AccountId == playerId &&
           			(r.Role == Role.CoOwner || r.Role == Role.Creator || r.Role == Role.TemporaryCoOwner)) ?? false) ||
       					(PlayerDB.Players.FindById(playerId)?.PlayerRoles?.Contains(PlayerDBClasses.PlayerRoles.Developer) ?? false);

        private bool RequestHasGameClientRole()
        {
            var auth = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer "))
                return false;

            var tokenStr = auth.Substring("Bearer ".Length).Trim();

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(tokenStr);

                var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
                if (string.IsNullOrEmpty(roleClaim))
                    return false;

                var roles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(roleClaim);
                return roles?.Contains("gameClient") ?? false;
            }
            catch
            {
                return false;
            }
        }

        [HttpGet("rooms")]
        public async Task<IActionResult> GetRoomBy([FromQuery] string? name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var room = RoomDB.GetRoomByName(name);
                return room != null ? Ok(room) : NotFound();
            }
            return NotFound("");
        }


        /*[HttpGet("rooms/{roomId}/subrooms/{subroomId}/saves/{subRoomDataSaveId}")]
        public async Task<IActionResult> GetRoomSave(ulong roomId, ulong subroomId, ulong subRoomDataSaveId, [FromQuery] int unityAssetTarget, [FromQuery] int unityAssetVersion)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            var room = RoomDB.GetRoom((long)roomId);
            if (room == null)
                return new ContentResult() { Content = "", ContentType = "application/json", StatusCode = 404 };

            if (!CanViewSaves(room, (long)playerId))
                return StatusCode(403);

            var save = RoomDB.GetRoomSaveById(roomId, subroomId, subRoomDataSaveId, unityAssetTarget, unityAssetVersion);

            if (save == null)
            {
                return new ContentResult()
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 404
                };
            }

            return Ok(save);
        }*/

        [HttpGet("rooms/{roomid}")]
        public IActionResult GetRoomById(long roomid)
        {
            var room = RoomDB.GetRoom(roomid);

            if (room == null)
            {
                return Ok(ServerConfig.Bracket);
            }

            var roomResponse = new
            {
                room.RoomId,
                room.IsDorm,
                room.MaxPlayerCalculationMode,
                room.MaxPlayers,
                room.CloningAllowed,
                room.DisableMicAutoMute,
                room.DisableRoomComments,
                room.EncryptVoiceChat,
                room.ToxmodEnabled,
                room.LoadScreenLocked,
                room.PersistenceVersion,
                room.UgcSubVersion,
                room.UgcVersion,
                room.AutoLocalizeRoom,
                room.IsDeveloperOwned,
                room.IsRRO,
                room.IsRecRoomApproved,
                room.IsJuniorCreated,
                room.IsPlacePlay,
                room.ExcludeFromLists,
                room.ExcludeFromSearch,
                room.Name,
                room.Description,
                ImageName = room.ImageName == "DormRoom.jpg" ? null : room.ImageName,
                room.WarningMask,
                room.CustomWarning,
                room.CreatorAccountId,
                room.State,
                room.Accessibility,
                room.SupportsLevelVoting,
                room.SupportsScreens,
                room.SupportsWalkVR,
                room.SupportsTeleportVR,
                room.SupportsVRLow,
                room.SupportsQuest2,
                room.SupportsMobile,
                room.SupportsJuniors,
                room.MinLevel,
                room.MinUgcSubVersion,
                room.AgeRating,
                room.CreatedAt,
                room.PublishedAt,
                room.BecameRRStudioRoomAt,
                room.Stats,
                room.RankedEntityId,
                room.RankingContext,
                DataBlob = room.DataBlob,
                room.CurrentSnapshotId,
                room.NeedsSnapshotId,
                room.ShouldAutoStageSaves,
                room.Roles,
                room.Tags,
                room.PromoImages,
                room.PromoExternalContent,
                room.LoadScreens,
                room.RestrictedCircuitsAllowListNames,
                room.LocalizationContext,
                room.RoomBanDetails,

                SubRooms = room.SubRooms?.Select(sub => new
                {
                    CurrentSave = (sub.CurrentSave != null ? new
                        {
                            sub.CurrentSave.SubRoomDataSaveId,
                            sub.CurrentSave.SubRoomId,
                            sub.CurrentSave.RoomId,
                            DataBlob = sub.CurrentSave.DataBlob,
                            sub.CurrentSave.SavedByAccountId,
                            sub.CurrentSave.SavedOnPlatform,
                            sub.CurrentSave.SavedOnDeviceClass,
                            sub.CurrentSave.Description,
                            sub.CurrentSave.CreatedAt,
                            sub.CurrentSave.UnityAssetId
                        } : RoomDB.SubRoomSaves
                        .Find(x => x.RoomId == room.RoomId && x.SubRoomId == sub.SubRoomId)
                        .OrderByDescending(x => x.CreatedAt)
                        .Select(save => new
                        {
                            save.SubRoomDataSaveId,
                            save.SubRoomId,
                            save.RoomId,
                            DataBlob = save.DataBlob,
                            save.SavedByAccountId,
                            save.SavedOnPlatform,
                            save.SavedOnDeviceClass,
                            save.Description,
                            save.CreatedAt,
                            save.UnityAssetId
                        })
                        .FirstOrDefault()),

                    sub.Accessibility,
                    sub.IsSandbox,
                    sub.MaxPlayers,
                    sub.Name,
                    sub.RoomId,
                    sub.SubRoomId,
                    sub.UnitySceneId,
                    DataBlob = sub.DataBlob,
                    sub.SavedByAccountId,
                    sub.ShouldAutoStageSaves,
                    sub.StagedSubRoomDataSaveId,
                    sub.LastModeratedSaveModerationState,
                    sub.CreatorAccountId
                }).ToList()
            };

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(roomResponse),
                ContentType = "application/json",
                StatusCode = 200
            };
        }
        
        [HttpGet("unity_assets/{guid}/{target}/{version}")]
        public IActionResult GetUnityAssets(string guid, int target, int version)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
            	return StatusCode(403);
                
            return Ok(new
            {
                UnityAssetId = guid,
                Target = target,
                Version = version,
                Filename = $"{guid}_{target}_{version}.assetbundle", // hacky?>?? it a stub?? but ok and it will hold up for now
                Hash = (string?)null
            });

            // ts will fail when we get rr studio or 2025 support (2025 uses more stupid shit)
            // someone test vana on 2025 tho lowkey || as of 8/25/2026, 2025 work so good soonnnnioonnn
        }

        [HttpPut("rooms/{roomId}/roles/{playerId}/invite")]
		public async Task<IActionResult> SetRoomRole(long roomId, long playerId, [FromForm] int role)
		{
				var account = AuthStuff.GetPlayerId(Request);
				if (account == null)
						return StatusCode(403);

				var room = RoomDB.GetRoom(roomId);
				if (room == null)
						return NotFound(new { success = false, error = "Room not found" });

				if (room.IsDorm)
						return RoomError("Rooms.UserCannotBeInvitedInsideDorm", "You do not have permission to change roles inside of your Dorm Room.");

				room.Roles ??= new List<RoomDBClasses.Roles>();

				var newRole = (RoomDBClasses.Role)role;

				var callerEntry = room.Roles.FirstOrDefault(r => r.AccountId == account.Value);
				bool isOwner = room.CreatorAccountId == account.Value;
				bool isCoOwner = callerEntry != null && (callerEntry.Role == Role.CoOwner || callerEntry.Role == Role.TemporaryCoOwner);

				if (!isOwner && !isCoOwner)
						return StatusCode(403);

				var targetEntry = room.Roles.FirstOrDefault(r => r.AccountId == playerId);
				bool targetIsOwner = room.CreatorAccountId == playerId;

				if (targetIsOwner)
						return StatusCode(403);

				var targetCurrentRole = targetEntry?.Role ?? Role.None;
				var targetCurrentInvited = targetEntry?.InvitedRole ?? Role.None;

				bool targetIsCoOwnerTier = targetCurrentRole == Role.CoOwner || targetCurrentRole == Role.TemporaryCoOwner
						|| targetCurrentInvited == Role.CoOwner || targetCurrentInvited == Role.TemporaryCoOwner;

				if (targetIsCoOwnerTier && !isOwner)
						return StatusCode(403);

				if (newRole == Role.None || role == 0)
				{
						if (targetEntry != null)
						{
								bool wasCoOwnerTier = targetEntry.Role == Role.CoOwner || targetEntry.Role == Role.TemporaryCoOwner
										|| targetEntry.InvitedRole == Role.CoOwner || targetEntry.InvitedRole == Role.TemporaryCoOwner;

								room.Roles.Remove(targetEntry);
								RoomDB.Rooms.Update(room);

								if (wasCoOwnerTier)
								{
										var removePayload = JsonConvert.SerializeObject(new
										{
												Id = "2",
												Msg = new
												{
														FromPlayerId = account,
														Id = Random.Shared.Next(1, 0x7ffffff),
														SentTime = DateTime.UtcNow,
														Type = WebsocketEvents.WebsocketMessageType.RoomCoOwnerRemoved,
														Data = (string?)null,
														RoomId = roomId,
														PlayerEventId = (long?)null
												}
										});
										await NotificationsController.SendToPlayer(playerId, removePayload);
								}
						}

						var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
						var updatedRoom = RoomDB.GetRoom(roomId);
						foreach (var pid in allPlayers)
								await NotiController.SendRoomUpdate(pid, updatedRoom);

						return Ok(new { success = true });
				}

				bool isCoOwnerInvite = newRole == Role.CoOwner || newRole == Role.TemporaryCoOwner;

				if (isCoOwnerInvite)
				{
						if (!isOwner)
								return StatusCode(403);

						if (targetEntry != null)
						{
								targetEntry.Role = Role.None;
								targetEntry.InvitedRole = newRole;
								targetEntry.LastChangedByAccountId = account;
						}
						else
						{
								room.Roles.Add(new RoomDBClasses.Roles
								{
										AccountId = playerId,
										Role = Role.None,
										InvitedRole = newRole,
										LastChangedByAccountId = account
								});
						}

						RoomDB.Rooms.Update(room);

						var json = JsonConvert.SerializeObject(new
						{
								Id = "2",
								Msg = new
								{
										FromPlayerId = account,
										Id = Random.Shared.Next(1, 0x7ffffff),
										SentTime = DateTime.UtcNow,
										Type = MessageType.RoomCoOwnerInvited,
										Data = (string?)null,
										RoomId = roomId,
										PlayerEventId = (long?)null
								}
						});
						await NotificationsController.SendToPlayer(playerId, json);
				}
				else
				{
						if (targetEntry != null)
						{
								targetEntry.Role = newRole;
								targetEntry.InvitedRole = Role.None;
								targetEntry.LastChangedByAccountId = account;
						}
						else
						{
								room.Roles.Add(new RoomDBClasses.Roles
								{
										AccountId = playerId,
										Role = newRole,
										InvitedRole = Role.None,
										LastChangedByAccountId = account
								});
						}

						RoomDB.Rooms.Update(room);
				}

				await NotiController.SendRoomUpdate(playerId, RoomDB.GetRoom(roomId));
				await NotiController.SendRoomUpdate((long)account, RoomDB.GetRoom(roomId));
				return Ok(new { success = true });
		}

		[HttpGet("rooms/{roomId}/roles/{playerId}")] // made a temp workaround i think the response is correct?
		public IActionResult GetRoomRole(long roomId, long playerId)
		{
			var account = AuthStuff.GetPlayerId(Request);
			if (account == null)
				return StatusCode(403);

			var room = RoomDB.GetRoom(roomId);
			if (room == null)
				return NotFound(new { success = false, error = "Room not found" });

			var entry = room.Roles?.FirstOrDefault(r => r.AccountId == playerId);

			if (entry == null)
				return Ok(new { AccountId = playerId, Role = Role.None, InvitedRole = Role.None });

			return Ok(new
			{
				entry.AccountId,
				entry.Role,
				entry.InvitedRole,
				entry.LastChangedByAccountId
			});
		}

		[HttpGet("rooms/{roomId}/roles")]
		public IActionResult GetRoomRoles(long roomId)
		{
			var account = AuthStuff.GetPlayerId(Request);
			if (account == null)
				return StatusCode(403);

			var room = RoomDB.GetRoom(roomId);
			if (room == null)
				return NotFound(new { success = false, error = "Room not found" });

			var roles = (room.Roles ?? new List<RoomDBClasses.Roles>())
				.Select(r => new
				{
					r.AccountId,
					r.Role,
					r.InvitedRole,
					r.LastChangedByAccountId
				})
				.ToList();

			return Ok(roles);
		}

		[HttpPut("rooms/{roomId}/roles/{playerId}")]
		public async Task<IActionResult> RemoveRoomRole(long roomId, long playerId, [FromForm] int role)
		{
				var account = AuthStuff.GetPlayerId(Request);
				if (account == null)
						return StatusCode(403);

				var room = RoomDB.GetRoom(roomId);
				if (room == null)
						return NotFound(new { success = false, error = "Room not found" });

				room.Roles ??= new List<RoomDBClasses.Roles>();

				var existing = room.Roles.FirstOrDefault(r => r.AccountId == playerId);
                var existingTwo = room.Roles.FirstOrDefault(r => r.AccountId == account.Value);

				if (account == playerId)
				{
						if (existing == null)
								return Ok(new { success = true });

						var incomingRole = (RoomDBClasses.Role)role;

						if (incomingRole == Role.None)
						{
								bool wasCoOwnerSelf = existing.Role == Role.CoOwner || existing.Role == Role.TemporaryCoOwner;
								room.Roles.Remove(existing);
								RoomDB.Rooms.Update(room);

								if (wasCoOwnerSelf)
								{
										var wsPayload = JsonConvert.SerializeObject(new
										{
												Id = "2",
												Msg = new
												{
														FromPlayerId = account,
														Id = Random.Shared.Next(1, 0x7ffffff),
														SentTime = DateTime.UtcNow,
														Type = WebsocketEvents.WebsocketMessageType.RoomCoOwnerRemoved,
														Data = (string?)null,
														RoomId = roomId,
														PlayerEventId = (long?)null
												}
										});
										await NotificationsController.SendToPlayer(account.Value, wsPayload);
								}

								var allPids = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
								var updatedRoomSelf = RoomDB.GetRoom(roomId);
								foreach (var pid in allPids)
										await NotiController.SendRoomUpdate(pid, updatedRoomSelf);

								return Ok(new { success = true });
						}

						if (existing.InvitedRole == Role.CoOwner)
						{
								if (incomingRole == Role.CoOwner)
								{
										existing.Role = Role.CoOwner;
										existing.InvitedRole = Role.None;
										existing.LastChangedByAccountId = account;
								}
								else
								{
										existing.InvitedRole = Role.None;
										existing.LastChangedByAccountId = account;

										if (existing.Role == Role.None)
												room.Roles.Remove(existing);
								}
						}
				}
				else
				{
						var newRole = (RoomDBClasses.Role)role;

						bool wasCoOwner = existing != null &&
								(existing.Role == Role.CoOwner || existing.Role == Role.TemporaryCoOwner);
						bool beingDemoted = (int)newRole < (int)Role.CoOwner;

						if (newRole == Role.None)
						{
								if (existing != null)
										room.Roles.Remove(existing);
						}
						else
						{
                            if (existingTwo != null && existingTwo.Role < newRole)
                            {
                                APIController.SendNonHileWebhook(playerId, "not a hile, but someone tried to claim " + newRole + " role in room " + roomId);
                                return StatusCode(403);
                            }
								if (existing != null)
								{
										existing.Role = newRole;
										existing.InvitedRole = Role.None;
										existing.LastChangedByAccountId = account;
								}
								else
								{
										room.Roles.Add(new RoomDBClasses.Roles
										{
												AccountId = playerId,
												Role = newRole,
												InvitedRole = Role.None,
												LastChangedByAccountId = account
										});
								}
						}

						if (wasCoOwner && beingDemoted)
						{
								var wsPayload = JsonConvert.SerializeObject(new
								{
										Id = "2",
										Msg = new
										{
												FromPlayerId = account,
												Id = Random.Shared.Next(1, 0x7ffffff),
												SentTime = DateTime.UtcNow,
												Type = WebsocketEvents.WebsocketMessageType.RoomCoOwnerRemoved,
												Data = (string?)null,
												RoomId = roomId,
												PlayerEventId = (long?)null
										}
								});
								await NotificationsController.SendToPlayer(playerId, wsPayload);
						}
				}

				RoomDB.Rooms.Update(room);

				var allPlayerIds = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
				var updatedRoom = RoomDB.GetRoom(roomId);
				foreach (var pid in allPlayerIds)
						await NotiController.SendRoomUpdate(pid, updatedRoom);

				return Ok(new { success = true });
		}

        [HttpGet("rooms/bulk")]
		public async Task<IActionResult> GetRoomsBulk([FromQuery] List<string> name, [FromQuery] List<long> id)
		{
        
        	var userAgent = Request.Headers.UserAgent.ToString();
			if (!userAgent.Contains("BestHTTP", StringComparison.OrdinalIgnoreCase) && !Request.Headers.Cookie.ToString().Contains("cf_clearance"))
				return StatusCode(404);
        
			var roomsByName = RoomDB.GetRoomsByNames(name);
			var roomsById = RoomDB.GetRoomsByIds(id);
			var combinedRooms = roomsByName.Concat(roomsById).DistinctBy(r => r.RoomId).ToList();
			return Ok(combinedRooms);
		}

        [HttpGet("rooms/hot")]
        public async Task<IActionResult> HotRooms([FromQuery] string tag = "", [FromQuery] int skip = 0, [FromQuery] int take = 30)
        {
            string? passingTag = string.IsNullOrWhiteSpace(tag) ? null : tag;
            var (results, total) = RoomDB.GetHotRooms(passingTag, skip, take);
            return Ok(new
            {
                Results = results ?? new List<Room>(),
                TotalResults = total
            });
        }

        [HttpGet("rooms/search")]
		public async Task<IActionResult> SearchRooms([FromQuery] string query = "", [FromQuery] int skip = 0, [FromQuery] int take = 30)
		{
			if (string.IsNullOrWhiteSpace(query))
			{
				var (hotResults, hotTotal) = RoomDB.GetHotRooms(null, skip, take);
				return Ok(new
				{
					Results = hotResults ?? new List<Room>(),
					TotalResults = hotTotal
				});
			}

			var (searchList, searchTotal) = RoomDB.Search(query, skip: skip, take: take);
			return Ok(new
			{
				Results = searchList ?? new List<Room>(),
				TotalResults = searchTotal
			});
		}

        [HttpGet("rooms/base")]
        public async Task<IActionResult> GetBaseRooms()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var rooms = RoomDB.GetBaseRooms();
            
            foreach(var room in rooms)
            {
                room.Accessibility = RoomAccessibility.Public; 
            }

            return Ok(rooms);
        }

		[HttpGet("rooms/contributedby/me")]
		[HttpGet("rooms/createdby/me")]
        [HttpGet("rooms/ownedby/me")]
        public async Task<IActionResult> GetCreatedByMe(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 9999)
        {
            var id = AuthStuff.GetPlayerId(Request);

            if (id == null)
                return Unauthorized("");

            var results = RoomDB.Rooms.FindAll()
                .Where(r =>
                    !IsBaseRoom(r) &&
                    (
                        r.CreatorAccountId == (long)id ||
                        (r.Roles?.Any(x =>
                         x.AccountId == (long)id &&
                        (x.Role == Role.CoOwner ||
                         x.Role == Role.Creator || x.Role == Role.TemporaryCoOwner )) ?? false)
                    ))
                .OrderByDescending(r => r.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToList();

            return Ok(results);
        }

        [HttpGet("rooms/ownedby/{accountId}")]
        public async Task<IActionResult> GetRoomsOwnedBy(long accountId)
        {
            return Ok(RoomDB.GetRoomsByCreator(accountId).Where(r => !IsBaseRoom(r)).ToList());
        }

        [HttpGet("rooms/visitedby/me")]
        public async Task<IActionResult> GetVisitedByMe([FromQuery] int skip = 0, [FromQuery] int take = 9)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var visits = PlayerDB.GetPlayerRoomVisits((long)id);

            var results = visits
                .OrderByDescending(rv => rv.LastVisitedAt)
                .Skip(skip).Take(take)
                .Select(rv => RoomDB.GetRoom(rv.RoomId))
                .Where(r => r != null && !IsBaseRoom(r))
                .ToList();

            return Ok(results);
        }
        
        [HttpGet("rooms/visitedby/{playerId}")]
        public async Task<IActionResult> GetVisitedByOther(long playerId, [FromQuery] int skip = 0, [FromQuery] int take = 10)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var visits = PlayerDB.GetPlayerRoomVisits(playerId);

            var results = visits
                .OrderByDescending(rv => rv.LastVisitedAt)
                .Skip(skip).Take(take)
                .Select(rv => RoomDB.GetRoom(rv.RoomId))
                .Where(r => r != null && !IsBaseRoom(r))
                .ToList();

            return Ok(results);
        }

        [HttpGet("rooms/favoritedby/me")]
        public async Task<IActionResult> GetFavoritedByMe([FromQuery] int skip = 0, [FromQuery] int take = 100)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var col = RoomDB.RoomDBFile.GetCollection("Rooms");

            var rooms = new List<Room>();

            foreach (var doc in col.FindAll())
            {
                try
                {
                    var room = BsonMapper.Global.Deserialize<Room>(doc);

                    if (room == null)
                        continue;

                    if (IsBaseRoom(room))
                        continue;

                    if (!PlayerDB.HasFavoritedRoom((long)id, room.RoomId))
                        continue;

                    rooms.Add(room);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GetFavoritedByMe] Skipped corrupt room {doc["_id"]}: {ex.Message}");
                }
            }

            var results = rooms
                .OrderByDescending(r => r.Stats?.FavoriteCount ?? 0)
                .Skip(skip)
                .Take(take)
                .ToList();

            return Ok(results);
        }

        [HttpGet("/roomserver/dormroom/me")]
        public async Task<IActionResult> GetMyDormRoom()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var player = PlayerDB.GetCurrentPlayer((long)id);
            if (player == null) return NotFound("");

            return Ok(player.Player?.PlayerExtra?.DormRoomId ?? 0);
        }

        [HttpGet("rooms/{roomId}/interactionby/me")]
        public async Task<IActionResult> GetInteractionByMe(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            return Ok(new
            {
                Cheered = PlayerDB.HasCheeredRoom((long)id, roomId),
                Favorited = PlayerDB.HasFavoritedRoom((long)id, roomId),
                LastVisitedAt = PlayerDB.GetRoomLastVisitedAt((long)id, roomId) ?? DateTime.UtcNow
            });
        }

        [HttpPut("rooms/{roomId}/interactionby/me/favorite")]
        public async Task<IActionResult> FavoriteRoom(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            if (!PlayerDB.HasFavoritedRoom((long)id, roomId))
            {
                PlayerDB.AddFavoriteRoom((long)id, roomId);
                RoomDB.IncrementRoomFavoriteCount(roomId);
            }

			await NotiController.SendRoomUpdate((long)id, RoomDB.GetRoom(roomId));

            return Ok(new { success = true, error = "", value = new { Favorited = true } });
        }

        [HttpDelete("rooms/{roomId}/interactionby/me/favorite")]
        public async Task<IActionResult> UnfavoriteRoom(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            if (PlayerDB.HasFavoritedRoom((long)id, roomId))
            {
                PlayerDB.RemoveFavoriteRoom((long)id, roomId);
                RoomDB.DecrementRoomFavoriteCount(roomId);
            }
            
            await NotiController.SendRoomUpdate((long)id, RoomDB.GetRoom(roomId));

            return Ok(new { success = true, error = "", value = new { Favorited = false } });
        }

        [HttpPut("rooms/{roomId}/interactionby/me/cheer")]
        public async Task<IActionResult> CheerRoom(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            if (!PlayerDB.HasCheeredRoom((long)id, roomId))
            {
                PlayerDB.AddCheerRoom((long)id, roomId);
                RoomDB.IncrementRoomCheerCount(roomId);
            }

            await NotiController.SendRoomUpdate((long)id, RoomDB.GetRoom(roomId));

            return Ok(new { success = true, error = "", value = new { Cheered = true } });
        }

        [HttpDelete("rooms/{roomId}/interactionby/me/cheer")]
        public async Task<IActionResult> UncheerRoom(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            if (PlayerDB.HasCheeredRoom((long)id, roomId))
            {
                PlayerDB.RemoveCheerRoom((long)id, roomId);
                RoomDB.DecrementRoomCheerCount(roomId);
            }

            await NotiController.SendRoomUpdate((long)id, RoomDB.GetRoom(roomId));

            return Ok(new { success = true, error = "", value = new { Cheered = false } });
        }

		[HttpPut("rooms/{roomId}/subrooms/{subRoomId}/permissions")]
		public async Task<IActionResult> SetSubroomPermissions(long roomId, long subRoomId, [FromBody] List<RoomDBClasses.PermissionEntry> permissions)
		{
				var playerId = AuthStuff.GetPlayerId(Request);
				if (playerId == null)
						return Unauthorized("");
                        
				var room = RoomDB.GetRoom(roomId);
				if (room == null)
						return NotFound(new { success = false, error = "Subroom was not found", error_id = "Rooms.Subroom.NotFound" });
                        
				if (!RoomDB.UserCanEditRoom(roomId, (long)playerId))
						return Forbid();
				var subroom = room.SubRooms?.FirstOrDefault(x => x.SubRoomId == subRoomId);
				if (subroom == null)
						return NotFound(new { success = false, error = "Subroom was not found", error_id = "Rooms.Subroom.NotFound" });

				var storedEntries = permissions.Select(p => new PhotonAccessTokenDBClasses.StoredPermissionEntry
				{
						Permission = p.Permission,
						Role = (int)p.Role,
						Type = p.Type,
						Override = p.Override,
						Value = p.Value
				}).ToList();

				var updatedSet = PhotonAccessTokenDB.SetPermissions(roomId, subRoomId, storedEntries);

				var fullPermissions = updatedSet.Permissions.Select(p => new
				{
						Override = p.Override,
						Permission = p.Permission,
						Role = p.Role,
						Type = p.Type,
						Value = p.Value
				}).ToList();

				var callerHeartbeat = PlayerDB.GetPlayerHeartbeat((long)playerId);
				var instanceId = callerHeartbeat?.roomInstance?.roomInstanceId;
				var playersInInstance = instanceId != null
						? PlayerDB.Players.FindAll()
								.Where(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == instanceId)
								.Select(p => p.PlayerId)
								.ToList()
						: new List<long> { (long)playerId };

				foreach (var pid in playersInInstance)
				{
						PlayerDB.SetPlayerPermissions(pid, permissions);
						await NotificationsController.SendEvent(pid, "PhotonAccessToken", new
						{
								RoomId = roomId,
								SubRoomId = subRoomId,
								Permissions = fullPermissions
						});
				}

				var updatedRoom = RoomDB.GetRoom(roomId);

				return Ok(new
				{
						value = updatedRoom,
						success = true,
						error_id = (string?)null,
						error = (string?)null
				});
		}

		[HttpPost("rooms/{roomId}/clone")]
		public async Task<IActionResult> CloneRoom(long roomId, [FromForm] CloneRoomRequest body)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");
			Console.WriteLine($"clone {id}");
			var query = RoomDB.Rooms.FindAll()
				.Where(r =>
					!IsBaseRoom(r) &&
					(
						r.CreatorAccountId == (long)id ||
						(r.Roles?.Any(x =>
							x.AccountId == (long)id &&
							(x.Role == Role.CoOwner ||
							 x.Role == Role.Creator)) ?? false)
					));
			var roomCount = query.Count();
			if (roomCount > 50)
				return RoomError("Rooms.AccountOwnsTooManyRooms", "Account cannot create or co-own any more rooms");
            //if ()
			//	return RoomError("Rooms.AccountIsNotAboveAge", "Junior accounts are prohibited from creating rooms.");
			if (body == null || string.IsNullOrWhiteSpace(body.Name))
				return RoomError("Rooms.MissingName", "You must enter a name for your room.");
			if (body.Name.Length < 3 || body.Name.Length > 128)
				return RoomError("Rooms.TooShortOrLong", "Room Name must be within 3 - 128 Characters.");
			if (await ContainsProhibitedText(body.Name))
				return RoomError("Rooms.ContainsProhibitedText", "Name contains prohibited text!");
			long playerId = (long)id;
			var now = DateTime.UtcNow;
			var twoMinutesAgo = now.AddMinutes(-2);
			var cloneHistory = _cloneRateLimits.GetOrAdd(playerId, _ => new List<DateTime>());
			lock (cloneHistory)
			{
				cloneHistory.RemoveAll(timestamp => timestamp < twoMinutesAgo);
				if (cloneHistory.Count() >= 10)
				{
					return RoomError("Rooms.TooManyClonesRateLimited", "An error occured. Wait a few minutes and try again.");
				}
				cloneHistory.Add(now);
			}
			var originalRoom = RoomDB.GetRoom(roomId);
			if (originalRoom == null)
				return RoomError("Rooms.CannotClone", "The room you are trying to clone does not exist.");
			bool isBaseRoom = originalRoom.Tags != null &&
				originalRoom.Tags.Any(t => string.Equals(t.Tag, "base", StringComparison.OrdinalIgnoreCase));
			if (RoomDB.GetRoomByName(body.Name) != null)
				return RoomError("Rooms.DuplicateName", "A room with that name already exists!");
			if (!isBaseRoom)
			{
				var playerRoomCount = RoomDB.GetRoomsByCreator(playerId).Count;
				if (playerRoomCount >= 101)
					return RoomError("Rooms.AccountOwnsTooManyRooms", "Account cannot create or co-own any more rooms");
			}
			var newId = RoomDB.CloneRoom(roomId, body.Name, playerId);
			if (newId == null)
				return RoomError("Rooms.CannotClone", "You can't clone this room!");
			var newRoom = RoomDB.GetRoom(newId.Value);
			var playerRecord = PlayerDB.Players.FindById(playerId);
			bool isDeveloper = playerRecord?.PlayerRoles?.Contains(PlayerDBClasses.PlayerRoles.Developer) ?? false;
			if (isDeveloper && newRoom != null)
			{
				newRoom.IsDeveloperOwned = true;
				RoomDB.Rooms.Update(newRoom);
			}
            if (AuthStuff.GetAndEnsureAppVersion(Request) == "20250724" && newRoom != null)
			{
				if (!newRoom.Tags.Any(x => x.Tag.Equals("2025", StringComparison.OrdinalIgnoreCase)))
                        newRoom.Tags.Add(new Tags { Tag = "2025", Type = TagType.AGOnly });
				RoomDB.Rooms.Update(newRoom);
			}
			var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
			foreach (var pid in allPlayers)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));
			return Ok(new { success = true, error = "", value = newRoom });
		}

        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/clone")]
        public async Task<IActionResult> CloneSubroom(long roomId, long subRoomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return RoomError("Rooms.DoesntExist", "This room does not exist!");
            if (!CanEdit(room, (long)id))
                return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");
            if (RoomDB.GetPlayerDormRoom((long)id)?.RoomId == roomId)
                return RoomError("Rooms.Subroom.CloneDormRoom", "You cannot clone subrooms from your dorm room!");
            var cloned = RoomDB.CloneSubroom(roomId, subRoomId, (long)id);
            if (cloned == null)
                return RoomError("Rooms.SubRoom.DoesntExist", "This subroom does not exist!");
            var updatedRoom = RoomDB.GetRoom(roomId);
            var allPlayers = PlayerDB.Players
                .FindAll()
                .Select(p => p.PlayerId)
                .ToList();
            foreach (var pid in allPlayers)
                await NotiController.SendRoomUpdate(pid, updatedRoom);
            return Ok(new
            {
                value = updatedRoom,
                success = true,
                error_id = (string?)null,
                error = (string?)null
            });
        }

        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/move")]
        public IActionResult MoveSubRoom(
                long roomId,
                long subRoomId,
                [FromForm] long newRoomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
            if (RoomDB.GetPlayerDormRoom((long)id)?.RoomId == roomId)
                return RoomError("Rooms.Subroom.MoveDormRoom", "You cannot move your dorm room!");
            var movedRoom = RoomDB.MoveSubRoomToRoom(
                roomId,
                subRoomId,
                newRoomId,
                (long)id);
            if (movedRoom == null)
                return RoomError("Subroom.MoveFailed", "Failed to move subroom.");
            return Ok(new
            {
                success = true,
                error = "",
                value = movedRoom
            });
        }

        [HttpDelete("rooms/{roomId}")]
        public async Task<IActionResult> DeleteRoom(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");

            if (!CanEdit(room, (long)id))
                return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

            RoomDB.DeleteRoom(roomId);
            return Ok(new { success = true, error = "", value = (object?)null });
        }

        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/accessibility")]
        public async Task<IActionResult> SetSubRoomAccessibility(long roomId, long subRoomId, [FromForm] string accessibility)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
            if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

            var sub = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
            if (sub == null) return RoomError("Rooms.SubRoom.DoesntExist", "This subroom does not exist!");

            sub.Accessibility = accessibility?.ToLower() switch
            {
                "public" => RoomAccessibility.Public,
                "private" => RoomAccessibility.Private,
                "unlisted" => RoomAccessibility.Unlisted,
                _ => RoomAccessibility.Private
            };

            RoomDB.Rooms.Update(room);

            var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
            foreach (var pid in allPlayers)
                await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

            return Ok(new { success = true });
        }

        [HttpPut("rooms/{roomId}/name")]
		public async Task<IActionResult> SetRoomName(long roomId, [FromForm] string name)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			var room = RoomDB.GetRoom(roomId);
			if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
			if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");
			if (room.IsDorm) return RoomError("Rooms.Dorm", "You cannot edit your dorm room's name!");
			if (string.IsNullOrWhiteSpace(name)) return RoomError("Rooms.InvalidName", "Name cannot be empty.");
			if (name.Length < 3 || name.Length > 128) return RoomError("Rooms.TooShortOrLong", "Room Name must be within 3 - 128 Characters.");
			if (name.Contains(" ")) return RoomError("Rooms.NoSpaces", "Your room name must not contain spaces!");
			if (await ContainsProhibitedText(name))
				return RoomError("Rooms.ContainsProhibitedText", "Name contains prohibited text!");

			var updated = RoomDB.SetRoomName(roomId, name);
			if (updated == null)
				return RoomError("Rooms.DuplicateName", "A room with the same name already exists!");

			var updatedRoom = RoomDB.GetRoom(roomId);
			var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
			foreach (var pid in allPlayers)
				await NotiController.SendRoomUpdate(pid, updatedRoom);

			return Ok(new { success = true, error = "", value = updatedRoom });
		}

        [HttpPut("rooms/{roomId}/description")]
        public async Task<IActionResult> SetRoomDescription(long roomId, [FromForm] string description)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
            if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");
            if (!string.IsNullOrWhiteSpace(description) && await ContainsProhibitedText(description))
                return RoomError("Rooms.ContainsProhibitedText", "Description must not contain prohibited text.");

            RoomDB.SetRoomDescription(roomId, description ?? "");

            var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
            foreach (var pid in allPlayers)
                await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

            return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
        }

        [HttpPut("rooms/{roomId}/image")]
        public async Task<IActionResult> SetRoomImage(long roomId, [FromForm] string imageName)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
            if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

            RoomDB.SetRoomImageName(roomId, imageName);

            var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
            foreach (var pid in allPlayers)
                await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

            return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
        }

        [HttpPut("rooms/{roomId}/accessibility")]
		public async Task<IActionResult> SetRoomAccessibility(long roomId, [FromForm] int accessibility)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			if (!Enum.IsDefined(typeof(RoomAccessibility), accessibility))
				return BadRequest();

			var room = RoomDB.GetRoom(roomId);
			if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
			if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

			if ((RoomAccessibility)accessibility == RoomAccessibility.Public && room.ImageName == "DefaultRoomImage.png")
				return RoomError("Rooms.MissingRoomThumbnail", "Cannot publish room: Missing room photo"); // wowe just like ingame

			RoomDB.SetRoomAccessibility(roomId, (RoomAccessibility)accessibility);

			var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
			foreach (var pid in allPlayers)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

			return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
		}

        [HttpPut("rooms/{roomId}/cloning")]
        public async Task<IActionResult> SetRoomCloning(long roomId, [FromForm] bool cloningAllowed)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
            if (room.IsDorm) return RoomError("Rooms.DormNotAllowed", "You cannot edit that in this dorm.");
            if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

            RoomDB.SetRoomCloning(roomId, cloningAllowed);

            var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
            foreach (var pid in allPlayers)
                await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));


            return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
        }

        [HttpPut("rooms/{roomId}/restrictions")]
        public async Task<IActionResult> SetRoomRestrictions(
            long roomId,
            [FromForm] bool supportsScreens,
            [FromForm] bool supportsWalkVR,
            [FromForm] bool supportsTeleportVR,
            [FromForm] bool supportsJuniors)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
            if (room.IsDorm) return RoomError("Rooms.DormNotAllowed", "You cannot edit that in dorms.");
            if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

            RoomDB.SetRoomRestrictions(roomId, supportsScreens, supportsWalkVR, supportsTeleportVR, supportsJuniors);

            var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
            foreach (var pid in allPlayers)
                await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));


            return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
        }

        [HttpPut("rooms/{roomId}/loadscreen")]
        public async Task<IActionResult> SetRoomLoadscreen(
            long roomId,
            [FromForm] string? imageName,
            [FromForm] string? title,
            [FromForm] string? subtitle)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
            if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

            if (!string.IsNullOrEmpty(title) && title.Length > 24)
                return RoomError("Room.LoadScreen.Title", "Title cannot exceed 24 characters!");
            if (!string.IsNullOrEmpty(subtitle) && subtitle.Length > 150)
                return RoomError("Room.LoadScreen.Subtitle", "Subtitle cannot exceed 150 characters!");

            RoomDB.SetRoomLoadscreen(roomId, imageName, title, subtitle);

            var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
            foreach (var pid in allPlayers)
                await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

            return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
        }

        [HttpPut("rooms/{roomId}/warning")]
		public async Task<IActionResult> SetRoomWarning(long roomId, [FromForm] WarningMaskType warningMask, [FromForm] string? customWarning = null)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");
            
			var room = RoomDB.GetRoom(roomId);
            
			if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
			if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");
            
			RoomDB.SetRoomWarningMask(roomId, warningMask);
            
			if (warningMask == (WarningMaskType)29)
				RoomDB.SetRoomCustomWarning(roomId, null);
                
			else if (customWarning != null)
				RoomDB.SetRoomCustomWarning(roomId, customWarning);
                
			var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
            
			foreach (var pid in allPlayers)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));
                
			return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
		}
        
        [HttpPut("rooms/{roomId}/tags")]
		public async Task<IActionResult> SetRoomTags(long roomId)
		{
				var account = AuthStuff.GetPlayerId(Request);
				if (account == null) return Unauthorized("");
				var room = RoomDB.GetRoom(roomId);
				if (room == null)
						return Vanadium.Utils.Utils.ErrorRoom("Rooms.DoesntExist", "This room does not exist!");
				if (!RoomDB.UserCanEditRoom(roomId, account.Value))
						return Vanadium.Utils.Utils.ErrorRoom("Rooms.PermissionDenied", "You are not the owner of this room!");

				var restrictedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
				{
						"rro", "recroomoriginal", "featured",
						"janrrmonthly", "febrrmonthly", "marchrrmonthly",
						"aprilrrmonthly", "mayrrmonthly", "junerrmonthly",
						"julyrrmonthly", "augrrmonthly", "seprrmonthly",
						"octrrmonthly", "novrrmonthly", "decrrmonthly"
				};

				var newTags = new List<RoomDBClasses.Tags>();
				var ct = Request.ContentType ?? "";
				bool hasForm = ct.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
						|| ct.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase);
				if (hasForm)
				{
						var autoTagKeys = Request.Form.Keys
								.Where(k => k.Equals("autotag", StringComparison.OrdinalIgnoreCase))
								.ToList();
						foreach (var key in autoTagKeys)
						{
								foreach (var val in Request.Form[key])
								{
										var t = (val ?? "").Trim().ToLowerInvariant();
										if (!string.IsNullOrWhiteSpace(t))
										{
												if (restrictedTags.Contains(t))
														return Vanadium.Utils.Utils.ErrorRoom("Rooms.InvalidTags", "You cannot use this room tag.");
												newTags.Add(new RoomDBClasses.Tags { Tag = t, Type = RoomDBClasses.TagType.Auto });
										}
								}
						}
						var tagKeys = Request.Form.Keys
								.Where(k => k.Equals("tag", StringComparison.OrdinalIgnoreCase))
								.ToList();
						foreach (var key in tagKeys)
						{
								foreach (var val in Request.Form[key])
								{
										var t = (val ?? "").Trim().ToLowerInvariant();
										if (!string.IsNullOrWhiteSpace(t))
										{
												if (restrictedTags.Contains(t))
														return Vanadium.Utils.Utils.ErrorRoom("Rooms.InvalidTags", "Permission denied.");
												newTags.Add(new RoomDBClasses.Tags { Tag = t, Type = RoomDBClasses.TagType.General });
										}
								}
						}
				}
				room.Tags = newTags;
				RoomDB.Rooms.Update(room);
				var updatedRoom = RoomDB.GetRoom(roomId);
				await NotiController.SendToAll(Newtonsoft.Json.JsonConvert.SerializeObject(new WebsocketEvents.Response
				{
						Id = NotiController.EventTypes.RoomUpdate,
						Msg = updatedRoom
				}));
				return Ok(new { success = true, error = "", value = updatedRoom });
		}

        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/data")]
        public async Task<IActionResult> PostSubRoomData(long roomId, long subRoomId, [FromBody] RoomDataRequest request)
        {
            try
            {
                var playerId = AuthStuff.GetPlayerId(Request);

                if (playerId == null)
                    return Unauthorized("");

                var room = RoomDB.GetRoom(roomId);

                if (room == null)
                    return Ok(new { success = false, error_id = "Rooms.DoesntExist", error = "Room not found", value = (object?)null });

                if (!CanEdit(room, (long)playerId))
                    return Ok(new { success = false, error_id = "Rooms.PermissionDenied", error = "You are not the owner of this room!", value = (object?)null });

                if (AuthStuff.GetAndEnsureAppVersion(Request) == "20250724" && !room.Tags.Any(x => x.Tag.Equals("2025", StringComparison.OrdinalIgnoreCase)))
                {
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                Id = "2",
                Msg = new
                {
                    FromPlayerId = 1,
                    Id = new Random().Next(1, 0x7ffffff),
                    SentTime = DateTime.UtcNow,
                    Type = 100,
                    Data = "You can't save 2023 rooms on 2025. Clone the room or go back to 2023.",
                    RoomId = 1
                }
            });
            await NotificationsController.SendToPlayer((long)playerId, json);
                    return Ok(new { success = false, error_id = "Rooms.UpdateRequired", error = "You can't save 2023 rooms on a 2025 version!", value = (object?)null });
                }

                var subRoom = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);

                if (subRoom == null)
                    return Ok(new { success = false, error_id = "Rooms.SubRoom.DoesntExist", error = "Subroom not found", value = (object?)null });

                string dataBlob = request.SubRoomData?.Filename ?? "";
                string dataBlobHash = request.SubRoomData?.Hash ?? "";
                string description = request.Description ?? "";

                int platform = 0;
                int deviceClass = 5;

                var authHeader = Request.Headers["Authorization"].FirstOrDefault();

                if (!string.IsNullOrEmpty(authHeader))
                {
                    try
                    {
                        var tokenStr = authHeader
                            .Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
                            .Trim();

                        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                        var jwt = handler.ReadJwtToken(tokenStr);

                        var platformClaim = jwt.Claims.FirstOrDefault(c => c.Type == "platform")?.Value;
                        if (platformClaim != null)
                            platform = int.Parse(platformClaim);
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(request.RoomData?.Filename))
                    room.DataBlob = request.RoomData.Filename;

                room.PersistenceVersion = request.PersistenceVersion;

                var save = new currentSave
                {
                    SubRoomDataSaveId = RoomDB.GetNextSubRoomSaveId(),
                    RoomId = roomId,
                    SubRoomId = subRoomId,
                    DataBlob = dataBlob,
                    DataBlobHash = dataBlobHash,
                    PersistenceVersion = request.PersistenceVersion,
                    UgcSubVersion = request.PersistenceVersion,
                    OMVersion = 0,
                    SavedByAccountId = (long)playerId,
                    SavedOnPlatform = platform,
                    SavedOnDeviceClass = deviceClass,
                    Description = description,
                    Tags = new List<string>(),
                    ModerationState = 0,
                    CreatedAt = DateTime.UtcNow
                };

                RoomDB.SubRoomSaves.Insert(save);

                //subRoom.DataBlob = dataBlob;
                subRoom.SavedByAccountId = (long)playerId;

                if (request.AutoPublish && !string.IsNullOrEmpty(dataBlob))
                {
                    subRoom.DataBlob = dataBlob;
                    subRoom.CurrentSave = save;
                }

                RoomDB.Rooms.Update(room);

                var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
                foreach (var pid in allPlayers)
                    await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

                var updatedRoom = RoomDB.GetRoom(roomId);

                var subRoomsProjected = updatedRoom.SubRooms?.Select(sub => new
                {
                    sub.SubRoomId,
                    sub.RoomId,
                    CreatorAccountId = sub.CreatorAccountId,
                    sub.UnitySceneId,
                    sub.Name,
                    CurrentSave = (sub.CurrentSave != null ? new
                        {
                            sub.CurrentSave.SubRoomDataSaveId,
                            sub.CurrentSave.SubRoomId,
                            sub.CurrentSave.RoomId,
                            DataBlob = sub.CurrentSave.DataBlob,
                            sub.CurrentSave.SavedByAccountId,
                            sub.CurrentSave.SavedOnPlatform,
                            sub.CurrentSave.SavedOnDeviceClass,
                            sub.CurrentSave.Description,
                            sub.CurrentSave.CreatedAt,
                            sub.CurrentSave.UnityAssetId
                        } : RoomDB.SubRoomSaves
                        .Find(x => x.RoomId == room.RoomId && x.SubRoomId == sub.SubRoomId)
                        .OrderByDescending(x => x.CreatedAt)
                        .Select(save => new
                        {
                            save.SubRoomDataSaveId,
                            save.SubRoomId,
                            save.RoomId,
                            DataBlob = save.DataBlob,
                            save.SavedByAccountId,
                            save.SavedOnPlatform,
                            save.SavedOnDeviceClass,
                            save.Description,
                            save.CreatedAt,
                            save.UnityAssetId
                        })
                        .FirstOrDefault()),
                    sub.LastModeratedSaveModerationState,
                    sub.IsSandbox,
                    sub.MaxPlayers,
                    sub.Accessibility,
                    sub.ShouldAutoStageSaves,
                    sub.StagedSubRoomDataSaveId
                }).ToList();

                return Ok(new
                {
                    value = new
                    {
                        Room = new
                        {
                            updatedRoom.RoomId,
                            updatedRoom.Name,
                            updatedRoom.Description,
                            updatedRoom.ImageName,
                            updatedRoom.WarningMask,
                            updatedRoom.CustomWarning,
                            updatedRoom.CreatorAccountId,
                            updatedRoom.State,
                            updatedRoom.Accessibility,
                            PublishState = 0,
                            updatedRoom.SupportsLevelVoting,
                            updatedRoom.IsRRO,
                            updatedRoom.IsRecRoomApproved,
                            updatedRoom.ExcludeFromLists,
                            updatedRoom.ExcludeFromSearch,
                            updatedRoom.SupportsScreens,
                            updatedRoom.SupportsWalkVR,
                            updatedRoom.SupportsTeleportVR,
                            updatedRoom.SupportsVRLow,
                            updatedRoom.SupportsQuest2,
                            updatedRoom.SupportsMobile,
                            updatedRoom.SupportsJuniors,
                            updatedRoom.MinLevel,
                            updatedRoom.AgeRating,
                            updatedRoom.CreatedAt,
                            updatedRoom.PublishedAt,
                            updatedRoom.BecameRRStudioRoomAt,
                            updatedRoom.Stats,
                            updatedRoom.RankingContext,
                            updatedRoom.IsDorm,
                            updatedRoom.IsPlacePlay,
                            updatedRoom.MaxPlayerCalculationMode,
                            updatedRoom.MaxPlayers,
                            updatedRoom.CloningAllowed,
                            updatedRoom.DisableMicAutoMute,
                            updatedRoom.DisableRoomComments,
                            updatedRoom.EncryptVoiceChat,
                            updatedRoom.ToxmodEnabled,
                            updatedRoom.LoadScreenLocked,
                            updatedRoom.UgcVersion,
                            updatedRoom.PersistenceVersion,
                            updatedRoom.UgcSubVersion,
                            updatedRoom.MinUgcSubVersion,
                            updatedRoom.AutoLocalizeRoom,
                            updatedRoom.LocalizationContext,
                            updatedRoom.IsDeveloperOwned,
                            updatedRoom.RankedEntityId,
                            BoostCount = 0,
                            updatedRoom.DataBlob,
                            DataBlobHash = request.RoomData?.Hash,
                            updatedRoom.CurrentSnapshotId,
                            LatestSnapshotId = updatedRoom.CurrentSnapshotId,
                            StagedSnapshotId = updatedRoom.CurrentSnapshotId,
                            SubRooms = subRoomsProjected,
                            updatedRoom.Roles,
                            updatedRoom.IsJuniorCreated,
                            updatedRoom.Tags,
                            updatedRoom.PromoImages,
                            updatedRoom.PromoExternalContent,
                            updatedRoom.LoadScreens,
                            CurrentRoomModerationState = 0,
                            LastRoomModerationState = 0,
                            PublishStateAvailability = new
                            {
                                CanSaveAsBeta = false,
                                CanSaveAsUpdate = true,
                                AvailableUpdateTokenCount = 3
                            },
                            updatedRoom.RestrictedCircuitsAllowListNames
                        },
                        SubRoomDataSave = new
                        {
                            save.SubRoomDataSaveId,
                            save.SubRoomId,
                            save.DataBlob,
                            save.DataBlobHash,
                            save.ReferencedUnityAssetIds,
                            save.PersistenceVersion,
                            save.OMVersion,
                            save.UgcSubVersion,
                            save.SavedByAccountId,
                            save.SavedOnPlatform,
                            save.SavedOnDeviceClass,
                            save.Description,
                            save.Tags,
                            save.ModerationState,
                            save.CreatedAt
                        }
                    },
                    success = true,
                    error_id = (string?)null,
                    error = (string?)null
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    error_id = "Internal.Error",
                    error = ex.Message,
                    value = (object?)null
                });
            }
        }

        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/publish_save")]
        public async Task<IActionResult> RestoreSubRoomData(long roomId, long subRoomId, [FromForm] long subRoomDataSaveId)
        {
            try
            {
                var playerId = AuthStuff.GetPlayerId(Request);

                if (playerId == null)
                    return Unauthorized("");

                var room = RoomDB.GetRoom(roomId);

                if (room == null)
                    return Ok(new { success = false, error_id = "Rooms.DoesntExist", error = "Room not found", value = (object?)null });

                if (!CanEdit(room, (long)playerId))
                    return Ok(new { success = false, error_id = "Rooms.PermissionDenied", error = "You are not the owner of this room!", value = (object?)null });

                var subRoom = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);

                if (subRoom == null)
                    return Ok(new { success = false, error_id = "Rooms.SubRoom.DoesntExist", error = "Subroom not found", value = (object?)null });

                var save = RoomDB.SubRoomSaves.Find(x => x.RoomId == (long)roomId && x.SubRoomId == (long)subRoomId && x.SubRoomDataSaveId == (long)subRoomDataSaveId).FirstOrDefault();

                subRoom.DataBlob = save.DataBlob;
                subRoom.CurrentSave = save;

                RoomDB.Rooms.Update(room);

                var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
                foreach (var pid in allPlayers)
                    await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

                var updatedRoom = RoomDB.GetRoom(roomId);

                var subRoomsProjected = updatedRoom.SubRooms?.Select(sub => new
                {
                    sub.SubRoomId,
                    sub.RoomId,
                    CreatorAccountId = sub.CreatorAccountId,
                    sub.UnitySceneId,
                    sub.Name,
                    CurrentSave = RoomDB.SubRoomSaves
                        .Find(x => x.RoomId == updatedRoom.RoomId && x.SubRoomId == sub.SubRoomId)
                        .OrderByDescending(x => x.CreatedAt)
                        .Select(s => new
                        {
                            s.SubRoomDataSaveId,
                            s.SubRoomId,
                            s.DataBlob,
                            s.DataBlobHash,
                            s.ReferencedUnityAssetIds,
                            s.PersistenceVersion,
                            s.OMVersion,
                            s.UgcSubVersion,
                            s.SavedByAccountId,
                            s.SavedOnPlatform,
                            s.SavedOnDeviceClass,
                            s.Description,
                            s.Tags,
                            s.ModerationState,
                            s.CreatedAt
                        })
                        .FirstOrDefault(),
                    sub.LastModeratedSaveModerationState,
                    sub.IsSandbox,
                    sub.MaxPlayers,
                    sub.Accessibility,
                    sub.ShouldAutoStageSaves,
                    sub.StagedSubRoomDataSaveId
                }).ToList();

                return Ok(new
                {
                    success = true,
                    error_id = (string?)null,
                    error = (string?)null
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    error_id = "Internal.Error",
                    error = ex.Message,
                    value = (object?)null
                });
            }
        }

        /*[HttpGet("rooms/{roomId}/subrooms/{subroomId}/saves")]
        public ContentResult GetRoomSaves(ulong roomId, ulong subroomId, int unityAssetTarget = 0, int unityAssetVersion = 1, int skip = 0, int take = 20)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            var room = RoomDB.GetRoom((long)roomId);
            if (room == null)
                return NotFound("");

            if (!CanViewSaves(room, (long)playerId))
                return Forbidden("");

            if (PlayerDB.Players.FindById((long)playerId)?.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false)
                take = 999;

            var save = RoomDB.GetRoomSaves(roomId, subroomId, skip, take);

            if (save == null) return Ok(ServerConfig.Bracket);

            return Ok(new { Results = save.Results, TotalResults = save.TotalCount} );
        }*/

        [HttpGet("rooms/magic_door")]
        public async Task<IActionResult> MagicDoor([FromQuery] int partySize = 1)
        {
            if (partySize <= 0)
                partySize = 1;

            var random = new Random();

            var col = RoomDB.RoomDBFile.GetCollection("Rooms");

            var publicRooms = new List<Room>();

            foreach (var doc in col.FindAll())
            {
                try
                {
                    var room = BsonMapper.Global.Deserialize<Room>(doc);

                    if (room == null)
                        continue;

                    if (room.IsDorm)
                        continue;

                    if (room.Accessibility != RoomAccessibility.Public)
                        continue;

                    if (room.SubRooms == null || room.SubRooms.Count == 0)
                        continue;

                    publicRooms.Add(room);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[-] Skipped corrupt room doc {doc["_id"]}: {ex.Message}");
                }
            }

            if (publicRooms.Count == 0)
                return RoomError("Rooms.NotFound", "No");

            var triedRoomIds = new HashSet<long>();

            for (int i = 0; i < 20; i++)
            {
                var remainingRooms = publicRooms
                    .Where(r => !triedRoomIds.Contains(r.RoomId))
                    .ToList();

                if (remainingRooms.Count == 0)
                    break;

                var room = remainingRooms[random.Next(remainingRooms.Count)];

                triedRoomIds.Add(room.RoomId);

                var publicSubrooms = room.SubRooms
                    .Where(s => s.Accessibility == RoomAccessibility.Public)
                    .ToList();

                if (publicSubrooms.Count == 0)
                    continue;

                SubRooms? targetSubroom = null;

                if (publicSubrooms.Count == 1)
                {
                    var onlySubroom = publicSubrooms[0];

                    if (onlySubroom.MaxPlayers < partySize)
                        continue;

                    targetSubroom = onlySubroom;
                }
                else
                {
                    var validSubrooms = publicSubrooms
                        .Where(s => s.MaxPlayers >= partySize)
                        .ToList();

                    if (validSubrooms.Count == 0)
                        continue;

                    targetSubroom = validSubrooms[random.Next(validSubrooms.Count)];
                }

                if (targetSubroom == null)
                    continue;

                return Ok(new { RefreshIntervalMinutes = 1, RefreshesAt = DateTime.UtcNow.AddMinutes(1), Room = room });
            }
            return BadRequest("");
        }

        [HttpGet("rooms/{roomId}/subrooms/{subRoomId}/saves/no_unity_assets")]
        public async Task<IActionResult> GetSubRoomSavesNoUnity(
            long roomId, long subRoomId,
            [FromQuery] int skip = 0, [FromQuery] int take = 20)
        {
            var (results, total) = RoomDB.GetSubRoomSaves(roomId, subRoomId, skip, take);
            return Ok(new { Results = results, TotalResults = total });
        }

        [HttpGet("rooms/{roomId}/subrooms/{subRoomId}/saves/{saveId}")]
        public async Task<IActionResult> GetSubRoomSaveById(long roomId, long subRoomId, long saveId)
        {
            var save = RoomDB.GetSubRoomSaveById(roomId, subRoomId, saveId);
            if (save == null) return NotFound("");
            return Ok(save);
        }

        [HttpPost("rooms/{roomId}/subrooms")]
        public async Task<IActionResult> CreateSubroom(long roomId, [FromForm] string name)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
            if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");
            if (room.IsDorm) return RoomError("Rooms.PermissionDenied", "You cannot create a subroom for your dorm room!");
            if (string.IsNullOrWhiteSpace(name)) return RoomError("Rooms.InvalidName", "Subroom name cannot be empty.");

            var updated = RoomDB.CreateSubroomForRoom(roomId, name);

            var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
            foreach (var pid in allPlayers)
                await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

            return Ok(new { success = true, error = "", value = updated });
        }

        [HttpDelete("rooms/{roomId}/subrooms/{subRoomId}")]
		public async Task<IActionResult> DeleteSubroom(long roomId, long subRoomId)
		{
				var id = AuthStuff.GetPlayerId(Request);
				if (id == null) return Unauthorized("");

				var room = RoomDB.GetRoom(roomId);
				if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
				if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

				RoomDB.DeleteSubroom(roomId, subRoomId);

				var updatedRoom = RoomDB.GetRoom(roomId);
				var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
				foreach (var pid in allPlayers)
						await NotiController.SendRoomUpdate(pid, updatedRoom);

				return Ok(new { success = true, error = "", value = updatedRoom });
		}

        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/modify")]
        public async Task<IActionResult> ModifySubroom(
            long roomId, long subRoomId,
            [FromForm] string name,
            [FromForm] RoomAccessibility accessibility,
            [FromForm] int maxPlayers = 32)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
            if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

            RoomDB.ModifySubroom(roomId, subRoomId, name, accessibility, maxPlayers);

            var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
            foreach (var pid in allPlayers)
                await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

            return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
        }

        [HttpGet("photon_access_token")]
		public async Task<IActionResult> GetPhotonAccessToken()
		{
				var id = AuthStuff.GetPlayerId(Request);
				if (id == null) return Unauthorized("");

				var heartbeat = PlayerDB.GetPlayerHeartbeat((long)id);
				var roomInstance = heartbeat?.roomInstance;

				List<object> permissions = new List<object>();

				if (roomInstance != null)
				{
						var stored = PhotonAccessTokenDB.GetPermissions(roomInstance.roomId, roomInstance.subRoomId);
						if (stored != null)
						{
								permissions = stored.Permissions.Select(p => (object)new
								{
										Override = p.Override,
										Permission = p.Permission,
										Role = p.Role,
										Type = p.Type,
										Value = p.Value
								}).ToList();
						}
				}

				return Ok(new
				{
						PhotonAccessToken = "",
						RoomInstanceId = roomInstance?.roomInstanceId,
						Permissions = permissions.ToArray()
				});
		}

        [HttpGet("/roomserver/rooms/{elroom}/similar")]
        public async Task<IActionResult> SimilarRooms(long elroom)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return Unauthorized("");
            var target = RoomDB.GetRoom(elroom);
            if (target == null) return NotFound("");
            var allRooms = RoomDB.Rooms.FindAll().Where(r => !IsBaseRoom(r)).ToList();
            var results = allRooms
                .Where(r => r.RoomId != elroom && (int)r.Accessibility != 0)
                .Select(r =>
                {
                    double score = 0;
                    var aTags = target.Tags?.Select(t => t.Tag.ToLower()).ToHashSet() ?? new HashSet<string>();
                    var bTags = r.Tags?.Select(t => t.Tag.ToLower()).ToHashSet() ?? new HashSet<string>();
                    int tagMatches = aTags.Intersect(bTags).Count();
                    int totalTags = aTags.Union(bTags).Count();
                    if (totalTags > 0)
                        score += (double)tagMatches / totalTags * 40;
                    if (!string.IsNullOrEmpty(target.Name) && !string.IsNullOrEmpty(r.Name))
                    {
                        var aWords = target.Name.ToLower().Split(' ');
                        var bWords = r.Name.ToLower().Split(' ');
                        int wordMatches = aWords.AsEnumerable().Intersect(bWords).Count();
                        int totalWords = aWords.AsEnumerable().Union(bWords).Count();
                        if (totalWords > 0)
                            score += (double)wordMatches / totalWords * 20;
                    }
                    if (!string.IsNullOrEmpty(target.Description) && !string.IsNullOrEmpty(r.Description))
                    {
                        if (r.Description.ToLower().Contains(target.Name?.ToLower() ?? string.Empty))
                            score += 5;
                    }
                    if (target.Stats != null && r.Stats != null)
                    {
                        if (!(target.Stats.VisitCount == 0 && r.Stats.VisitCount == 0))
                        {
                            double diff = Math.Abs(target.Stats.VisitCount - r.Stats.VisitCount);
                            double max = Math.Max(target.Stats.VisitCount, r.Stats.VisitCount);
                            if (max > 0)
                                score += (1 - diff / max) * 10;
                        }
                        if (!(target.Stats.FavoriteCount == 0 && r.Stats.FavoriteCount == 0))
                        {
                            double diff = Math.Abs(target.Stats.FavoriteCount - r.Stats.FavoriteCount);
                            double max = Math.Max(target.Stats.FavoriteCount, r.Stats.FavoriteCount);
                            if (max > 0)
                                score += (1 - diff / max) * 10;
                        }
                        if (!(target.Stats.CheerCount == 0 && r.Stats.CheerCount == 0))
                        {
                            double diff = Math.Abs(target.Stats.CheerCount - r.Stats.CheerCount);
                            double max = Math.Max(target.Stats.CheerCount, r.Stats.CheerCount);
                            if (max > 0)
                                score += (1 - diff / max) * 10;
                        }
                    }
                    if (target.MaxPlayers == r.MaxPlayers)
                        score += 5;
                    double daysDiff = Math.Abs((target.CreatedAt - r.CreatedAt).TotalDays);
                    score += Math.Max(0, 5 - daysDiff / 30);
                    if (target.Accessibility == r.Accessibility)
                        score += 5;
                    return new { Room = r, Score = score };
                })
                .OrderByDescending(x => x.Score)
                .Take(50)
                .Select(x => x.Room)
                .ToList();

            return Ok(new { rooms = results });
        }

        [HttpGet("/roomserver/rooms/{roomId}/experience/player")]
        public async Task<IActionResult> GetPlayerRoomExperience(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            return Ok(new { Enabled = false, Experience = (object?)null, ConcurrencyCode = (string?)null });
        }

        [HttpGet("/roomserver/rooms/{roomId}/bans/{playerId}/isBanned")]
        public async Task<IActionResult> GetIsBanned(long roomId, long playerId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            return Ok(new { success = true, error = (string?)null, error_id = (string?)null, value = false });
        }

        [HttpGet("/roomserver/rooms/{roomId}/assets")]
        public async Task<IActionResult> GetRoomAssets(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            return Ok(new { success = true, error = (string?)null, error_id = (string?)null, value = Array.Empty<object>() });
        }

        [HttpGet("/roomserver/rooms/{roomId}/snapshots/{snapshotId}/loadinfo")]
        public async Task<IActionResult> GetSnapshotLoadInfo(long roomId, string snapshotId, [FromQuery] long subRoomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return NotFound("");

            var subRoom = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
            var latestSave = RoomDB.GetSubRoomSaves(roomId, subRoomId, 0, 1).Results.FirstOrDefault();

            return Ok(new
            {
                success = true,
                error = (string?)null,
                error_id = (string?)null,
                value = new
                {
                    Assets = Array.Empty<object>(),
                    RoomId = roomId,
                    SnapshotId = snapshotId,
                    SubRoom = new { SubRoomDataSaveId = latestSave?.SubRoomDataSaveId },
                    SubRoomId = subRoomId,
                    UgcSubVersion = room.UgcSubVersion,
                    UgcVersion = room.UgcVersion
                }
            });
        }

        [HttpGet("/roomserver/publishState/configs")]
        public async Task<IActionResult> GetPublishStateConfigs()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            return Ok(new
            {
                value = new
                {
                    UpdateMaxCount = 3,
                    UpdateRollingWindowInDays = 365,
                    UpdateExpirationInDays = 30,
                    UpdateCooldownInDays = 45
                },
                success = true,
                error_id = (string?)null,
                error = (string?)null
            });
        }

        /*[HttpPost("/api/rooms/v1/verifyRole")]
        public async Task<IActionResult> VerifyRole([FromQuery] long roomId, [FromQuery] int role, [FromQuery] string? context)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return Ok(false);

            bool hasRole = room.Roles?.Any(r => r.AccountId == (long)id && (int)r.Role >= role) ?? false;
            return Ok(hasRole);
        }*/
        
        [HttpPost("/api/rooms/v1/verifyRole")]
        public async Task<IActionResult> VerifyRole([FromQuery] long roomId, [FromQuery] int role, [FromQuery] string? context)
        {
            return Ok(true); // temp
        }

        [HttpGet("/roomserver/rooms/{roomId}/playerdata/me")]
        public async Task<IActionResult> GetMyPlayerData(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            
            if ((long)id == 55)
            	return NotFound("");

            var data = PlayerDB.GetPlayerData((long)id, roomId);
            if (string.IsNullOrEmpty(data)) return Ok(new { Data = "" }); // modern did this, so i am too
            return Ok(new { Data = data });
        }

        [HttpPut("/roomserver/rooms/{roomId}/playerdata/me")]
        public async Task<IActionResult> SetMyPlayerData(long roomId, [FromForm] string data)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            //if (AuthStuff.GetAndEnsureAppVersion(Request) != "20230406") return NoContent();

            bool success = PlayerDB.SetPlayerData((long)id, roomId, data);
            if (!success) return BadRequest();
            return NoContent();
        }

        [HttpGet("/roomserver/rooms/{roomId}/boosts/{playerId}/count")]
        public async Task<IActionResult> GetBoostCount(long roomId, long playerId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            return Ok("0");
        }

        [HttpGet("/algorithmiclists/topengagementrooms")]
        public async Task<IActionResult> AlgorithmicListTop()
        {
            var featured = RoomDB.Rooms.FindAll()
				.Where(r =>
					r.Accessibility == RoomAccessibility.Public &&
					!r.IsDorm &&
					r.Tags != null &&
					r.Tags.Any(t => t.Tag.Equals("featured", StringComparison.OrdinalIgnoreCase)))
				.Select(r => new
				{
					Id = r.RoomId.ToString(),
					Context = "{\"algo_name\":\"Rooms_TopEngagement\",\"algo_version\":\"1\",\"user_segment\":\"11\",\"score\":1.0,\"is_shuffled\":false,\"is_rotated\":false}"
				})
				.ToList();
			return Ok(new
			{
                Type = 1,
                Entities = featured
			});
        }
        
        [HttpGet("/algorithmiclists/{thing}")]
        public async Task<IActionResult> AlgorithmicList()
        {
            string path = Path.Join(Program.dataDir, "APIS", "2025thing.json");
            return System.IO.File.Exists(path) ? Content(System.IO.File.ReadAllText(path), "application/json") : NotFound();
        }
        
        
        [HttpGet("rooms/carousel/rising")]
        public async Task<IActionResult> RisingRooms([FromQuery] string tag = "", [FromQuery] int skip = 0, [FromQuery] int take = 30)
        {
            string? passingTag = string.IsNullOrWhiteSpace(tag) ? null : tag;
            var (results, total) = RoomDB.GetHotRooms(passingTag, skip, take, true);
            return Ok(new
            {
                Results = results ?? new List<Room>(),
                TotalResults = total
            });
        }

		[HttpGet("featuredrooms/current")]
		public async Task<IActionResult> CurrentFeaturedRooms()
		{
			var featured = RoomDB.Rooms.FindAll()
				.Where(r =>
					r.Accessibility == RoomAccessibility.Public &&
					!r.IsDorm &&
					r.Tags != null &&
					r.Tags.Any(t => t.Tag.Equals("featured", StringComparison.OrdinalIgnoreCase)))
				.ToList();
			return Ok(new
			{
                FeaturedRoomGroupId = 300,
                Name = "Featured Rooms",
                StartAt = "2024-05-06T10:01:00Z",
                EndAt = "2024-05-13T10:00:00Z",
                Rooms = featured
			});
		}
        
        [HttpGet("rooms/curated_playlists")]
        public async Task<IActionResult> RoomsCurated()
        {
        	return Ok(ServerConfig.Bracket);
        }
        
        [HttpGet("rooms/recommendations")]
        public async Task<IActionResult> RoomsRecommended()
        {
        	return Ok(ServerConfig.Bracket);
        }

        [HttpGet("showcase/{accountId}")]
        public async Task<IActionResult> RoomShowcase(long accountId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            return Ok(new List<object>());
        }

		[HttpPut("rooms/{roomId}/voice_chat_encryption")]
		public async Task<IActionResult> SetVoiceChatEncryption(long roomId, [FromForm] bool encryptVoiceChat)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			var room = RoomDB.GetRoom(roomId);
			if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
			if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

			room.EncryptVoiceChat = encryptVoiceChat;
			RoomDB.Rooms.Update(room);

			var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
			foreach (var pid in allPlayers)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

			return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
		}

		[HttpPut("rooms/{roomId}/comments")]
		public async Task<IActionResult> SetRoomComments(long roomId, [FromForm] bool disable)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			var room = RoomDB.GetRoom(roomId);
			if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
			if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

			room.DisableRoomComments = disable;
			RoomDB.Rooms.Update(room);

			var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
			foreach (var pid in allPlayers)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

			return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
		}

		[HttpPut("rooms/{roomId}/automute")]
		public async Task<IActionResult> SetRoomAutomute(long roomId, [FromForm] bool disable)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			var room = RoomDB.GetRoom(roomId);
			if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
			if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

			room.DisableMicAutoMute = disable;
			RoomDB.Rooms.Update(room);

			var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
			foreach (var pid in allPlayers)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

			return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
		}

		[HttpPut("rooms/{roomId}/allow_new_users")]
		public async Task<IActionResult> SetAllowNewUsers(long roomId, [FromForm] bool allowNewUsers)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			var room = RoomDB.GetRoom(roomId);
			if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
			if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

			room.MinLevel = allowNewUsers ? 0 : 5;
			RoomDB.Rooms.Update(room);

			var allPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
			foreach (var pid in allPlayers)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

			return Ok(new { success = true, error = "", value = RoomDB.GetRoom(roomId) });
		}

		[HttpGet("rooms/moderatedby/me")]
		public async Task<IActionResult> GetRoomsModeratedByMe()
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			var rooms = RoomDB.Rooms.FindAll()
				.Where(r => r.Roles?.Any(x => x.AccountId == (long)id && (int)x.Role >= (int)Role.Moderator) ?? false)
				.ToList();

			return Ok(rooms);
		}

		[HttpPost("rooms/{roomId}/bans/import")]
		public async Task<IActionResult> ImportRoomBans(long roomId, [FromForm] long sourceRoomId)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			return Ok(new { success = true, error = (string?)null, value = (object?)null });
		}

		[HttpPost("rooms/{roomId}/bans")]
		public async Task<IActionResult> BanFromRoom(long roomId, [FromForm] int banMask, [FromForm(Name = "id")] long targetId)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			var room = RoomDB.GetRoom(roomId);
			if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
			if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

			room.Roles ??= new List<RoomDBClasses.Roles>();

			var existing = room.Roles.FirstOrDefault(r => r.AccountId == targetId);
			if (existing != null)
				existing.Role = Role.Banned;
			else
				room.Roles.Add(new RoomDBClasses.Roles { AccountId = targetId, Role = Role.Banned, InvitedRole = Role.None });

			RoomDB.Rooms.Update(room);

			var targetPlayer = PlayerDB.Players.FindById(targetId);
			if (targetPlayer?.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomId == roomId)
			{
				var dormPlayer = PlayerDB.GetCurrentPlayer(targetId);
				if (dormPlayer != null)
				{
					var dormRoomId = Vanadium.Controllers.MatchController.EnsurePlayerDorm(dormPlayer);
					var dormHeartbeat = Vanadium.Classes.Sessions.CreateDorm(targetId, dormPlayer.Player.Username, dormRoomId);
					await NotificationsController.RefreshHeartbeat(targetId, dormHeartbeat);
				}
			}

			return Ok(new { success = true, error = (string?)null, value = (object?)null });
		}

		[HttpGet("rooms/{roomId}/bans")]
		public async Task<IActionResult> GetRoomBans(long roomId)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			var room = RoomDB.GetRoom(roomId);
			if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");

			var banned = (room.Roles ?? new List<RoomDBClasses.Roles>())
				.Where(r => r.Role == Role.Banned)
				.Select(r => new { AccountId = r.AccountId, BanStartTime = DateTime.UtcNow })
				.ToList();

			return Ok(banned);
		}

		[HttpDelete("rooms/{roomId}/bans/{playerId}")]
		public async Task<IActionResult> UnbanFromRoom(long roomId, long playerId)
		{
			var id = AuthStuff.GetPlayerId(Request);
			if (id == null) return Unauthorized("");

			var room = RoomDB.GetRoom(roomId);
			if (room == null) return RoomError("Rooms.DoesntExist", "This room does not exist!");
			if (!CanEdit(room, (long)id)) return RoomError("Rooms.PermissionDenied", "You are not the owner of this room!");

			room.Roles?.RemoveAll(r => r.AccountId == playerId && r.Role == Role.Banned);

			RoomDB.Rooms.Update(room);

			return Ok(new { success = true, error = (string?)null, value = (object?)null });
		}

        public class RoomDataRequest
        {
            public bool AutoPublish { get; set; }
            public string? Description { get; set; }
            public string? InventionUsage { get; set; }
            public int PersistenceVersion { get; set; }
            public RoomFileData? RoomData { get; set; }
            public RoomFileData? SubRoomData { get; set; }
            public string? UnityAssetId { get; set; }
        }

		private async Task<bool> ContainsProhibitedText(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return false;
			string filePath = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "swears.txt");
			if (!System.IO.File.Exists(filePath)) return false;
			var swearWords = await System.IO.File.ReadAllLinesAsync(filePath);
			return swearWords
				.Select(w => w.Trim())
				.Where(w => !string.IsNullOrEmpty(w))
				.Any(word => Regex.IsMatch(text, $@"(?<![a-zA-Z]){Regex.Escape(word)}(?![a-zA-Z])", RegexOptions.IgnoreCase));
		}
    }
}