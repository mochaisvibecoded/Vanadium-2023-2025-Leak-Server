using System;
using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class EventDBClasses
    {
        public enum PlayerEventAccessibility
        {
            Private = 0,
            Public = 1,
            Unlisted = 2
        }

        public enum PlayerEventResponseType
        {
            None = -1,
            Yes = 0,
            Interested = 1,
            No = 2,
            Pending = 3
        }

        public class PlayerEventTag
        {
            [BsonId]
            public long Id { get; set; }
            public long PlayerEventId { get; set; }
            public string Tag { get; set; } = string.Empty;
            public int Type { get; set; } = 0;
        }

        public class PlayerEventRsvp
        {
            [BsonId]
            public long PlayerEventResponseId { get; set; }
            public long PlayerEventId { get; set; }
            public long PlayerId { get; set; }
            public int Type { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class PlayerEvent
        {
            [BsonId]
            public long PlayerEventId { get; set; }
            public long CreatorPlayerId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int Accessibility { get; set; } = (int)PlayerEventAccessibility.Public;
            public long? ClubId { get; set; }
            public long RoomId { get; set; }
            public long? SubRoomId { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string ImageName { get; set; } = string.Empty;
            public int State { get; set; } = 0;
            public int AttendeeCount { get; set; } = 0;
            public List<PlayerEventTag> Tags { get; set; } = new();
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public int CanRequestBroadcastPermissions { get; set; } = 0;
            public int DefaultBroadcastPermissions { get; set; } = 0;
            public bool IsMultiInstance { get; set; } = false;
            public bool SupportMultiInstanceRoomChat { get; set; } = false;
        }

        public class LivePlayerEvent
        {
            public long PlayerEventId { get; set; }
            public long CreatorPlayerId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int Accessibility { get; set; }
            public long? ClubId { get; set; }
            public long RoomId { get; set; }
            public long? SubRoomId { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string? ImageName { get; set; }
            public int State { get; set; }
            public int AttendeeCount { get; set; }
            public List<PlayerEventTag> Tags { get; set; } = new();
            public bool IsFull { get; set; }
            public int PlayerCount { get; set; }
            public int CanRequestBroadcastPermissions { get; set; } = 0;
            public int DefaultBroadcastPermissions { get; set; } = 0;
            public bool IsMultiInstance { get; set; } = false;
            public bool SupportMultiInstanceRoomChat { get; set; } = false;
        }

        public class RespondRequest
        {
            public long PlayerEventId { get; set; }
            public string Type { get; set; } = "Yes";
        }

        public class PlayerEventRequest
        {
            public int Accessibility { get; set; } = (int)PlayerEventAccessibility.Public;
            public long? ClubId { get; set; }
            public string Description { get; set; } = string.Empty;
            public string EndTime { get; set; } = string.Empty;
            public string ImageName { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public long RoomId { get; set; }
            public string StartTime { get; set; } = string.Empty;
            public long? SubRoomId { get; set; }
            public List<string> Tags { get; set; } = new();
            public int CanRequestBroadcastPermissions { get; set; } = 0;
            public int DefaultBroadcastPermissions { get; set; } = 0;
            public bool IsMultiInstance { get; set; } = false;
            public bool SupportMultiInstanceRoomChat { get; set; } = false;
        }

        public class PlayerEventResponse
        {
            public PlayerEvent? PlayerEvent { get; set; }
            public int Result { get; set; } = 0;
        }

        public class PlayerEventRespondRequest
        {
            public long PlayerEventId { get; set; }
            public int Type { get; set; }
        }

        public class PlayerEventRoomRequest
        {
            public long RoomId { get; set; }
            public long? SubRoomId { get; set; }
        }

        public class PlayerEventTimeRequest
        {
            public string? StartTime { get; set; }
            public string? EndTime { get; set; }
        }

        public class PlayerEventClubRequest
        {
            public long? ClubId { get; set; }
        }

        public class PlayerEventMultiInstanceRequest
        {
            public bool IsMultiInstance { get; set; }
            public bool SupportMultiInstanceRoomChat { get; set; }
        }
    }
}