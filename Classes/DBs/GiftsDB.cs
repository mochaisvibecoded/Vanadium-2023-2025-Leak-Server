using LiteDB;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.enums;
using static Vanadium.Classes.DBs.DBClasses.GiftsDBClasses;

namespace Vanadium.Classes.DBs
{
    public class GiftsDB
    {
        public static LiteDatabase GiftsDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Gifts.db") + ";Connection=shared");
		public static readonly ILiteCollection<Gift> Gifts = GiftsDBFile.GetCollection<Gift>("Gifts");

		static GiftsDB()
		{
    		Gifts.EnsureIndex(x => x.ToPlayerId);
    		Gifts.EnsureIndex(x => x.Consumed);
		}

        public static Gift CreateGift(
            long toPlayerId,
            long fromPlayerId = 1,
            string avatarItemDesc = "",
            int avatarItemType = 0,
            int balanceType = -2,
            string consumableItemDesc = "",
            int currency = 0,
            int currencyType = 0,
            string equipmentModificationGuid = "",
            string equipmentPrefabName = "",
            GiftType giftContext = GiftType.None,
            int giftRarity = -1,
            int level = 0,
            string message = "A gift for you <3",
            int platform = -1,
            int platformsToSpawnOn = -1,
            int xp = 0)
        {
            var gift = new Gift
            {
                ToPlayerId = toPlayerId,
                FromPlayerId = fromPlayerId,
                AvatarItemDesc = avatarItemDesc,
                AvatarItemType = avatarItemType,
                BalanceType = balanceType,
                ConsumableItemDesc = consumableItemDesc,
                Currency = currency,
                CurrencyType = currencyType,
                EquipmentModificationGuid = equipmentModificationGuid,
                EquipmentPrefabName = equipmentPrefabName,
                GiftContext = (int)giftContext,
                GiftRarity = giftRarity,
                Level = level,
                Message = message,
                Platform = platform,
                PlatformsToSpawnOn = platformsToSpawnOn,
                Xp = xp,
                Consumed = false,
                CreatedAt = DateTime.UtcNow
            };

            Gifts.Insert(gift);
            return gift;
        }

        public static List<GiftDTO> GetUnconsumedGifts(long playerId)
        {
            return Gifts
                .Find(x => x.ToPlayerId == playerId && !x.Consumed)
                .Select(MapToDTO)
                .ToList();
        }

        public static Gift? GetGiftById(long giftId)
        {
            return Gifts.FindById(giftId);
        }

        public static bool ConsumeGift(long giftId, long playerId)
        {
            var gift = Gifts.FindById(giftId);
            if (gift == null || gift.ToPlayerId != playerId || gift.Consumed)
                return false;

            gift.Consumed = true;
            return Gifts.Update(gift);
        }

        public static GiftDTO MapToDTO(Gift gift)
        {
            return new GiftDTO
            {
                AvatarItemDesc = gift.AvatarItemDesc,
                AvatarItemType = gift.AvatarItemType,
                BalanceType = gift.BalanceType,
                ConsumableItemDesc = gift.ConsumableItemDesc,
                Currency = gift.Currency,
                CurrencyType = gift.CurrencyType,
                EquipmentModificationGuid = gift.EquipmentModificationGuid,
                EquipmentPrefabName = gift.EquipmentPrefabName,
                FromPlayerId = gift.FromPlayerId,
                GiftContext = gift.GiftContext,
                GiftRarity = gift.GiftRarity,
                Id = gift.Id,
                Level = gift.Level,
                Message = gift.Message,
                Platform = gift.Platform,
                PlatformsToSpawnOn = gift.PlatformsToSpawnOn,
                Xp = gift.Xp
            };
        }
    }
}