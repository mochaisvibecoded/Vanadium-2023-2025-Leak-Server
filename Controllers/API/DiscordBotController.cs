using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Hubs;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using Vanadium.Utils.NotiController;

namespace Vanadium.Controllers
{
    [ApiController]
    [Route("/discordbot")]
    public partial class DiscordBotController : ControllerBase
    {
        /*private static NotificationService? _notiService;

        public static void Init(NotificationService service)
        {
            _notiService = service;
        }*/
        

        public static Task BroadcastBotEvent(string eventType, object payload)
        {
            return Task.CompletedTask;
        }

        [HttpGet("status")]
        public async Task<IActionResult> Status()
        {
            bool hasToken = !string.IsNullOrWhiteSpace(ServerConfig.BotToken);
            return Ok(new
            {
                botConfigured = hasToken,
                message = hasToken
                    ? "token is working"
                    : "token is missing"
            });
        }

        [HttpPut("rooms/{roomId}/image")]
        public async Task<IActionResult> SetRoomImage(long roomId, [FromForm] string imageName)
        {
            if (string.IsNullOrWhiteSpace(imageName))
                return BadRequest("imageName is required.");

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound($"Room {roomId} not found.");

            RoomDB.SetRoomImageName(roomId, imageName);
            var updatedRoom = RoomDB.GetRoom(roomId);

            var onlinePlayers = PlayerDB.Players
                .FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
                .Select(p => p.PlayerId)
                .ToList();

            foreach (var pid in onlinePlayers)
                await NotiController.SendRoomUpdate(pid, updatedRoom);

            return Ok(new
            {
                success = true,
                roomId,
                imageName,
                notified = onlinePlayers.Count
            });
        }
    }
}