using System;
using LiteDB;
using Vanadium.Classes;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.EventDBClasses;

namespace Vanadium.Classes.DBs
{
    public class EventDB
    {
        public static LiteDatabase EventDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Events.db") + ";Connection=shared");
        public static readonly ILiteCollection<PlayerEvent> Events = EventDBFile.GetCollection<PlayerEvent>("PlayerEvents");
        public static readonly ILiteCollection<PlayerEventRsvp> Responses = EventDBFile.GetCollection<PlayerEventRsvp>("PlayerEventResponses");

        public static void Setup()
        {
            Events.EnsureIndex(x => x.CreatorPlayerId);
            Events.EnsureIndex(x => x.RoomId);
            Events.EnsureIndex(x => x.StartTime);
            Responses.EnsureIndex(x => x.PlayerEventId);
            Responses.EnsureIndex(x => x.PlayerId);
        }

        private static long GetNextEventId()
        {
            if (Events.Count() == 0) return 1;
            return Events.Max(x => x.PlayerEventId) + 1;
        }

        private static long GetNextResponseId()
        {
            if (Responses.Count() == 0) return 1;
            return Responses.Max(x => x.PlayerEventResponseId) + 1;
        }

        public static PlayerEventResponse CreatePlayerEvent(long creatorPlayerId, PlayerEventRequest request)
        {
            if (Sanitize.ContainsSwears(request.Name) || Sanitize.ContainsSwears(request.Description))
    return new PlayerEventResponse { PlayerEvent = null, Result = 14 };
            try
            {
                string imageName = request.ImageName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(imageName))
                {
                    var room = RoomDB.Rooms.FindById(request.RoomId);
                    imageName = room?.ImageName ?? string.Empty;
                }

                var playerEvent = new PlayerEvent
                {
                    PlayerEventId = GetNextEventId(),
                    CreatorPlayerId = creatorPlayerId,
                    Name = request.Name ?? string.Empty,
                    Description = request.Description ?? string.Empty,
                    Accessibility = request.Accessibility,
                    ClubId = request.ClubId,
                    RoomId = request.RoomId,
                    SubRoomId = request.SubRoomId,
                    StartTime = DateTime.Parse(request.StartTime),
                    EndTime = DateTime.Parse(request.EndTime),
                    ImageName = imageName,
                    State = 0,
                    AttendeeCount = 0,
                    Tags = (request.Tags ?? new List<string>())
                        .Select(t => new PlayerEventTag { Tag = t, Type = 0 })
                        .ToList(),
                    CreatedAt = DateTime.UtcNow,
                    CanRequestBroadcastPermissions = request.CanRequestBroadcastPermissions,
                    DefaultBroadcastPermissions = request.DefaultBroadcastPermissions,
                    IsMultiInstance = request.IsMultiInstance,
                    SupportMultiInstanceRoomChat = request.SupportMultiInstanceRoomChat
                };

                Events.Insert(playerEvent);

                return new PlayerEventResponse
                {
                    PlayerEvent = playerEvent,
                    Result = 0
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EventDB] Error creating player event: {ex.Message}");
                return new PlayerEventResponse
                {
                    PlayerEvent = null,
                    Result = -1
                };
            }
        }

        public static PlayerEventRsvp? SetResponse(long playerEventId, long playerId, int type)
        {
            var existing = Responses.FindOne(x => x.PlayerEventId == playerEventId && x.PlayerId == playerId);

            if (existing != null)
            {
                existing.Type = type;
                Responses.Update(existing);
                return existing;
            }

            var response = new PlayerEventRsvp
            {
                PlayerEventResponseId = GetNextResponseId(),
                PlayerEventId = playerEventId,
                PlayerId = playerId,
                Type = type,
                CreatedAt = DateTime.UtcNow
            };

            Responses.Insert(response);

            if (type == (int)PlayerEventResponseType.Yes)
            {
                var ev = Events.FindById(playerEventId);
                if (ev != null)
                {
                    ev.AttendeeCount++;
                    Events.Update(ev);
                }
            }

            return response;
        }

        public static PlayerEventRsvp? GetResponse(long playerEventId, long playerId)
        {
            return Responses.FindOne(x => x.PlayerEventId == playerEventId && x.PlayerId == playerId);
        }

        public static List<PlayerEventRsvp> GetResponsesForEvent(long playerEventId)
        {
            return Responses.Find(x => x.PlayerEventId == playerEventId).ToList();
        }

        public static List<PlayerEventRsvp> GetResponsesForPlayer(long playerId)
        {
            return Responses.Find(x => x.PlayerId == playerId).ToList();
        }

        public static List<PlayerEvent> GetPlayerEvents(long creatorPlayerId)
        {
            return Events.Find(x => x.CreatorPlayerId == creatorPlayerId).ToList();
        }

        public static List<PlayerEvent> GetAttendingEvents(long playerId)
        {
            var attendingIds = Responses
                .Find(x => x.PlayerId == playerId && x.Type == (int)PlayerEventResponseType.Yes)
                .Select(x => x.PlayerEventId)
                .ToHashSet();

            return Events
                .FindAll()
                .Where(e => attendingIds.Contains(e.PlayerEventId) && e.CreatorPlayerId != playerId)
                .OrderByDescending(e => e.StartTime)
                .ToList();
        }

