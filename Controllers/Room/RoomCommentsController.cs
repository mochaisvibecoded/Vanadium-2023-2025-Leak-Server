using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.RoomCommentsDBClasses;
using static Vanadium.Classes.ServerConfig;

namespace Vanadium.Controllers
{
    [ApiController]
    [Route("comments")]
    public class RoomCommentsController : ControllerBase
    {
        private static readonly HttpClient WebhookClient = new HttpClient();

        private static async Task SendCommentWebhook(string type, RoomDBClasses.Room? room, RoomComment comment, long actorPlayerId, bool actorOwnsComment)
        {
            if (string.IsNullOrWhiteSpace(RoomCommentsWebhook))
                return;

            var actor = PlayerDB.GetCurrentPlayer(actorPlayerId);
            string displayName = actor?.Player?.DisplayName ?? actorPlayerId.ToString();
            string roomName = room?.Name ?? comment.roomId.ToString();
            string roomImage = room?.ImageName ?? "";

            var fields = new List<object>();

            if (type == "created")
            {
                fields.Add(new
                {
                    name = "Comment",
                    value = comment.message,
                    inline = false
                });
            }

            fields.Add(new
            {
                name = "Room",
                value = $"{roomName} `{comment.roomId}`",
                inline = true
            });

            fields.Add(new
            {
                name = "Account",
                value = $"{displayName} `{actorPlayerId}` *(owns: {actorOwnsComment})*",
                inline = true
            });

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                embeds = new[]
                {
                    new
                    {
                        author = new
                        {
                            name = $"Room Comment was {type}",
                            icon_url = $"https://reloxa.xyz/imageserver/{roomImage}"
                        },
                        fields,
                        color = 12190963
                    }
                }
            });

            try
            {
                await WebhookClient.PostAsync(RoomCommentsWebhook,
                    new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
            }
            catch { }
        }

        [HttpGet("get/{roomId}")]
        public IActionResult GetComments(long roomId, [FromQuery] int count = 100, [FromQuery] long minId = -1)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            var comments = RoomCommentsDB.GetComments(roomId, count, minId);
            return Ok(comments);
        }

        [HttpPost("create/{roomId}")]
        public async Task<IActionResult> CreateComment(long roomId, [FromForm] CreateCommentRequest req)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            if (string.IsNullOrWhiteSpace(req.message))
                return Ok(new { success = false, error = "Message cannot be empty.", value = (object?)null });

            if (req.message.Length > 100)
                return Ok(new { success = false, error = "Message is too long.", value = (object?)null });

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return Ok(new { success = false, error = "Room not found.", value = (object?)null });

            if (room.DisableRoomComments)
                return Ok(new { success = false, error_id = "Rooms.RoomCommentsDisabledForHighlightedRoom", error = "Room Comments are disabled!" });

            var comment = RoomCommentsDB.CreateComment(
                roomId,
                req.subRoomId,
                playerId.Value,
                req.message,
                req.style,
                req.positionX,
                req.positionY,
                req.positionZ
            );

            _ = SendCommentWebhook("created", room, comment, playerId.Value, true);

            return Ok(new
            {
                success = true,
                error = "",
                value = comment
            });
        }

        [HttpPut("read/{roomId}/{commentId}")]
        public IActionResult MarkCommentRead(long roomId, long commentId)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            RoomCommentsDB.MarkAsRead(roomId, commentId);

            return Ok(new { success = true });
        }

        [HttpDelete("delete/{commentId}")]
        public async Task<IActionResult> DeleteComment(long commentId)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            var comment = RoomCommentsDB.GetComment(commentId);
            if (comment == null)
                return Ok(new { success = false, error = "Comment not found.", value = (object?)null });

            var room = RoomDB.GetRoom(comment.roomId);
            bool isOwner = room?.CreatorAccountId == playerId.Value;
            bool isAuthor = comment.accountId == playerId.Value;
            bool hasRoomRole = room?.Roles?.Any(r =>
                r.AccountId == playerId.Value &&
                (int)r.Role >= (int)Vanadium.Classes.DBs.DBClasses.RoomDBClasses.Role.Moderator) ?? false;

            if (!isOwner && !isAuthor && !hasRoomRole)
                return StatusCode(403);

            RoomCommentsDB.DeleteComment(commentId);

            _ = SendCommentWebhook("deleted", room, comment, playerId.Value, isAuthor);

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                Id = Vanadium.Hubs.NotiEventTypes.RoomCommentDeleted,
                Msg = new
                {
                    RoomId = comment.roomId,
                    Data = comment
                }
            });

            await NotificationsController.SendToAll(json);

            return Ok(new { success = true });
        }
    }
}