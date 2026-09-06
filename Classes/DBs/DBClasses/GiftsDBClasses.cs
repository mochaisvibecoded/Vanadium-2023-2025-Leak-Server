using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class GiftsDBClasses
    {
        public class Gift
        {
            [BsonId]
            public long Id { get; set; }
            public long ToPlayerId { get; set; }
            public long FromPlayerId { get; set; } = 1;
            public string AvatarItemDesc { get; set; } = "";
            public int AvatarItemType { get; set; } = 0;
            public int BalanceType { get; set; } = -2;
            public string ConsumableItemDesc { get; set; } = "";
            public int Currency { get; set; } = 0;
            public int CurrencyType { get; set; } = 0;
            public string EquipmentModificationGuid { get; set; } = "";
            public string EquipmentPrefabName { get; set; } = "";
            public int GiftContext { get; set; } = 0;
            public int GiftRarity { get; set; } = -1;
            public int Level { get; set; } = 0;
            public string Message { get; set; } = "A gift for you <3";
            public int Platform { get; set; } = -1;
            public int PlatformsToSpawnOn { get; set; } = -1;
            public int Xp { get; set; } = 0;
            public bool Consumed { get; set; } = false;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class GiftDTO
        {
            public string AvatarItemDesc { get; set; } = "";
            public int AvatarItemType { get; set; } = 0;
            public int BalanceType { get; set; } = -2;
            public string ConsumableItemDesc { get; set; } = "";
            public int Currency { get; set; } = 0;
            public int CurrencyType { get; set; } = 0;
            public string EquipmentModificationGuid { get; set; } = "";
            public string EquipmentPrefabName { get; set; } = "";
            public long FromPlayerId { get; set; } = 1;
            public int GiftContext { get; set; } = 0;
            public int GiftRarity { get; set; } = -1;
            public long Id { get; set; }
            public int Level { get; set; } = 0;
            public string Message { get; set; } = "A gift for you <3";
            public int Platform { get; set; } = -1;
            public int PlatformsToSpawnOn { get; set; } = -1;
            public int Xp { get; set; } = 0;
        }

        public class ConsumeGiftRequest
        {
            public long Id { get; set; }
            public int UnlockedLevel { get; set; } = 0;
        }
    }
}