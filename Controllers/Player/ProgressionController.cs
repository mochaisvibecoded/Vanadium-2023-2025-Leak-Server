using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    [Route("/")]
    public partial class ProgressionController : ControllerBase
    {
        private bool CanEdit(RoomDBClasses.Room room, long playerId)
            => room.CreatorAccountId == playerId ||
               (room.Roles?.Any(r => r.AccountId == playerId &&
                   (r.Role == RoomDBClasses.Role.CoOwner ||
                    r.Role == RoomDBClasses.Role.Creator ||
                    r.Role == RoomDBClasses.Role.TemporaryCoOwner)) ?? false);

        [HttpGet("api/players/v1/progression/xpEarnedToday")]
        public IActionResult xpEarnedToday()
        {
            return Ok((int)67);
        }

        [HttpGet("roomserver/rooms/{roomId}/experience")]
        public async Task<IActionResult> GetRoomExperienceTho(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            var room = RoomDB.GetRoom(roomId);
            if (room == null) return NotFound();
            return Ok(new { DailyLimit = room.ExperienceDailyLimit > 0 ? room.ExperienceDailyLimit : 1000000, Enabled = room.ExperienceEnabled, RoomExperienceId = roomId });
        }

        [HttpPost("/roomserver/rooms/{roomId}/experience")]
        public async Task<IActionResult> SetRoomExperience(long roomId, [FromForm] bool enabled, [FromForm] int? dailyLimit)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null) return NotFound();

            if (!CanEdit(room, (long)id)) return Ok(new { success = false });

            room.ExperienceEnabled = enabled;

            if (dailyLimit.HasValue)
                room.ExperienceDailyLimit = dailyLimit.Value;

            RoomDB.Rooms.Update(room);

            return Ok(new { success = true, error = "", value = new { DailyLimit = room.ExperienceDailyLimit, Enabled = room.ExperienceEnabled, RoomExperienceId = roomId } });
        }
    }
}