using LiteDB;
using System;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class RoomKeysDBClasses
    {
        public class RoomKey
        {
            [BsonId]
            public long RoomKeyId { get; set; }
            public long RoomId { get; set; }
            public string Name { get; set; }
            public string? Description { get; set; }
            public string? ImageName { get; set; }
            public int Price { get; set; }
            public string? PurchaseCurrencyId { get; set; }
            public string ReplicationId { get; set; } = Guid.NewGuid().ToString();
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class RoomKeyOwnership
        {
            [BsonId]
            public long Id { get; set; }
            public long PlayerId { get; set; }
            public long RoomKeyId { get; set; }
            public bool WasPurchased { get; set; }
            public DateTime AwardedAt { get; set; } = DateTime.UtcNow;
        }

        public class AwardBulkRequest
        {
            public long PlayerId { get; set; }
            public long RoomKeyId { get; set; }
        }

        public class RevokeRequest
        {
            public long PlayerId { get; set; }
            public long RoomKeyId { get; set; }
        }
    }
}