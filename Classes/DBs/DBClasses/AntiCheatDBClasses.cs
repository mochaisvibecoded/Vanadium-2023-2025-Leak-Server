using System;
using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public static class AntiCheatDBClasses
    {
        public class Detection
        {
            [BsonId]
            public ObjectId Id { get; set; }
            public long PlayerId { get; set; }
            public string Username { get; set; }
            public ulong SteamId { get; set; }
            public string Type { get; set; }        // "cheat" | "injection"
            public string Reason { get; set; }
            public string ModVersion { get; set; }
            public long? RoomId { get; set; }
            public DateTime DetectedAt { get; set; }
            public DateTime LastSeen { get; set; }
            public int Count { get; set; } = 1;
        }
    }
}
