using LiteDB;
using System;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class RoomConsumablesDBClasses
    {
        /// <summary>
        /// A creator-made consumable for sale in a room's shop.
        /// The client identifies these purely by GUID, so the GUID is the primary key.
        /// </summary>
        public class RoomConsumable
        {
            [BsonId]
            public Guid RoomConsumableId { get; set; } = Guid.NewGuid();
            public long RoomId { get; set; }
            public long CreatorPlayerId { get; set; }
            public string Name { get; set; } = "";
            public string? Description { get; set; }
            public string? ImageName { get; set; }
            public int Price { get; set; }

            /// <summary>Room currency the item is priced in; null means it is priced in tokens.</summary>
            public Guid? CurrencyId { get; set; }

            /// <summary>Soft delete — players may still hold deleted items in inventory.</summary>
            public bool IsDeleted { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
        }

        /// <summary>One player's stack of one consumable.</summary>
        public class RoomConsumableOwnership
        {
            [BsonId]
            public long Id { get; set; }
            public long PlayerId { get; set; }
            public Guid RoomConsumableId { get; set; }
            public int Count { get; set; }

            /// <summary>
            /// Client-generated token guarding consume/purchase races. The client adopts the
            /// NewConcurrencyCode it sent regardless of what the response echoes back, so this
            /// must be stored verbatim or the next consume fails with ConcurrencyCodeMismatch.
            /// </summary>
            public Guid ConcurrencyCode { get; set; } = Guid.NewGuid();
            public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
        }

        /// <summary>
        /// A player's balance of one room currency. Vanadium has no room-currency registry yet
        /// (see RoomConsumablesController.PurchaseCurrency), so this exists to make
        /// currency-priced purchases work once one is added.
        /// </summary>
        public class RoomCurrencyBalance
        {
            [BsonId]
            public long Id { get; set; }
            public long PlayerId { get; set; }
            public Guid CurrencyId { get; set; }
            public int Balance { get; set; }
            public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
        }
    }
}
