using System;
using System.Collections.Generic;
using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class PhotonAccessTokenDBClasses
    {
        public class SubroomPermissionSet
        {
            [BsonId]
            public long Id { get; set; }
            public long RoomId { get; set; }
            public long SubRoomId { get; set; }
            public List<StoredPermissionEntry> Permissions { get; set; } = new();
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public class StoredPermissionEntry
        {
            public string Permission { get; set; } = "";
            public int Role { get; set; }
            public int Type { get; set; }
            public bool Override { get; set; }
            public string Value { get; set; } = "";
        }
    }
}