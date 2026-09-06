using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Utils.NotiController;
using Vanadium.enums;
using static Vanadium.Classes.DBs.DBClasses.StorefrontsDBClasses;
using static Vanadium.Classes.DBs.DBClasses.GiftsDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    public partial class StorefrontController : ControllerBase
    {
        [HttpGet("/api/storefronts/v4/balance/{CurrencyType}")]
        public IActionResult StorefrontsBalance(int CurrencyType)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            var player = PlayerDB.Players.FindById(account.Value);
            if (player == null)
                return NotFound("");

            var currency = player.Player?.PlayerExtra?.Currencies?
                .FirstOrDefault(x => (int)x.CurrencyType == CurrencyType);

            return Ok(new[]
            {
                new
                {
                    CurrencyType,
                    Platform = -1,
                    Balance = currency?.Balance ?? 0
                }
            });
        }
        
        [HttpGet("/econ/roomInventory/room/{roomid}/player")]
		public IActionResult roomInventoryEconomy()
        {
        	return Ok(ServerConfig.Bracket);
        }
        
        [HttpGet("/econ/roomOffer/room/{roomid}")]
		public IActionResult roomOfferEconomy()
        {
        	return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/econ/roomGiftDropShops/room/{roomid}")]
		public IActionResult roomGiftDropShopsEconomy()
        {
        	return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/storefronts/v3/giftdropstore/{Storefront}")]
        public IActionResult StorefrontsGiftDropStore(int Storefront)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            var store = StorefrontsDB.GetGiftDropStore(Storefront);
            if (store == null)
                return Ok(ServerConfig.Bracket);

            return Ok(store);
        }

        [HttpGet("/api/storefronts/v1/adcarouselitems")]
        public IActionResult GetAdCarouselItems()
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            var items = StorefrontsDB.GetAdCarouselItems();
            return Ok(items);
        }
        
        [HttpGet("api/catalog/v1/all")]
        public async Task<IActionResult> GetCatalogItemsAllOfThem(bool onlyAvailableSkus = true)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "2025Consumables", "AllCatalogItems.json");
            string content = await System.IO.File.ReadAllTextAsync(path);
            return Ok(content);
        }

        [HttpGet("/api/itemWishlists/v1/wishlist/me")]
        public IActionResult GetMyWishlist()
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            var wishlist = StorefrontsDB.GetWishlist(account.Value);
            return Ok(wishlist);
        }

        [HttpPost("/api/itemWishlists/v1/wishlist/me/{purchasableItemId}")]
        public IActionResult AddToWishlist(int purchasableItemId)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            var item = StorefrontsDB.AddToWishlist(account.Value, purchasableItemId);
            return Ok(item);
        }

        [HttpDelete("/api/itemWishlists/v1/wishlist/me/{purchasableItemId}")]
        public IActionResult RemoveFromWishlist(int purchasableItemId)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            StorefrontsDB.RemoveFromWishlist(account.Value, purchasableItemId);
            return Ok(new SuccessResult { error = "", success = true, value = null });
        }
        
        /* Free Gifts */
        
        [HttpGet("/api/consumables/v1/getTransferable/{playerId}")]
		public IActionResult GetTransferableConsumables(long playerId)
		{
			var account = AuthStuff.GetPlayerId(Request);
			if (account == null)
				return StatusCode(403);

			var groups = StorefrontsDB.GetTransferableConsumables(playerId);
			return Ok(groups);
		}

		[HttpGet("/api/config/v1/freegiftbutton")]
		public IActionResult GetFreeGiftButton()
		{
			var account = AuthStuff.GetPlayerId(Request);
			if (account == null)
				return StatusCode(403);

			return Ok(true);
		}

		[HttpPost("/api/storefronts/v1/buyForFreeGiftButton")]
		public IActionResult BuyForFreeGiftButton([FromBody] BuyForFreeGiftButtonRequest request)
		{
			var account = AuthStuff.GetPlayerId(Request);
			if (account == null)
				return StatusCode(403);

			if (request == null)
				return BadRequest();

			var storeItem = StorefrontsDB.GetStoreItemById(1, request.PurchasableItemId);
			if (storeItem == null)
				return NotFound(new { error = "Item not found", success = false });

			var consumableDesc = storeItem.GiftDrop?.ConsumableItemDesc ?? "";
			var activeDuration = 5;

			var granted = StorefrontsDB.GrantTransferableConsumables(account.Value, consumableDesc, request.Count, activeDuration);

			int balance = StorefrontsDB.GetBalance(account.Value, 2);

			var response = new SuccessResult
			{
				error = "",
				success = true,
				value = new BuyForFreeGiftButtonResponse
				{
					Balance = balance,
					BalanceType = -2,
					CurrencyType = 2,
					BalanceUpdates = new List<ConsumableBalanceUpdate>
					{
						new ConsumableBalanceUpdate
						{
							Data = granted,
							UpdateResponse = 2
						}
					}
				}
			};

			return Ok(response);
		}
        
        /* End of free gifts, lmk if i missed anything */

		[HttpPost("/api/items/bulkpurchase")]
        [HttpPost("/api/storefronts/v2/buyItem")]
        public async Task<IActionResult> BuyItem([FromBody] BuyItemRequest request)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            if (request == null)
                return BadRequest();

            var storeItem = StorefrontsDB.GetStoreItemById(request.StorefrontType, request.PurchasableItemId);
            if (storeItem == null)
                return NotFound(new { error = "Item not found", success = false });

            var matchingPrice = storeItem.Prices.FirstOrDefault(x => x.CurrencyType == request.CurrencyType);
            if (matchingPrice == null)
                return BadRequest(new { error = "Invalid currency type for this item", success = false });

            if (matchingPrice.Price != request.RequestedPrice)
                return BadRequest(new { error = "Price mismatch", success = false });

            var player = PlayerDB.Players.FindById(account.Value);
            if (storeItem.GiftDrop.Tooltip.Contains("[DEV ONLY]") && !(AuthStuff.GetCurrentPlayer(Request)?.PlayerRoles?.Contains(PlayerDBClasses.PlayerRoles.Developer) ?? false))
                return BadRequest(new { error = "You are not a dev, you can not purchase this item.", success = false });

            int currentBalance = StorefrontsDB.GetBalance(account.Value, request.CurrencyType);
            if (currentBalance < request.RequestedPrice)
                return BadRequest(new { error = "Insufficient balance", success = false });

            long recipientId = account.Value;
            string giftMessage = "A gift for you <3";

            if (request.Gift != null)
            {
                recipientId = request.Gift.RecipientPlayerId > 0
                    ? request.Gift.RecipientPlayerId
                    : (request.Gift.ToPlayerId ?? 0);

                if (recipientId <= 0)
                    return BadRequest(new { error = "Invalid gift recipient", success = false });

                if (!string.IsNullOrWhiteSpace(request.Gift.Message))
                    giftMessage = request.Gift.Message;
            }

            var currency = StorefrontsDB.DeductCurrency(account.Value, request.CurrencyType, request.RequestedPrice);
            if (currency == null)
                return BadRequest(new { error = "Insufficient balance", success = false });

            var giftDrop = storeItem.GiftDrop;

            var createdGift = GiftsDB.CreateGift(
                toPlayerId: recipientId,
                fromPlayerId: account.Value,
                avatarItemDesc: giftDrop?.AvatarItemDesc ?? "",
                avatarItemType: giftDrop?.AvatarItemType ?? 0,
                balanceType: -2,
                consumableItemDesc: giftDrop?.ConsumableItemDesc ?? "",
                currency: giftDrop?.Currency ?? 0,
                currencyType: giftDrop?.CurrencyType ?? 0,
                equipmentModificationGuid: giftDrop?.EquipmentModificationGuid ?? "",
                equipmentPrefabName: giftDrop?.EquipmentPrefabName ?? "",
                giftContext: (GiftType)(account.Value != recipientId ? 500 : 200000),
                giftRarity: giftDrop?.Rarity ?? 50,
                level: 0,
                message: giftMessage,
                platform: -1,
                platformsToSpawnOn: -1,
                xp: 0
            );

            if (giftDrop?.Currency != null)
                await APIController.SendTokenEarningWebhook(recipientId, giftDrop.Currency.ToString(), 2);

            if (!string.IsNullOrWhiteSpace(giftDrop?.EquipmentModificationGuid) && !string.IsNullOrWhiteSpace(giftDrop?.EquipmentPrefabName))
            {
                PlayerDB.GrantEquipmentItem(
                    recipientId,
                    giftDrop.EquipmentModificationGuid,
                    giftDrop.EquipmentPrefabName,
                    giftDrop.FriendlyName,
                    giftDrop.Tooltip,
                    giftDrop.Rarity
                );
            }

            Console.WriteLine($"Player {account.Value} bought item for recipient {recipientId} with gift ID {createdGift.Id}. Is this a gift for someone else?: {account.Value != recipientId}. Comparison: {account.Value} != {recipientId}");

            if (account.Value != recipientId)
                await NotiController.SendEvent(recipientId, "31", GiftsDB.MapToDTO(createdGift));

            var response = new BuyItemResponse
            {
                Balance = -request.RequestedPrice,
                BalanceType = -2,
                CurrencyType = request.CurrencyType,
                BalanceUpdates = new List<BalanceUpdate>
                {
                    new BalanceUpdate
                    {
                        Data = new List<GiftDTO> { GiftsDB.MapToDTO(createdGift) },
                        UpdateResponse = 0
                    }
                }
            };

            return Ok(response);
        }

        [HttpPost("/api/gamerewards/v1/request")]
        public async Task<IActionResult> RequestGameReward([FromForm] string rewardType, [FromForm] string? Message)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            if (string.IsNullOrWhiteSpace(rewardType))
                return BadRequest(new SuccessResult { error = "Missing rewardType", success = false });

            await NotiController.SendEvent(account.Value, "GameRewardRequested", new
            {
                PlayerId = account.Value,
                RewardType = rewardType,
                Message = Message ?? ""
            });

            return Ok(new SuccessResult { error = "", success = true, value = null });
        }

        [HttpPost("/api/gamerewards/v1/select")]
        public async Task<IActionResult> SelectGameReward([FromForm] int rewardSelectionId, [FromForm] int giftDropId)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            var giftDrop = StorefrontsDB.GetGiftDropByIdAcrossAllStores(giftDropId);
            if (giftDrop == null)
                return NotFound(new { error = "GiftDrop not found", success = false });

            var createdGift = GiftsDB.CreateGift(
                toPlayerId: account.Value,
                fromPlayerId: 1,
                avatarItemDesc: giftDrop.AvatarItemDesc,
                avatarItemType: giftDrop.AvatarItemType,
                balanceType: -2,
                consumableItemDesc: giftDrop.ConsumableItemDesc,
                currency: giftDrop.Currency,
                currencyType: giftDrop.CurrencyType,
                equipmentModificationGuid: giftDrop.EquipmentModificationGuid,
                equipmentPrefabName: giftDrop.EquipmentPrefabName,
                giftContext: (GiftType)(giftDrop.Context != 0 ? giftDrop.Context : 110000),
                giftRarity: giftDrop.Rarity,
                level: 0,
                message: "A gift for you <3",
                platform: -1,
                platformsToSpawnOn: -1,
                xp: 0
            );

            await NotiController.SendEvent(account.Value, "GameRewardSelected", new
            {
                PlayerId = account.Value,
                GiftDropId = giftDropId,
                RewardSelectionId = rewardSelectionId,
                Gift = GiftsDB.MapToDTO(createdGift)
            });

            var response = new RewardSelectionResponse
            {
                AvatarItemDesc = giftDrop.AvatarItemDesc,
                AvatarItemType = giftDrop.AvatarItemType,
                ConsumableItemDesc = giftDrop.ConsumableItemDesc,
                Context = giftDrop.Context != 0 ? giftDrop.Context : 110000,
                Currency = giftDrop.Currency,
                CurrencyType = giftDrop.CurrencyType,
                EquipmentModificationGuid = giftDrop.EquipmentModificationGuid,
                EquipmentPrefabName = giftDrop.EquipmentPrefabName,
                FriendlyName = giftDrop.FriendlyName,
                GiftDropId = giftDrop.GiftDropId,
                IsQuery = giftDrop.IsQuery,
                ItemSetFriendlyName = giftDrop.ItemSetFriendlyName,
                ItemSetId = 1,
                Rarity = giftDrop.Rarity,
                SubscribersOnly = giftDrop.SubscribersOnly,
                Tooltip = giftDrop.Tooltip,
                Unique = giftDrop.Unique
            };

            return Ok(response);
        }

        [HttpGet("/api/gamerewards/v1/pending")]
        public async Task<IActionResult> GetPendingGameRewards()
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/storefronts/v2/buyInvention")]
        [HttpPost("/api/storefronts/v2/buyInvention")]
        public async Task<IActionResult> BuyInvention(
        [FromQuery] long inventionId,
        [FromQuery] long? requestedPrice = null,
        [FromQuery] int currencyType = 2)
    {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return StatusCode(403);

            var invention = InventionDB.Inventions.FindOne(i => i.InventionId == inventionId);
            if (invention == null)
                return NotFound(new { error = "Invention not found", success = false });

            if (requestedPrice.HasValue && requestedPrice.Value != invention.Price)
                return BadRequest(new { error = "Price mismatch", success = false });

            int currentBalance = StorefrontsDB.GetBalance(account.Value, currencyType);
            if (currentBalance < invention.Price)
                return BadRequest(new { error = "Insufficient balance", success = false });

            var currency = StorefrontsDB.DeductCurrency(account.Value, currencyType, invention.Price);
            if (currency == null)
                return BadRequest(new { error = "Insufficient balance", success = false });

            invention.DownloadIds.Add(account.Value);
            InventionDB.Inventions.Update(invention);

            var resolvedVersion = invention.CurrentVersion
                ?? (invention.Versions?.OrderByDescending(v => v.VersionNumber).FirstOrDefault())
                ?? InventionDB.GetLatestVersion(inventionId)
                ?? new InventionDBClasses.InventionVersion
                {
                    InventionId = invention.InventionId,
                    VersionNumber = invention.CurrentVersionNumber
                };

            var response = new InventionDBClasses.BuyInventionResponse
            {
                InventionResponse = new InventionDBClasses.InventionResponse
                {
                    Invention = invention,
                    InventionVersion = resolvedVersion,
                    StatusCode = 0
                },
                BalanceUpdateResponse = new StorefrontsDBClasses.BuyForFreeGiftButtonResponse
                {
                    Balance = -invention.Price,
                    BalanceType = 0,
                    CurrencyType = currencyType,
                    BalanceUpdates = new List<ConsumableBalanceUpdate>()
                }
            };

            return Ok(response);
        }
    }
}