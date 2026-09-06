using LiteDB;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.StorefrontsDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Classes.DBs
{
    public class StorefrontsDB
    {
        public static LiteDatabase StorefrontsDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Storefronts.db") + ";Connection=shared");
        public static readonly ILiteCollection<WishlistItem> Wishlists = StorefrontsDBFile.GetCollection<WishlistItem>("Wishlists");
        public static readonly ILiteCollection<AdCarouselItem> AdCarousel = StorefrontsDBFile.GetCollection<AdCarouselItem>("AdCarousel");
        public static readonly ILiteCollection<TransferableConsumable> TransferableConsumables = StorefrontsDBFile.GetCollection<TransferableConsumable>("TransferableConsumables");

        static StorefrontsDB()
        {
            Wishlists.EnsureIndex(x => x.AccountId);
            Wishlists.EnsureIndex(x => x.PurchasableItemId);
            TransferableConsumables.EnsureIndex(x => x.PlayerId);
        }

        public static GiftDropStoreResponse? GetGiftDropStore(int storefrontType)
        {
            string fileName = $"{storefrontType}.json";
            string path = Path.Combine(Environment.CurrentDirectory, "Data", "Storefronts", fileName);

            if (!File.Exists(path))
                return null;

            try
            {
                var json = File.ReadAllText(path);
                var result = System.Text.Json.JsonSerializer.Deserialize<GiftDropStoreResponse>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return result;
            }
            catch
            {
                return null;
            }
        }

        public static StoreItem? GetStoreItemById(int storefrontType, int purchasableItemId)
        {
            var store = GetGiftDropStore(storefrontType);
            return store?.StoreItems.FirstOrDefault(x => x.PurchasableItemId == purchasableItemId);
        }

        public static GiftDrop? GetGiftDropById(int storefrontType, int giftDropId)
        {
            var store = GetGiftDropStore(storefrontType);
            return store?.StoreItems.FirstOrDefault(x => x.GiftDrop?.GiftDropId == giftDropId)?.GiftDrop;
        }

        public static GiftDrop? GetGiftDropByIdAcrossAllStores(int giftDropId)
        {
            for (int i = 1; i <= 10; i++)
            {
                var drop = GetGiftDropById(i, giftDropId);
                if (drop != null)
                    return drop;
            }
            return null;
        }

        public static List<WishlistItem> GetWishlist(long accountId)
        {
            return Wishlists.Find(x => x.AccountId == accountId).ToList();
        }

        public static WishlistItem? AddToWishlist(long accountId, int purchasableItemId)
        {
            var existing = Wishlists.FindOne(x => x.AccountId == accountId && x.PurchasableItemId == purchasableItemId);
            if (existing != null)
                return existing;

            var item = new WishlistItem
            {
                WishlistItemId = Guid.NewGuid().ToString(),
                AccountId = accountId,
                CreatedAt = DateTime.UtcNow,
                PurchasableItemId = purchasableItemId
            };

            Wishlists.Insert(item);
            return item;
        }

        public static bool RemoveFromWishlist(long accountId, int purchasableItemId)
        {
            var item = Wishlists.FindOne(x => x.AccountId == accountId && x.PurchasableItemId == purchasableItemId);
            if (item == null)
                return false;
            return Wishlists.Delete(item.WishlistItemId);
        }

        public static List<AdCarouselItem>? GetAdCarouselItems()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "Data", "Storefronts", "AdCarousel.json");

            if (!File.Exists(path))
                return null;

            try
            {
                var json = File.ReadAllText(path);
                return System.Text.Json.JsonSerializer.Deserialize<List<AdCarouselItem>>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }

        public static PlayerCurrency? DeductCurrency(long playerId, int currencyType, int amount)
        {
            var player = PlayerDB.Players.FindById(playerId);
            if (player?.Player?.PlayerExtra?.Currencies == null)
                return null;

            var currency = player.Player.PlayerExtra.Currencies
                .FirstOrDefault(x => (int)x.CurrencyType == currencyType);

            if (currency == null || currency.Balance < amount)
                return null;

            currency.Balance -= amount;
            PlayerDB.Players.Update(player);
            return currency;
        }

        public static PlayerCurrency? AddCurrency(long playerId, int currencyType, int amount, BalanceType balanceType = BalanceType.NonPurchasedNotUsableInP2P)
        {
            var player = PlayerDB.Players.FindById(playerId);
            if (player?.Player == null)
                return null;

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Currencies ??= new List<PlayerCurrency>();

            var currency = player.Player.PlayerExtra.Currencies
                .FirstOrDefault(x => (int)x.CurrencyType == currencyType);

            if (currency == null)
            {
                currency = new PlayerCurrency
                {
                    Balance = 0,
                    CurrencyType = (CurrencyType)currencyType,
                    BalanceType = balanceType
                };
                player.Player.PlayerExtra.Currencies.Add(currency);
            }

            currency.Balance += amount;
            PlayerDB.Players.Update(player);
            return currency;
        }

        public static int GetBalance(long playerId, int currencyType)
        {
            var player = PlayerDB.Players.FindById(playerId);
            if (player?.Player?.PlayerExtra?.Currencies == null)
                return 0;

            var currency = player.Player.PlayerExtra.Currencies
                .FirstOrDefault(x => (int)x.CurrencyType == currencyType);

            return currency?.Balance ?? 0;
        }

        public static List<TransferableConsumableGroup> GetTransferableConsumables(long playerId)
        {
            var items = TransferableConsumables
                .Find(x => x.PlayerId == playerId && x.IsTransferable)
                .ToList();

            return items
                .GroupBy(x => x.ConsumableItemDesc)
                .Select(g =>
                {
                    var first = g.First();
                    return new TransferableConsumableGroup
                    {
                        ActiveDurationMinutes = first.ActiveDurationMinutes,
                        ConsumableItemDesc = g.Key,
                        Count = first.Count,
                        InitialCount = first.InitialCount,
                        IsActive = first.IsActive,
                        IsTransferable = first.IsTransferable,
                        CreatedAts = g.Select(x => x.CreatedAt).ToList(),
                        Ids = g.Select(x => x.Id).ToList()
                    };
                })
                .ToList();
        }

        public static List<ConsumableItemDTO> GrantTransferableConsumables(long playerId, string consumableItemDesc, int count, int activeDurationMinutes)
        {
            var granted = new List<ConsumableItemDTO>();

            for (int i = 0; i < count; i++)
            {
                var item = new TransferableConsumable
                {
                    PlayerId = playerId,
                    ConsumableItemDesc = consumableItemDesc,
                    ActiveDurationMinutes = activeDurationMinutes,
                    Count = 1,
                    InitialCount = 0,
                    IsActive = false,
                    IsTransferable = true,
                    CreatedAt = DateTime.UtcNow
                };

                TransferableConsumables.Insert(item);

                granted.Add(new ConsumableItemDTO
                {
                    Id = item.Id,
                    ConsumableItemDesc = item.ConsumableItemDesc,
                    ActiveDurationMinutes = item.ActiveDurationMinutes,
                    Count = item.Count,
                    InitialCount = item.InitialCount,
                    IsActive = item.IsActive,
                    IsTransferable = item.IsTransferable,
                    CreatedAt = item.CreatedAt
                });
            }

            return granted;
        }
    }
}