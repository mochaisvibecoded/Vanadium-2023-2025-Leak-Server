using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes.DBs;
using Vanadium.Classes;
using static Vanadium.Classes.DBs.DBClasses.EventDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    public partial class PlayerEventsController : ControllerBase
    {
        [HttpGet("/api/playerevents/v1/tagfilters")]
        public async Task<IActionResult> PlayerEventTags()
        {
            return Ok(System.Text.Json.JsonSerializer.Deserialize<object>(
                System.IO.File.ReadAllText(
                    Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "EventTags.json"))));
        }

        [HttpGet("/api/playerevents/v1/all")]
        public async Task<IActionResult> GetAllPlayerEvents()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var created = EventDB.GetPlayerEvents(id.Value);
            var attending = EventDB.GetAttendingEvents(id.Value);
            var hosting = EventDB.GetHostingEvents(id.Value);
            var responses = EventDB.GetResponsesForPlayer(id.Value);

            return Ok(new
            {
                Created = created,
                Attending = attending,
                Hosting = hosting,
                Responses = responses
            });
        }

        [HttpGet("/api/playerevents/v1/searchlive")]
        public async Task<IActionResult> GetLiveEvents()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var events = EventDB.GetLiveEvents();
            return Ok(events);
        }

        [HttpGet("/api/playerevents/v1/search")]
        public async Task<IActionResult> SearchEvents(
            [FromQuery] string? query,
            [FromQuery] string? sort,
            [FromQuery] string? scheduleFilter)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var events = EventDB.SearchEvents(query, sort, scheduleFilter);
            return Ok(events);
        }

        [HttpGet("/api/playerevents/v1/room/{roomId}")]
        public async Task<IActionResult> GetEventsForRoom(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var events = EventDB.GetEventsByRoomId(roomId);
            return Ok(events);
        }
        
        [HttpGet("/api/playerevents/v1/club")]
        [HttpGet("/api/playerevents/v1/clubs")]
        public async Task<IActionResult> GetEventsForClub()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            return Ok(new List<object>());
        }

        [HttpGet("/api/playerevents/v1/{eventId}")]
        public async Task<IActionResult> GetPlayerEvent(long eventId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var ev = EventDB.GetPlayerEventById(eventId);
            if (ev == null)
                return NotFound("");

            return Ok(ev);
        }

        [HttpPost("/api/playerevents/v2")]
        public async Task<IActionResult> CreatePlayerEvent([FromBody] PlayerEventRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            if (Sanitize.ContainsSwears(request.Name) || Sanitize.ContainsSwears(request.Description))
                return Ok(new PlayerEventResponse { PlayerEvent = null, Result = 14 });

            var response = EventDB.CreatePlayerEvent(id.Value, request);
            if (response.Result != 0)
                return Ok(response);

            var wsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new Vanadium.Classes.WebSocket.WebsocketEvents.Response
            {
                Id = "PlayerEventCreated",
                Msg = response.PlayerEvent
            });
            await NotificationsController.SendToAll(wsJson);

            return Ok(response);
        }

        [HttpDelete("/api/playerevents/v2/delete/{eventId}")]
        public async Task<IActionResult> DeletePlayerEvent(long eventId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var deleted = EventDB.DeletePlayerEvent(eventId, id.Value);
            return Ok(new { success = deleted });
        }

        [HttpPost("/api/playerevents/v2/{eventId}/rsvp")]
        public async Task<IActionResult> RsvpPlayerEvent(long eventId, [FromBody] RsvpRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var ev = EventDB.GetPlayerEventById(eventId);
            if (ev == null)
                return NotFound("");

            var response = EventDB.SetResponse(eventId, id.Value, request.Type);
            return Ok(new { success = true, value = response });
        }

        [HttpGet("/api/playerevents/v2/{eventId}/rsvp")]
        public async Task<IActionResult> GetRsvp(long eventId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var response = EventDB.GetResponse(eventId, id.Value);
            var type = response?.Type ?? (int)PlayerEventResponseType.None;
            return Ok(new { type });
        }

        [HttpGet("/api/playerevents/v2/{eventId}/responses")]
        public async Task<IActionResult> GetEventResponses(long eventId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var responses = EventDB.GetResponsesForEvent(eventId);
            return Ok(responses);
        }

        [HttpPost("/api/playerevents/v1/respond")]
        public async Task<IActionResult> RespondToPlayerEvent([FromBody] RespondRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            if (request == null || request.PlayerEventId == 0)
                return BadRequest(new { success = false, error = "PlayerEventId required." });

            var ev = EventDB.GetPlayerEventById(request.PlayerEventId);
            if (ev == null)
                return NotFound("");

            int typeInt = request.Type switch
            {
                "Yes" => (int)PlayerEventResponseType.Yes,
                "Interested" => (int)PlayerEventResponseType.Interested,
                "No" => (int)PlayerEventResponseType.No,
                _ => (int)PlayerEventResponseType.None
            };

            var response = EventDB.SetResponse(request.PlayerEventId, id.Value, typeInt);

            var updatedEvent = EventDB.GetPlayerEventById(request.PlayerEventId);
            var wsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new Vanadium.Classes.WebSocket.WebsocketEvents.Response
            {
                Id = "PlayerEventResponseChanged",
                Msg = updatedEvent
            });
            await NotificationsController.SendToAll(wsJson);

            return Ok(new { success = true, value = response });
        }

        [HttpGet("/api/playerevents/v1/{eventId}/responses")]
        public async Task<IActionResult> GetEventResponsesV1(long eventId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            return Ok(EventDB.GetResponsesForEvent(eventId));
        }

        [HttpPut("/api/playerevents/v2/{eventId}/name")]
        [HttpPost("/api/playerevents/v2/{eventId}/name")]
        public async Task<IActionResult> EditEventName(long eventId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var name = await ReadBodyValue("name", "value");
            if (name == null)
                return BadRequest(new { success = false, error = "Name required." });

            var ev = EventDB.EditPlayerEvent(eventId, id.Value, e => e.Name = name);
            if (ev == null) return NotFound();
            var wsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new Vanadium.Classes.WebSocket.WebsocketEvents.Response
            {
                Id = "PlayerEventUpdated",
                Msg = ev
            });
            await NotificationsController.SendToAll(wsJson);
            return Ok(new { success = true, value = ev });
        }

        [HttpPut("/api/playerevents/v2/{eventId}/description")]
        [HttpPost("/api/playerevents/v2/{eventId}/description")]
        public async Task<IActionResult> EditEventDescription(long eventId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var desc = await ReadBodyValue("description", "value");
            if (desc == null)
                return BadRequest(new { success = false, error = "Description required." });

            var ev = EventDB.EditPlayerEvent(eventId, id.Value, e => e.Description = desc);
            if (ev == null) return NotFound();
            var wsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new Vanadium.Classes.WebSocket.WebsocketEvents.Response
            {
                Id = "PlayerEventUpdated",
                Msg = ev
            });
            await NotificationsController.SendToAll(wsJson);
            return Ok(new { success = true, value = ev });
        }

        [HttpPut("/api/playerevents/v2/{eventId}/image")]
        [HttpPost("/api/playerevents/v2/{eventId}/image")]
        public async Task<IActionResult> EditEventImage(long eventId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var image = await ReadBodyValue("imageName", "image", "value");
            if (image == null)
                return BadRequest(new { success = false, error = "ImageName required." });

            var ev = EventDB.EditPlayerEvent(eventId, id.Value, e => e.ImageName = image);
            if (ev == null) return NotFound();
            var wsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new Vanadium.Classes.WebSocket.WebsocketEvents.Response
            {
                Id = "PlayerEventUpdated",
                Msg = ev
            });
            await NotificationsController.SendToAll(wsJson);
            return Ok(new { success = true, value = ev });
        }

        [HttpPut("/api/playerevents/v2/{eventId}/room")]
        [HttpPost("/api/playerevents/v2/{eventId}/room")]
        public async Task<IActionResult> EditEventRoom(long eventId, [FromBody] PlayerEventRoomRequest body)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            if (body == null || body.RoomId == 0)
                return BadRequest(new { success = false, error = "RoomId required." });

            var ev = EventDB.EditPlayerEvent(eventId, id.Value, e =>
            {
                e.RoomId = body.RoomId;
                e.SubRoomId = body.SubRoomId;
            });
            if (ev == null) return NotFound();
            var wsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new Vanadium.Classes.WebSocket.WebsocketEvents.Response
            {
                Id = "PlayerEventUpdated",
                Msg = ev
            });
            await NotificationsController.SendToAll(wsJson);
            return Ok(new { success = true, value = ev });
        }

        [HttpPut("/api/playerevents/v2/{eventId}/time")]
        [HttpPost("/api/playerevents/v2/{eventId}/time")]
        public async Task<IActionResult> EditEventTime(long eventId, [FromBody] PlayerEventTimeRequest body)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            if (body == null || (string.IsNullOrWhiteSpace(body.StartTime) && string.IsNullOrWhiteSpace(body.EndTime)))
                return BadRequest(new { success = false, error = "StartTime or EndTime required." });

            DateTime? start = null, end = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(body.StartTime)) start = DateTime.Parse(body.StartTime);
                if (!string.IsNullOrWhiteSpace(body.EndTime)) end = DateTime.Parse(body.EndTime);
            }
            catch
            {
                return BadRequest(new { success = false, error = "Invalid time format." });
            }

            var ev = EventDB.EditPlayerEvent(eventId, id.Value, e =>
            {
                if (start.HasValue) e.StartTime = start.Value;
                if (end.HasValue) e.EndTime = end.Value;
            });
            if (ev == null) return NotFound();
            var wsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new Vanadium.Classes.WebSocket.WebsocketEvents.Response
            {
                Id = "PlayerEventUpdated",
                Msg = ev
            });
            await NotificationsController.SendToAll(wsJson);
            return Ok(new { success = true, value = ev });
        }

        [HttpPut("/api/playerevents/v2/{eventId}/club")]
        [HttpPost("/api/playerevents/v2/{eventId}/club")]
        public async Task<IActionResult> EditEventClub(long eventId, [FromBody] PlayerEventClubRequest body)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var ev = EventDB.EditPlayerEvent(eventId, id.Value, e => e.ClubId = body?.ClubId);
            if (ev == null) return NotFound();
            var wsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new Vanadium.Classes.WebSocket.WebsocketEvents.Response
            {
                Id = "PlayerEventUpdated",
                Msg = ev
            });
            await NotificationsController.SendToAll(wsJson);
            return Ok(new { success = true, value = ev });
        }

        [HttpPut("/api/playerevents/v2/{eventId}/multiinstance")]
        [HttpPost("/api/playerevents/v2/{eventId}/multiinstance")]
        public async Task<IActionResult> EditEventMultiInstance(long eventId, [FromBody] PlayerEventMultiInstanceRequest body)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            if (body == null)
                return BadRequest(new { success = false, error = "Body required." });

            var ev = EventDB.EditPlayerEvent(eventId, id.Value, e =>
            {
                e.IsMultiInstance = body.IsMultiInstance;
                e.SupportMultiInstanceRoomChat = body.SupportMultiInstanceRoomChat;
            });
            if (ev == null) return NotFound();
            var wsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new Vanadium.Classes.WebSocket.WebsocketEvents.Response
            {
                Id = "PlayerEventUpdated",
                Msg = ev
            });
            await NotificationsController.SendToAll(wsJson);
            return Ok(new { success = true, value = ev });
        }

        private async Task<string?> ReadBodyValue(params string[] keys)
        {
            using var reader = new StreamReader(Request.Body);
            var raw = (await reader.ReadToEndAsync())?.Trim();
            if (string.IsNullOrEmpty(raw))
                return null;

            if (raw.StartsWith("{"))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(raw);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        foreach (var k in keys)
                            if (string.Equals(prop.Name, k, StringComparison.OrdinalIgnoreCase))
                                return prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                                    ? prop.Value.GetString()
                                    : prop.Value.ToString();
                }
                catch { }
                return null;
            }

            if (raw.StartsWith("\""))
            {
                try { return System.Text.Json.JsonSerializer.Deserialize<string>(raw); }
                catch { return raw.Trim('"'); }
            }

            return raw;
        }

        public class RsvpRequest
        {
            public int Type { get; set; }
        }
    }
}