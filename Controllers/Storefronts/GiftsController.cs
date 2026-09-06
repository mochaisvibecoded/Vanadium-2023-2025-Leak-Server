using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Utils.NotiController;
using static Vanadium.Classes.DBs.DBClasses.GiftsDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    public partial class GiftsController : ControllerBase
    {
        [HttpGet("/api/avatar/v2/gifts")]
        public async Task<IActionResult> GetMyGifts()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var gifts = GiftsDB.GetUnconsumedGifts(id.Value);
            return Ok(gifts);
        }

        [HttpPost("/api/avatar/v2/gifts/consume/")]
        public async Task<IActionResult> ConsumeGift([FromForm] long Id, [FromForm] int UnlockedLevel = 0)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            var gift = GiftsDB.GetGiftById(Id);
            if (gift == null)
                return NotFound("");

            if (gift.ToPlayerId != playerId.Value)
                return StatusCode(403);

            bool consumed = GiftsDB.ConsumeGift(Id, playerId.Value);
            if (!consumed)
                return BadRequest(new { error = "Gift already consumed or not found", success = false, value = (object?)null });

            var player = PlayerDB.Players.FindById(playerId.Value);
            bool playerUpdated = false;

            Console.WriteLine($"Comparing Gift Currency: {gift.Currency} > 0 && {gift.CurrencyType} != 0");
            if (gift.Currency > 0 && gift.CurrencyType != 0)
            {
                Console.WriteLine($"Adding currency to player {playerId.Value}: {gift.Currency} of type {gift.CurrencyType}");
                if (player?.Player?.PlayerExtra != null)
                {
                    player.Player.PlayerExtra.Currencies ??= new List<PlayerDBClasses.PlayerCurrency>();
                    var currency = player.Player.PlayerExtra.Currencies
                        .FirstOrDefault(x => (int)x.CurrencyType == gift.CurrencyType);

                    Console.WriteLine($"About to set currency balance. Current balance: {currency?.Balance ?? 0}, Gift currency: {gift.Currency}");

                    if (currency != null)
                        currency.Balance += gift.Currency;
                    else
                        player.Player.PlayerExtra.Currencies.Add(new PlayerDBClasses.PlayerCurrency
                        {
                            Balance = gift.Currency,
                            CurrencyType = (PlayerDBClasses.CurrencyType)gift.CurrencyType,
                            BalanceType = (PlayerDBClasses.BalanceType)gift.BalanceType
                        });

                    Console.WriteLine($"Updated player {playerId.Value} currency. New balance: {currency?.Balance ?? gift.Currency}");
                    playerUpdated = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(gift.EquipmentModificationGuid) && player?.Player != null)
            {
                string equipPath = Path.Join(Program.dataDir, "APIS", "Items", "Equipment.json");
                if (System.IO.File.Exists(equipPath))
                {
                    var allItems = System.Text.Json.JsonSerializer.Deserialize<List<PlayerDBClasses.OwnedEquipmentItem>>(
                        System.IO.File.ReadAllText(equipPath),
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    var target = allItems?.FirstOrDefault(i =>
                        string.Equals(i.ModificationGuid, gift.EquipmentModificationGuid, StringComparison.OrdinalIgnoreCase));

                    if (target != null)
                    {
                        player.Player.PlayerExtra ??= new PlayerDBClasses.PlayerExtra();
                        player.Player.PlayerExtra.OwnedEquipment ??= new List<PlayerDBClasses.OwnedEquipmentItem>();

                        bool owns = player.Player.PlayerExtra.OwnedEquipment
                            .Any(e => string.Equals(e.ModificationGuid, target.ModificationGuid, StringComparison.OrdinalIgnoreCase));

                        if (!owns)
                        {
                            player.Player.PlayerExtra.OwnedEquipment.Add(new PlayerDBClasses.OwnedEquipmentItem
                            {
                                PrefabName = target.PrefabName,
                                ModificationGuid = target.ModificationGuid,
                                FriendlyName = target.FriendlyName,
                                PlatformMask = target.PlatformMask,
                                Tooltip = target.Tooltip,
                                Rarity = target.Rarity,
                                Favorited = false
                            });
                            playerUpdated = true;
                        }
                    }
                }
            }

            if (playerUpdated && player != null)
                PlayerDB.Players.Update(player);

            var giftDto = GiftsDB.MapToDTO(gift);
            await NotiController.SendEvent(playerId.Value, "30", giftDto);

            var remainingGifts = GiftsDB.GetUnconsumedGifts(playerId.Value);
            await NotiController.SendEvent(playerId.Value, "GiftConsumed", new
            {
                GiftId = Id,
                RemainingGifts = remainingGifts
            });

            return Ok(new { error = "", success = true, value = (object?)null });
        }
    }
}