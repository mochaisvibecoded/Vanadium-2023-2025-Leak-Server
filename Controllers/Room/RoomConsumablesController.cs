using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vanadium.Auth;
using Vanadium.Classes.DBs;
using static Vanadium.Classes.DBs.DBClasses.RoomConsumablesDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    public class RoomConsumablesController : ControllerBase
    {
        private static class Status
        {
            public const int Success = 0;
            public const int RoomConsumableNotFound = 4;
            public const int PlayerDoesntHavePermission = 7;
            public const int MaxConsumablesInRoom = 13;
            public const int RoomIdMissing = 15;
            public const int PriceOrCurrencyMissing = 17;
            public const int CurrencyNotFound = 18;
            public const int PriceTooLowTokens = 21;
            public const int PriceTooHighTokens = 22;
            public const int NameTooShort = 23;
            public const int NameTooLong = 24;
            public const int DescriptionTooLong = 27;
            public const int ConcurrencyCodeMismatch = 32;
            public const int PlayerDoesNotOwnConsumable = 33;
            public const int PurchaseFailed = 35;
            public const int RoomNotFound = 36;
            public const int RequestedPriceDoesNotMatch = 38;
            public const int RequestedCurrencyDoesNotMatch = 39;
            public const int ConsumableCannotBePurchasedWithRoomCurrency = 40;
            public const int ConsumableCannotBePurchasedWithTokens = 41;
        }

        private static class TokenOp
        {
            public const int OK = 0;
            public const int NotEnoughCredit = 2;
            public const int RequestedPriceDoesNotMatch = 6;
        }

        private static class CurrencyOp
        {
            public const int Success = 0;
            public const int NotEnoughCredit = 1;
        }

        private const int TokenCurrencyType = 2;
        private const int MaxConsumablesPerRoom = 50;

        [HttpGet("/econ/roomInventory/room/{roomId:long}")]
        [HttpGet("/api/roomconsumables/v1/roomConsumable/room/{roomId:long}")]
        public IActionResult ListForRoom(long roomId) =>
            Ok(RoomConsumablesDB.GetConsumablesForRoom(roomId).Select(ToDescWire).ToList());

        [HttpGet("/econ/roomInventory/room/{roomId:long}/player")]
        [HttpGet("/api/roomconsumables/v1/roomConsumable/room/{roomId:long}/me")]
        public IActionResult MineForRoom(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var rows = RoomConsumablesDB.GetInventoryForRoom(id.Value, roomId);
            return Ok(rows.Select(r => ToInventoryWire(r.Ownership, r.Item)).ToList());
        }

        [HttpGet("/api/roomconsumables/v1/roomConsumable/{roomConsumableId:guid}")]
        public IActionResult GetOne(Guid roomConsumableId)
        {
            var item = RoomConsumablesDB.GetConsumable(roomConsumableId);
            return item == null ? NotFound("") : Ok(ToDescWire(item));
        }

        [HttpGet("/api/roomconsumables/v1/roomConsumable/{roomConsumableId:guid}/isOwned")]
        public IActionResult IsOwned(Guid roomConsumableId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var item = RoomConsumablesDB.GetConsumable(roomConsumableId);
            if (item == null) return Content("false", "application/json");

            return Content(RoomConsumablesDB.IsOwnedByOthers(item) ? "true" : "false", "application/json");
        }

        [HttpPost("/api/roomconsumables/v1/roomConsumable")]
        [HttpPut("/api/roomconsumables/v1/roomConsumable")]
        public async Task<IActionResult> CreateOrUpdate()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var body = await ReadBodyAsync();
            var name = (ReadString(body, "Name") ?? "").Trim();
            var description = (ReadString(body, "Description") ?? "").Trim();
            var imageName = (ReadString(body, "ImageName") ?? "").Trim();
            var (price, currencyId, hasPrice) = ReadPriceAndCurrency(body);

            var publicId = ReadGuid(body, "RoomConsumableId");
            var existing = publicId is Guid pid && pid != Guid.Empty
                ? RoomConsumablesDB.GetConsumable(pid)
                : null;

            if (existing == null)
            {
                var roomId = ReadLong(body, "RoomId");
                if (roomId is not long rid || rid <= 0) return Ok(EditResponse(Status.RoomIdMissing));
                if (RoomDB.GetRoom(rid) == null) return Ok(EditResponse(Status.RoomNotFound));
                if (!RoomDB.UserCanEditRoom(rid, id.Value))
                    return Ok(EditResponse(Status.PlayerDoesntHavePermission));

                if (ValidateFields(name, description, hasPrice, price) is int fieldError)
                    return Ok(EditResponse(fieldError));
                if (RoomConsumablesDB.CountConsumablesInRoom(rid) >= MaxConsumablesPerRoom)
                    return Ok(EditResponse(Status.MaxConsumablesInRoom));

                var created = RoomConsumablesDB.CreateConsumable(
                    rid, id.Value, name, description, imageName, price, currencyId);
                return Ok(EditResponse(Status.Success, created));
            }

            if (!RoomDB.UserCanEditRoom(existing.RoomId, id.Value))
                return Ok(EditResponse(Status.PlayerDoesntHavePermission, existing));

            if (ValidateFields(name, description, hasPrice, price) is int updateError)
                return Ok(EditResponse(updateError, existing));

            RoomConsumablesDB.UpdateConsumable(existing, name, description, imageName, price, currencyId);
            return Ok(EditResponse(Status.Success, existing));
        }

        [HttpDelete("/api/roomconsumables/v1/roomConsumable/{roomConsumableId:guid}")]
        [HttpPost("/api/roomconsumables/v1/roomConsumable/{roomConsumableId:guid}")]
        public IActionResult Delete(Guid roomConsumableId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var item = RoomConsumablesDB.GetConsumable(roomConsumableId);
            if (item == null) return StatusBody(Status.RoomConsumableNotFound);

            if (!RoomDB.UserCanEditRoom(item.RoomId, id.Value))
                return StatusBody(Status.PlayerDoesntHavePermission);

            RoomConsumablesDB.DeleteConsumable(roomConsumableId);
            return StatusBody(Status.Success);
        }

        [HttpPut("/api/roomconsumables/v1/roomconsumable/{roomConsumableId:guid}/purchase/tokens")]
        [HttpPost("/api/roomconsumables/v1/roomconsumable/{roomConsumableId:guid}/purchase/tokens")]
        public async Task<IActionResult> PurchaseTokens(Guid roomConsumableId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var item = RoomConsumablesDB.GetConsumable(roomConsumableId);
            if (item == null) return Ok(CurrencyTokenResponse(null, Status.RoomConsumableNotFound, 0));

            var body = await ReadBodyAsync();
            var (price, _, hasPrice) = ReadPriceAndCurrency(body, "ExpectedPriceAndCurrency");
            var newCode = ReadConcurrency(body).NewCode;
            var balance = StorefrontsDB.GetBalance(id.Value, TokenCurrencyType);

            if (item.CurrencyId != null)
                return Ok(CurrencyTokenResponse(null, Status.ConsumableCannotBePurchasedWithTokens, balance));
            if (!hasPrice || price != item.Price)
                return Ok(CurrencyTokenResponse(
                    TokenOp.RequestedPriceDoesNotMatch, Status.RequestedPriceDoesNotMatch, balance));
            if (balance < item.Price)
                return Ok(CurrencyTokenResponse(TokenOp.NotEnoughCredit, Status.PurchaseFailed, balance));

            var newBalance = balance;
            if (item.Price > 0)
            {
                var deducted = StorefrontsDB.DeductCurrency(id.Value, TokenCurrencyType, item.Price);
                if (deducted == null)
                    return Ok(CurrencyTokenResponse(TokenOp.NotEnoughCredit, Status.PurchaseFailed, balance));
                newBalance = deducted.Balance;

                if (item.CreatorPlayerId != id.Value)
                    StorefrontsDB.AddCurrency(item.CreatorPlayerId, TokenCurrencyType, item.Price);
            }

            var own = RoomConsumablesDB.AddToInventory(id.Value, item.RoomConsumableId, newCode);
            return Ok(CurrencyTokenResponse(TokenOp.OK, Status.Success, newBalance, ToInventoryWire(own, item)));
        }

        [HttpPut("/api/roomconsumables/v1/roomconsumable/{roomConsumableId:guid}/purchase/currency")]
        [HttpPost("/api/roomconsumables/v1/roomconsumable/{roomConsumableId:guid}/purchase/currency")]
        public async Task<IActionResult> PurchaseCurrency(Guid roomConsumableId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var item = RoomConsumablesDB.GetConsumable(roomConsumableId);
            if (item == null)
                return Ok(CurrencyResponse(null, Status.RoomConsumableNotFound, id.Value, Guid.Empty, 0));
            if (item.CurrencyId is not Guid currencyId)
                return Ok(CurrencyResponse(
                    null, Status.ConsumableCannotBePurchasedWithRoomCurrency, id.Value, Guid.Empty, 0));

            var body = await ReadBodyAsync();
            var (price, requestedCurrency, hasPrice) = ReadPriceAndCurrency(body, "ExpectedPriceAndCurrency");
            var newCode = ReadConcurrency(body).NewCode;
            var balance = RoomConsumablesDB.GetCurrencyBalance(id.Value, currencyId);

            if (!hasPrice || price != item.Price)
                return Ok(CurrencyResponse(
                    null, Status.RequestedPriceDoesNotMatch, id.Value, currencyId, balance));
            if (requestedCurrency is Guid reqCur && reqCur != currencyId)
                return Ok(CurrencyResponse(
                    null, Status.RequestedCurrencyDoesNotMatch, id.Value, currencyId, balance));
            if (balance < item.Price)
                return Ok(CurrencyResponse(
                    CurrencyOp.NotEnoughCredit, Status.PurchaseFailed, id.Value, currencyId, balance));

            var newBalance = balance;
            if (item.Price > 0)
            {
                var deducted = RoomConsumablesDB.DeductCurrencyBalance(id.Value, currencyId, item.Price);
                if (deducted is not int remaining)
                    return Ok(CurrencyResponse(
                        CurrencyOp.NotEnoughCredit, Status.PurchaseFailed, id.Value, currencyId, balance));
                newBalance = remaining;
            }

            var own = RoomConsumablesDB.AddToInventory(id.Value, item.RoomConsumableId, newCode);
            return Ok(CurrencyResponse(CurrencyOp.Success, Status.Success, id.Value, currencyId,
                newBalance, ToInventoryWire(own, item)));
        }

        [HttpPut("/api/roomconsumables/v1/roomConsumable/{roomConsumableId:guid}/consume")]
        [HttpPost("/api/roomconsumables/v1/roomConsumable/{roomConsumableId:guid}/consume")]
        public async Task<IActionResult> Consume(Guid roomConsumableId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var item = RoomConsumablesDB.GetConsumable(roomConsumableId);
            if (item == null)
                return Ok(new { Status = Status.RoomConsumableNotFound, InventoryItem = (object?)null });

            var own = RoomConsumablesDB.GetOwnership(id.Value, item.RoomConsumableId);
            if (own == null || own.Count <= 0)
                return Ok(new
                {
                    Status = Status.PlayerDoesNotOwnConsumable,
                    InventoryItem = own == null ? null : ToInventoryWire(own, item)
                });

            var (currentCode, newCode) = ReadConcurrency(await ReadBodyAsync());
            if (currentCode is Guid current && current != own.ConcurrencyCode)
                return Ok(new { Status = Status.ConcurrencyCodeMismatch, InventoryItem = ToInventoryWire(own, item) });

            RoomConsumablesDB.ConsumeOne(own, newCode);
            return Ok(new { Status = Status.Success, InventoryItem = ToInventoryWire(own, item) });
        }

        private static int? ValidateFields(string name, string description, bool hasPrice, int price)
        {
            if (name.Length == 0) return Status.NameTooShort;
            if (name.Length > 128) return Status.NameTooLong;
            if (description.Length > 1024) return Status.DescriptionTooLong;
            if (!hasPrice) return Status.PriceOrCurrencyMissing;
            if (price < 0) return Status.PriceTooLowTokens;
            if (price > 1_000_000) return Status.PriceTooHighTokens;
            return null;
        }

        private IActionResult StatusBody(int status) => Content(status.ToString(), "application/json");

        private static object EditResponse(int status, RoomConsumable? item = null) => new
        {
            Status = status,
            Consumable = item == null ? null : ToDescWire(item)
        };

        private static object CurrencyTokenResponse(
            int? balanceUpdateResult, int status, int balance, object? inventoryItem = null) => new
        {
            OperationResult = new { Status = status, InventoryItem = inventoryItem },
            BalanceUpdateResult = balanceUpdateResult,
            TokenBalanceResponse = new
            {
                CurrencyType = TokenCurrencyType,
                Balance = balance,
                Platform = 0
            }
        };

        private static object CurrencyResponse(
            int? balanceUpdateResult, int status, long playerId, Guid currencyId, int balance,
            object? inventoryItem = null) => new
        {
            OperationResult = new { Status = status, InventoryItem = inventoryItem },
            BalanceUpdateResult = balanceUpdateResult,
            CurrencyBalanceResponse = new
            {
                AccountId = (int)playerId,
                CurrencyId = currencyId,
                Balance = balance,
                ModifiedAt = DateTime.UtcNow
            }
        };

        private static object ToDescWire(RoomConsumable c) => new
        {
            RoomConsumableId = c.RoomConsumableId,
            c.RoomId,
            c.Name,
            Description = c.Description ?? "",
            ImageName = c.ImageName ?? "",
            c.Price,
            PurchaseCurrencyId = c.CurrencyId,
            ModifiedAt = DateTime.SpecifyKind(c.ModifiedAt, DateTimeKind.Utc)
        };

        private static object ToInventoryWire(RoomConsumableOwnership own, RoomConsumable item) => new
        {
            RoomConsumableId = item.RoomConsumableId,
            AccountId = (int)own.PlayerId,
            Count = Math.Max(0, own.Count),
            own.ConcurrencyCode,
            ModifiedAt = DateTime.SpecifyKind(own.ModifiedAt, DateTimeKind.Utc),
            Consumable = ToDescWire(item)
        };

        private async Task<Dictionary<string, JsonElement>> ReadBodyAsync()
        {
            var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (Request.ContentLength is 0) return fields;

            try
            {
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return fields;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    fields[prop.Name] = prop.Value.Clone();
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                        foreach (var inner in prop.Value.EnumerateObject())
                            fields.TryAdd(inner.Name, inner.Value.Clone());
                }
            }
            catch (JsonException)
            {
            }

            return fields;
        }

        private static string? ReadString(Dictionary<string, JsonElement> fields, string name) =>
            fields.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static long? ReadLong(Dictionary<string, JsonElement> fields, string name)
        {
            if (!fields.TryGetValue(name, out var v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.Number when v.TryGetInt64(out var n) => n,
                JsonValueKind.String when long.TryParse(v.GetString(), out var n) => n,
                _ => null
            };
        }

        private static Guid? ReadGuid(Dictionary<string, JsonElement> fields, string name) =>
            fields.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String
            && Guid.TryParse(v.GetString(), out var guid)
                ? guid
                : null;

        /// <summary>Read { Price, CurrencyId } from the body, whether it arrives nested under
        /// <paramref name="wrapper"/> / PriceAndCurrency or flattened at the root.</summary>
        private static (int Price, Guid? CurrencyId, bool HasPrice) ReadPriceAndCurrency(
            Dictionary<string, JsonElement> fields, string wrapper = "PriceAndCurrency")
        {
            var scope = fields;
            if (fields.TryGetValue(wrapper, out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                scope = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in nested.EnumerateObject()) scope[prop.Name] = prop.Value;
            }

            var price = ReadLong(scope, "Price");
            return ((int)(price ?? 0), ReadGuid(scope, "CurrencyId"), price != null);
        }

        private static (Guid? CurrentCode, Guid? NewCode) ReadConcurrency(Dictionary<string, JsonElement> fields)
        {
            var scope = fields;
            if (fields.TryGetValue("ConcurrencyCodes", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                scope = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in nested.EnumerateObject()) scope[prop.Name] = prop.Value;
            }

            return (ReadGuid(scope, "CurrentConcurrencyCode"), ReadGuid(scope, "NewConcurrencyCode"));
        }
    }
}
