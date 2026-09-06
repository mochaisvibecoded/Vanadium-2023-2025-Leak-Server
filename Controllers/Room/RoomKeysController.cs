using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using Vanadium.Auth;
using Vanadium.Classes.DBs;
using static Vanadium.Classes.DBs.DBClasses.RoomKeysDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    public class RoomKeysController : ControllerBase
    {
        private static object MapKey(RoomKey key) => new
        {
            key.CreatedAt,
            key.Description,
            key.Name,
            key.Price,
            key.PurchaseCurrencyId,
            key.ReplicationId,
            key.RoomId,
            key.RoomKeyId
        };

        [HttpPost("/api/roomkeys/v1/create")]
        public IActionResult CreateRoomKey(
            [FromForm] long RoomId,
            [FromForm] string Name,
            [FromForm] string? Description,
            [FromForm] int Price,
            [FromForm] string? PurchaseCurrencyId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            if (string.IsNullOrWhiteSpace(Name) || Name.Length > 40)
                return BadRequest(new { Status = 1, Error = "Name must be between 1 and 40 characters." });

            if (!string.IsNullOrEmpty(Description) && Description.Length > 174)
                return BadRequest(new { Status = 7, Error = "Description must be 174 characters or less." });

            var room = RoomDB.GetRoom(RoomId);
            if (room == null) return NotFound("");

            if (!RoomDB.UserCanEditRoom(RoomId, id.Value))
                return Forbid();

            var key = RoomKeysDB.CreateRoomKey(RoomId, Name, Description, Price, PurchaseCurrencyId);
            if (key == null) return StatusCode(500);

            return Ok(new
            {
                RoomKey = MapKey(key),
                Status = 0
            });
        }

        [HttpPut("/api/roomkeys/v1/updateAll")]
        public IActionResult UpdateAll(
            [FromForm] long RoomKeyId,
            [FromForm] string Name,
            [FromForm] string? Description,
            [FromForm] int Price,
            [FromForm] string? PurchaseCurrencyId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var key = RoomKeysDB.GetRoomKey(RoomKeyId);
            if (key == null) return NotFound("");

            if (!RoomDB.UserCanEditRoom(key.RoomId, id.Value))
                return Forbid();

            if (string.IsNullOrWhiteSpace(Name) || Name.Length > 40)
                return BadRequest(new { Status = 1, Error = "Name must be between 1 and 40 characters." });

            if (!string.IsNullOrEmpty(Description) && Description.Length > 174)
                return BadRequest(new { Status = 7, Error = "Description must be 174 characters or less." });

            RoomKeysDB.UpdateRoomKeyAll(RoomKeyId, Name, Description, Price, PurchaseCurrencyId);

            return Ok(new
            {
                RoomKey = RoomKeyId,
                Status = 12
            });
        }

        [HttpPut("/api/roomkeys/v1/updateName")]
        public IActionResult UpdateName(
            [FromForm] long RoomKeyId,
            [FromForm] string Name)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var key = RoomKeysDB.GetRoomKey(RoomKeyId);
            if (key == null) return NotFound("");

            if (!RoomDB.UserCanEditRoom(key.RoomId, id.Value))
                return Forbid();

            if (string.IsNullOrWhiteSpace(Name) || Name.Length > 40)
                return BadRequest(new { Status = 1, Error = "Name must be between 1 and 40 characters." });

            RoomKeysDB.UpdateRoomKeyName(RoomKeyId, Name);

            return Ok(new { RoomKey = RoomKeyId, Status = 12 });
        }

        [HttpPut("/api/roomkeys/v1/updateDescription")]
        public IActionResult UpdateDescription(
            [FromForm] long RoomKeyId,
            [FromForm] string Description)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var key = RoomKeysDB.GetRoomKey(RoomKeyId);
            if (key == null) return NotFound("");

            if (!RoomDB.UserCanEditRoom(key.RoomId, id.Value))
                return Forbid();

            if (!string.IsNullOrEmpty(Description) && Description.Length > 174)
                return BadRequest(new { Status = 7, Error = "Description must be 174 characters or less." });

            RoomKeysDB.UpdateRoomKeyDescription(RoomKeyId, Description);

            return Ok(new { RoomKey = RoomKeyId, Status = 12 });
        }

        [HttpDelete("/api/roomkeys/v1/delete/{roomKeyId}")]
        public IActionResult DeleteRoomKey(long roomKeyId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var key = RoomKeysDB.GetRoomKey(roomKeyId);
            if (key == null) return NotFound("");

            if (!RoomDB.UserCanEditRoom(key.RoomId, id.Value))
                return Forbid();

            var deleted = RoomKeysDB.DeleteRoomKey(roomKeyId);
            return Ok(deleted);
        }

        [HttpGet("/api/roomkeys/v1/room")]
        public IActionResult GetRoomKeys([FromQuery] long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var keys = RoomKeysDB.GetRoomKeysByRoom(roomId).Select(MapKey).ToList();
            return Ok(keys);
        }

        [HttpGet("/api/roomkeys/v1/mine")]
        public IActionResult GetMyKeys()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var keys = RoomKeysDB.GetRoomKeysByPlayer(id.Value).Select(MapKey).ToList();
            return Ok(keys);
        }

        [HttpGet("/api/roomkeys/v1/owns")]
        public IActionResult PlayerOwnsKey([FromQuery] long playerId, [FromQuery] long roomKeyId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var owns = RoomKeysDB.PlayerOwnsKey(playerId, roomKeyId);
            return Ok(owns);
        }

        [HttpGet("/api/roomkeys/v1/purchased/{roomKeyId}")]
        public IActionResult HasPurchased(long roomKeyId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var purchased = RoomKeysDB.HasPurchasedKey(id.Value, roomKeyId);
            return Ok(purchased);
        }

        [HttpPost("/api/roomkeys/v1/awardbulk")]
        public IActionResult AwardBulk([FromBody] List<AwardBulkRequest> requests)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            if (requests == null || requests.Count == 0)
                return BadRequest();

            var results = new List<object>();

            foreach (var req in requests)
            {
                var key = RoomKeysDB.GetRoomKey(req.RoomKeyId);
                if (key == null)
                {
                    results.Add(new { req.PlayerId, req.RoomKeyId, Success = false, Error = "Key not found" });
                    continue;
                }

                if (!RoomDB.UserCanEditRoom(key.RoomId, id.Value))
                {
                    results.Add(new { req.PlayerId, req.RoomKeyId, Success = false, Error = "Forbidden" });
                    continue;
                }

                var awarded = RoomKeysDB.AwardKey(req.PlayerId, req.RoomKeyId);
                results.Add(new { req.PlayerId, req.RoomKeyId, Success = awarded });
            }

            return Ok(results);
        }

        [HttpPost("/api/roomkeys/v1/revoke")]
        public IActionResult RevokeKey([FromBody] RevokeRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var key = RoomKeysDB.GetRoomKey(request.RoomKeyId);
            if (key == null) return NotFound("");

            if (!RoomDB.UserCanEditRoom(key.RoomId, id.Value))
                return Forbid();

            var revoked = RoomKeysDB.RevokeKey(request.PlayerId, request.RoomKeyId);

            return Ok(new
            {
                Success = revoked,
                Message = revoked ? null : "Player does not own this key"
            });
        }
    }
}