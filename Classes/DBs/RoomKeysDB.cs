using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vanadium.Classes;
using static Vanadium.Classes.DBs.DBClasses.RoomKeysDBClasses;

namespace Vanadium.Classes.DBs
{
    public class RoomKeysDB
    {
        public static LiteDatabase RoomKeysDBFile = new LiteDatabase(
            "Filename=" + Path.Join(Program.dataDir, "DBs", "RoomKeys.db") + ";Connection=shared");

        public static readonly ILiteCollection<RoomKey> RoomKeys =
            RoomKeysDBFile.GetCollection<RoomKey>("RoomKeys");

        public static readonly ILiteCollection<RoomKeyOwnership> Ownerships =
            RoomKeysDBFile.GetCollection<RoomKeyOwnership>("RoomKeyOwnerships");

        public static void Setup()
        {
            RoomKeys.EnsureIndex(x => x.RoomKeyId);
            RoomKeys.EnsureIndex(x => x.RoomId);
            Ownerships.EnsureIndex(x => x.PlayerId);
            Ownerships.EnsureIndex(x => x.RoomKeyId);
        }

        public static long GetNextRoomKeyId()
        {
            if (RoomKeys.Count() == 0) return 1;
            return RoomKeys.Max(x => x.RoomKeyId) + 1;
        }

        public static RoomKey? CreateRoomKey(long roomId, string name, string? description, int price, string? purchaseCurrencyId)
        {
            var key = new RoomKey
            {
                RoomKeyId = GetNextRoomKeyId(),
                RoomId = roomId,
                Name = name,
                Description = description,
                Price = price,
                PurchaseCurrencyId = string.IsNullOrEmpty(purchaseCurrencyId) ? null : purchaseCurrencyId,
                ReplicationId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow
            };

            RoomKeys.Insert(key);
            return key;
        }

        public static RoomKey? GetRoomKey(long roomKeyId) => RoomKeys.FindById(roomKeyId);

        public static List<RoomKey> GetRoomKeysByRoom(long roomId) =>
            RoomKeys.Find(k => k.RoomId == roomId).ToList();

        public static List<RoomKey> GetRoomKeysByPlayer(long playerId)
        {
            var ownedIds = Ownerships
                .Find(o => o.PlayerId == playerId)
                .Select(o => o.RoomKeyId)
                .ToHashSet();

            return RoomKeys.Find(k => ownedIds.Contains(k.RoomKeyId)).ToList();
        }

        public static bool UpdateRoomKeyAll(long roomKeyId, string name, string? description, int price, string? purchaseCurrencyId)
        {
            var key = RoomKeys.FindById(roomKeyId);
            if (key == null) return false;

            key.Name = name;
            key.Description = description;
            key.Price = price;
            key.PurchaseCurrencyId = string.IsNullOrEmpty(purchaseCurrencyId) ? null : purchaseCurrencyId;

            return RoomKeys.Update(key);
        }

        public static bool UpdateRoomKeyName(long roomKeyId, string name)
        {
            var key = RoomKeys.FindById(roomKeyId);
            if (key == null) return false;
            key.Name = name;
            return RoomKeys.Update(key);
        }

        public static bool UpdateRoomKeyDescription(long roomKeyId, string description)
        {
            var key = RoomKeys.FindById(roomKeyId);
            if (key == null) return false;
            key.Description = description;
            return RoomKeys.Update(key);
        }

        public static bool UpdateRoomKeyPrice(long roomKeyId, int price, string? purchaseCurrencyId)
        {
            var key = RoomKeys.FindById(roomKeyId);
            if (key == null) return false;
            key.Price = price;
            key.PurchaseCurrencyId = string.IsNullOrEmpty(purchaseCurrencyId) ? null : purchaseCurrencyId;
            return RoomKeys.Update(key);
        }

        public static bool DeleteRoomKey(long roomKeyId)
        {
            Ownerships.DeleteMany(o => o.RoomKeyId == roomKeyId);
            return RoomKeys.Delete(roomKeyId);
        }

        public static bool PlayerOwnsKey(long playerId, long roomKeyId) =>
            Ownerships.FindOne(o => o.PlayerId == playerId && o.RoomKeyId == roomKeyId) != null;

        public static bool HasPurchasedKey(long playerId, long roomKeyId) =>
            Ownerships.FindOne(o => o.PlayerId == playerId && o.RoomKeyId == roomKeyId && o.WasPurchased) != null;

        public static bool AwardKey(long playerId, long roomKeyId)
        {
            if (PlayerOwnsKey(playerId, roomKeyId)) return false;

            Ownerships.Insert(new RoomKeyOwnership
            {
                PlayerId = playerId,
                RoomKeyId = roomKeyId,
                WasPurchased = false,
                AwardedAt = DateTime.UtcNow
            });

            return true;
        }

        public static bool GrantPurchasedKey(long playerId, long roomKeyId)
        {
            if (PlayerOwnsKey(playerId, roomKeyId)) return false;

            Ownerships.Insert(new RoomKeyOwnership
            {
                PlayerId = playerId,
                RoomKeyId = roomKeyId,
                WasPurchased = true,
                AwardedAt = DateTime.UtcNow
            });

            return true;
        }

        public static bool RevokeKey(long playerId, long roomKeyId)
        {
            var ownership = Ownerships.FindOne(o => o.PlayerId == playerId && o.RoomKeyId == roomKeyId);
            if (ownership == null) return false;
            return Ownerships.Delete(ownership.Id);
        }

        public static void DeleteAllKeysForRoom(long roomId)
        {
            var keyIds = RoomKeys.Find(k => k.RoomId == roomId).Select(k => k.RoomKeyId).ToList();
            foreach (var id in keyIds)
            {
                Ownerships.DeleteMany(o => o.RoomKeyId == id);
                RoomKeys.Delete(id);
            }
        }
    }
}