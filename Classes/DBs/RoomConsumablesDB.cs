using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Vanadium.Classes.DBs.DBClasses.RoomConsumablesDBClasses;

namespace Vanadium.Classes.DBs
{
    public class RoomConsumablesDB
    {
        public static LiteDatabase RoomConsumablesDBFile = new LiteDatabase(
            "Filename=" + Path.Join(Program.dataDir, "DBs", "RoomConsumables.db") + ";Connection=shared");

        public static readonly ILiteCollection<RoomConsumable> Consumables =
            RoomConsumablesDBFile.GetCollection<RoomConsumable>("RoomConsumables");

        public static readonly ILiteCollection<RoomConsumableOwnership> Ownerships =
            RoomConsumablesDBFile.GetCollection<RoomConsumableOwnership>("RoomConsumableOwnerships");

        public static readonly ILiteCollection<RoomCurrencyBalance> CurrencyBalances =
            RoomConsumablesDBFile.GetCollection<RoomCurrencyBalance>("RoomCurrencyBalances");

        static RoomConsumablesDB()
        {
            Consumables.EnsureIndex(x => x.RoomId);
            Consumables.EnsureIndex(x => x.CreatorPlayerId);
            Ownerships.EnsureIndex(x => x.PlayerId);
            Ownerships.EnsureIndex(x => x.RoomConsumableId);
            CurrencyBalances.EnsureIndex(x => x.PlayerId);
            CurrencyBalances.EnsureIndex(x => x.CurrencyId);
        }

        public static RoomConsumable? GetConsumable(Guid roomConsumableId)
        {
            var item = Consumables.FindById(roomConsumableId);
            return item == null || item.IsDeleted ? null : item;
        }

        public static List<RoomConsumable> GetConsumablesForRoom(long roomId, int take = 200) =>
            Consumables.Find(c => c.RoomId == roomId && !c.IsDeleted)
                .OrderBy(c => c.CreatedAt)
                .Take(take)
                .ToList();

        public static int CountConsumablesInRoom(long roomId) =>
            Consumables.Count(c => c.RoomId == roomId && !c.IsDeleted);

        public static RoomConsumable CreateConsumable(
            long roomId, long creatorPlayerId, string name, string? description,
            string? imageName, int price, Guid? currencyId)
        {
            var item = new RoomConsumable
            {
                RoomConsumableId = Guid.NewGuid(),
                RoomId = roomId,
                CreatorPlayerId = creatorPlayerId,
                Name = name,
                Description = description,
                ImageName = imageName,
                Price = price,
                CurrencyId = currencyId,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            Consumables.Insert(item);
            return item;
        }

        public static bool UpdateConsumable(
            RoomConsumable item, string name, string? description,
            string? imageName, int price, Guid? currencyId)
        {
            item.Name = name;
            item.Description = description;
            item.ImageName = imageName;
            item.Price = price;
            item.CurrencyId = currencyId;
            item.ModifiedAt = DateTime.UtcNow;
            return Consumables.Update(item);
        }
        public static bool DeleteConsumable(Guid roomConsumableId)
        {
            var item = Consumables.FindById(roomConsumableId);
            if (item == null || item.IsDeleted) return false;

            item.IsDeleted = true; // so ppl with it dont break
            item.ModifiedAt = DateTime.UtcNow;
            return Consumables.Update(item);
        }

        public static void DeleteAllConsumablesForRoom(long roomId)
        {
            foreach (var item in Consumables.Find(c => c.RoomId == roomId && !c.IsDeleted).ToList())
            {
                item.IsDeleted = true;
                item.ModifiedAt = DateTime.UtcNow;
                Consumables.Update(item);
            }
        }

        public static RoomConsumableOwnership? GetOwnership(long playerId, Guid roomConsumableId) =>
            Ownerships.FindOne(o => o.PlayerId == playerId && o.RoomConsumableId == roomConsumableId);

        public static List<(RoomConsumableOwnership Ownership, RoomConsumable Item)> GetInventoryForRoom(
            long playerId, long roomId, int take = 200)
        {
            var items = Consumables.Find(c => c.RoomId == roomId && !c.IsDeleted)
                .OrderBy(c => c.CreatedAt)
                .ToList();
            if (items.Count == 0) return new List<(RoomConsumableOwnership, RoomConsumable)>();

            var owned = Ownerships.Find(o => o.PlayerId == playerId)
                .GroupBy(o => o.RoomConsumableId)
                .ToDictionary(g => g.Key, g => g.First());

            return items
                .Where(i => owned.ContainsKey(i.RoomConsumableId))
                .Take(take)
                .Select(i => (owned[i.RoomConsumableId], i))
                .ToList();
        }
        public static bool IsOwnedByOthers(RoomConsumable item) =>
            Ownerships.Exists(o => o.RoomConsumableId == item.RoomConsumableId
                                && o.Count > 0
                                && o.PlayerId != item.CreatorPlayerId);

        public static RoomConsumableOwnership AddToInventory(long playerId, Guid roomConsumableId, Guid? newCode)
        {
            var own = GetOwnership(playerId, roomConsumableId);
            bool isNew = own == null;
            own ??= new RoomConsumableOwnership
            {
                PlayerId = playerId,
                RoomConsumableId = roomConsumableId
            };

            own.Count += 1;
            if (newCode is Guid code && code != Guid.Empty) own.ConcurrencyCode = code;
            own.ModifiedAt = DateTime.UtcNow;

            if (isNew) Ownerships.Insert(own);
            else Ownerships.Update(own);

            return own;
        }

        public static void ConsumeOne(RoomConsumableOwnership own, Guid? newCode)
        {
            own.Count -= 1;
            own.ConcurrencyCode = newCode is Guid code && code != Guid.Empty ? code : Guid.NewGuid();
            own.ModifiedAt = DateTime.UtcNow;
            Ownerships.Update(own);
        }

        public static int GetCurrencyBalance(long playerId, Guid currencyId) =>
            CurrencyBalances.FindOne(b => b.PlayerId == playerId && b.CurrencyId == currencyId)?.Balance ?? 0;

        public static int AddCurrencyBalance(long playerId, Guid currencyId, int amount)
        {
            var row = CurrencyBalances.FindOne(b => b.PlayerId == playerId && b.CurrencyId == currencyId);
            if (row == null)
            {
                row = new RoomCurrencyBalance
                {
                    PlayerId = playerId,
                    CurrencyId = currencyId,
                    Balance = amount,
                    ModifiedAt = DateTime.UtcNow
                };
                CurrencyBalances.Insert(row);
                return row.Balance;
            }

            row.Balance += amount;
            row.ModifiedAt = DateTime.UtcNow;
            CurrencyBalances.Update(row);
            return row.Balance;
        }

        /// <summary>Deduct if affordable; returns the new balance, or null when the player is short.</summary>
        public static int? DeductCurrencyBalance(long playerId, Guid currencyId, int amount)
        {
            var row = CurrencyBalances.FindOne(b => b.PlayerId == playerId && b.CurrencyId == currencyId);
            if (row == null || row.Balance < amount) return null;

            row.Balance -= amount;
            row.ModifiedAt = DateTime.UtcNow;
            CurrencyBalances.Update(row);
            return row.Balance;
        }
    }
}