        public static List<PlayerEvent> GetHostingEvents(long playerId)
        {
            var now = DateTime.UtcNow;
            return Events
                .Find(x => x.CreatorPlayerId == playerId)
                .Where(e => e.EndTime >= now)
                .OrderBy(e => e.StartTime)
                .ToList();
        }

        public static List<PlayerEvent> GetEventsByRoomId(long roomId)
        {
            return Events
                .FindAll()
                .Where(e => e.RoomId == roomId)
                .OrderByDescending(e => e.StartTime)
                .ToList();
        }

        public static PlayerEvent? GetPlayerEventById(long eventId)
        {
            return Events.FindById(eventId);
        }

        public static bool DeletePlayerEvent(long eventId, long creatorPlayerId)
        {
            var playerEvent = Events.FindById(eventId);
            if (playerEvent == null || playerEvent.CreatorPlayerId != creatorPlayerId)
                return false;

            Responses.DeleteMany(x => x.PlayerEventId == eventId);
            return Events.Delete(eventId);
        }

        public static PlayerEvent? EditPlayerEvent(long eventId, long editorPlayerId, Action<PlayerEvent> mutate)
        {
            var ev = Events.FindById(eventId);
            if (ev == null || ev.CreatorPlayerId != editorPlayerId)
                return null;

            mutate(ev);
            Events.Update(ev);
            return ev;
        }

        public static List<PlayerEvent> GetAllEvents()
        {
            return Events.FindAll()
                .OrderByDescending(e => e.StartTime)
                .ToList();
        }

        public static List<LivePlayerEvent> GetLiveEvents()
        {
            var now = DateTime.UtcNow;
            return Events.FindAll()
                .Where(e => e.StartTime <= now && e.EndTime >= now)
                .OrderByDescending(e => e.AttendeeCount)
                .Select(e => new LivePlayerEvent
                {
                    PlayerEventId = e.PlayerEventId,
                    CreatorPlayerId = e.CreatorPlayerId,
                    Name = e.Name,
                    Description = e.Description,
                    Accessibility = e.Accessibility,
                    ClubId = e.ClubId,
                    RoomId = e.RoomId,
                    SubRoomId = e.SubRoomId,
                    StartTime = e.StartTime,
                    EndTime = e.EndTime,
                    ImageName = e.ImageName,
                    State = e.State,
                    AttendeeCount = e.AttendeeCount,
                    CanRequestBroadcastPermissions = e.CanRequestBroadcastPermissions,
                    DefaultBroadcastPermissions = e.DefaultBroadcastPermissions,
                    IsMultiInstance = e.IsMultiInstance,
                    SupportMultiInstanceRoomChat = e.SupportMultiInstanceRoomChat,
                    Tags = e.Tags,
                    IsFull = Sessions.IsInstanceActuallyFull(e.PlayerEventId),
                    PlayerCount = PlayerDB.Players.FindAll()
                        .Count(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.eventId == e.PlayerEventId
                                && p.Player.PlayerExtra.Heartbeat.isOnline)
                })
                .ToList();
        }

        public static List<PlayerEvent> SearchEvents(string? query, string? sort, string? scheduleFilter)
        {
            var now = DateTime.UtcNow;
            var twoWeeks = now.AddDays(14);

            var all = Events.FindAll().AsEnumerable();

            if (!string.IsNullOrWhiteSpace(scheduleFilter))
            {
                var filter = scheduleFilter.ToLowerInvariant();
                if (filter == "upcoming")
                    all = all.Where(e => e.StartTime > now && e.StartTime <= twoWeeks);
                else if (filter == "live")
                    all = all.Where(e => e.StartTime <= now && e.EndTime >= now);
                else if (filter == "past")
                    all = all.Where(e => e.EndTime < now);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim();

                if (q.StartsWith("#"))
                {
                    var tag = q.Substring(1).ToLowerInvariant();
                    all = all.Where(e => e.Tags != null && e.Tags.Any(t => t.Tag.ToLowerInvariant().Contains(tag)));
                }
                else
                {
                    var ql = q.ToLowerInvariant();
                    all = all.Where(e =>
                        (e.Name ?? "").ToLowerInvariant().Contains(ql) ||
                        (e.Description ?? "").ToLowerInvariant().Contains(ql) ||
                        (e.Tags != null && e.Tags.Any(t => t.Tag.ToLowerInvariant().Contains(ql))));
                }
            }

            var results = all.ToList();

            var sortKey = sort?.ToLowerInvariant() ?? "";
            results = sortKey switch
            {
                "starttime" => results.OrderBy(e => e.StartTime).ToList(),
                "endtime" => results.OrderBy(e => e.EndTime).ToList(),
                "attendance" => results.OrderByDescending(e => e.AttendeeCount).ToList(),
                "createdat" => results.OrderByDescending(e => e.CreatedAt).ToList(),
                _ => results.OrderBy(e => e.StartTime).ToList()
            };

            return results;
        }
    }
}