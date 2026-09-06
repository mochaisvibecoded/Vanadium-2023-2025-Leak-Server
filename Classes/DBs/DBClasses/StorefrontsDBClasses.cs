using LiteDB;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class StorefrontsDBClasses
    {
        public class StorefrontSaleData
        {
            public int DiscountPercent { get; set; }
            public DateTime SaleStart { get; set; }
            public DateTime SaleEnd { get; set; }
        }

        public class StorefrontPrice
        {
            public int CurrencyType { get; set; }
            public int Price { get; set; }
            public StorefrontSaleData? StorefrontSaleData { get; set; } = null;
            public int Type { get; set; } = 0;
        }

        public class GiftDrop
        {
            public string AvatarItemDesc { get; set; } = "";
            public int AvatarItemType { get; set; } = 0;
            public string ConsumableItemDesc { get; set; } = "";
            public int Context { get; set; } = 0;
            public int Currency { get; set; } = 0;
            public int CurrencyType { get; set; } = 0;
            public string EquipmentModificationGuid { get; set; } = "";
            public string EquipmentPrefabName { get; set; } = "";
            public string FriendlyName { get; set; } = "";
            public int GiftDropId { get; set; }
            public bool IsQuery { get; set; } = false;
            public string ItemSetFriendlyName { get; set; } = "";
            public int ItemSetId { get; set; } = 0;
            public int Rarity { get; set; } = 0;
            public bool SubscribersOnly { get; set; } = false;
            public string Tooltip { get; set; } = "";
            public bool Unique { get; set; } = false;
        }

        public class StoreItem
        {
            public GiftDrop? GiftDrop { get; set; }
            public bool IsFeatured { get; set; } = false;
            public List<StorefrontPrice> Prices { get; set; } = new();
            public int PurchasableItemId { get; set; }
            public List<StorefrontPrice> SubscriberPrices { get; set; } = new();
            public int Type { get; set; } = 0;
        }

        public class GiftDropStoreResponse
        {
            public DateTime NextUpdate { get; set; }
            public List<StoreItem> StoreItems { get; set; } = new();
            public int StorefrontType { get; set; }
            public int SubscriberDiscountPercent { get; set; } = 0;
        }

        public class AdCarouselItem
        {
            [BsonId]
            public int AdCarouselItemId { get; set; }
            public string Description { get; set; } = "";
            public string ImageName { get; set; } = "";
            public int? PurchaseReminderId { get; set; } = null;
            public List<int> PurchasableItemIds { get; set; } = new List<int>();
            public bool ShowCustomAvatarItemSearch { get; set; } = false;
            public string Title { get; set; } = "";
        }

        public class WishlistItem
        {
            [BsonId]
            public string WishlistItemId { get; set; } = Guid.NewGuid().ToString();
            public long AccountId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public int PurchasableItemId { get; set; }
        }

        public class BuyItemRequest
        {
            public string? CouponConsumablePlayerMappingId { get; set; } = null;
            public int CurrencyType { get; set; }
            public BuyItemGift? Gift { get; set; } = null;
            public int PurchasableItemId { get; set; }
            public int RequestedPrice { get; set; }
            public int StorefrontType { get; set; }
        }

        public class BuyItemGift
        {
            public long RecipientPlayerId { get; set; }
            public long? ToPlayerId { get; set; }
            public string? Message { get; set; }
        }
        
        public class TransferableConsumable
		{
			[BsonId]
			public long Id { get; set; }
			public long PlayerId { get; set; }
			public string ConsumableItemDesc { get; set; } = "";
			public int ActiveDurationMinutes { get; set; } = 5;
			public int Count { get; set; } = 1;
			public int InitialCount { get; set; } = 0;
			public bool IsActive { get; set; } = false;
			public bool IsTransferable { get; set; } = true;
			public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
		}

		public class TransferableConsumableGroup
		{
			public int ActiveDurationMinutes { get; set; }
			public string ConsumableItemDesc { get; set; } = "";
			public int Count { get; set; }
			public List<DateTime> CreatedAts { get; set; } = new();
			public List<long> Ids { get; set; } = new();
			public int InitialCount { get; set; } = 0;
			public bool IsActive { get; set; } = false;
			public bool IsTransferable { get; set; } = true;
		}

		public class BuyForFreeGiftButtonRequest
		{
			public int Count { get; set; }
			public string? CouponConsumablePlayerMappingId { get; set; } = null;
			public long CreatorPlayerId { get; set; }
			public int PurchasableItemId { get; set; }
		}

		public class ConsumableItemDTO
		{
			public int ActiveDurationMinutes { get; set; }
			public string ConsumableItemDesc { get; set; } = "";
			public int Count { get; set; } = 1;
			public DateTime CreatedAt { get; set; }
			public long Id { get; set; }
			public int InitialCount { get; set; } = 0;
			public bool IsActive { get; set; } = false;
			public bool IsTransferable { get; set; } = true;
		}

		public class ConsumableBalanceUpdate
		{
			public List<ConsumableItemDTO> Data { get; set; } = new();
			public int UpdateResponse { get; set; } = 2;
		}

		public class BuyForFreeGiftButtonResponse
		{
			public int Balance { get; set; }
			public int BalanceType { get; set; } = -2;
			public List<ConsumableBalanceUpdate> BalanceUpdates { get; set; } = new();
			public int CurrencyType { get; set; } = 2;
		}

        public class BalanceUpdate
        {
            public List<GiftsDBClasses.GiftDTO> Data { get; set; } = new();
            public int UpdateResponse { get; set; } = 0;
        }

        public class BuyItemResponse
        {
            public int Balance { get; set; }
            public int BalanceType { get; set; } = -2;
            public List<BalanceUpdate> BalanceUpdates { get; set; } = new();
            public int CurrencyType { get; set; }
        }

        public class GameRewardRequest
        {
            public string RewardType { get; set; } = "";
            public string? Message { get; set; }
        }

        public class RewardSelectionRequest
        {
            public int RewardSelectionId { get; set; }
            public int GiftDropId { get; set; }
        }

        public class RewardSelectionResponse
        {
            public string AvatarItemDesc { get; set; } = "";
            public int AvatarItemType { get; set; } = 0;
            public string ConsumableItemDesc { get; set; } = "";
            public int Context { get; set; }
            public int Currency { get; set; } = 0;
            public int CurrencyType { get; set; } = 0;
            public string EquipmentModificationGuid { get; set; } = "";
            public string EquipmentPrefabName { get; set; } = "";
            public string FriendlyName { get; set; } = "";
            public int GiftDropId { get; set; }
            public bool IsQuery { get; set; } = false;
            public string ItemSetFriendlyName { get; set; } = "";
            public int ItemSetId { get; set; } = 1;
            public int Rarity { get; set; } = 0;
            public bool SubscribersOnly { get; set; } = false;
            public string Tooltip { get; set; } = "";
            public bool Unique { get; set; } = false;
        }

        public class SuccessResult
        {
            public string error { get; set; } = "";
            public bool success { get; set; } = true;
            public object? value { get; set; } = null;
        }
    }
}