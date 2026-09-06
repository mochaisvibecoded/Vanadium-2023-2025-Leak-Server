using LiteDB;
using System.Net;
using System.Text.Json;
using static Vanadium.Classes.DBs.DBClasses.CustomClothingDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Classes.DBs
{
    public class CustomClothingDB
    {
        public static LiteDatabase CustomClothingDBFile = new LiteDatabase(
            "Filename=" + Path.Join(Program.dataDir, "DBs", "CustomClothing.db") + ";Connection=shared");

        public static readonly ILiteCollection<CustomClothingItem> CustomClothing =
            CustomClothingDBFile.GetCollection<CustomClothingItem>("CustomClothing");

        private static readonly HttpClient Http = new HttpClient();
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public static List<CustomClothingItem> GetByCreator(long creatorId, bool includePrivate)
        {
            if (includePrivate)
                return CustomClothing.Find(x => x.CreatorAccountId == creatorId).ToList();
            return CustomClothing.Find(x => x.CreatorAccountId == creatorId && x.Accessibility == ClothingAccessibility.Public).ToList();
        }

        public static int CountByCreator(long creatorId, bool includePrivate)
        {
            if (includePrivate)
                return CustomClothing.Count(x => x.CreatorAccountId == creatorId);
            return CustomClothing.Count(x => x.CreatorAccountId == creatorId && x.Accessibility == ClothingAccessibility.Public);
        }

        public static List<CustomClothingItem> GetBulk(List<Guid> ids)
        {
            return CustomClothing.Find(x => ids.Contains(x.Id)).ToList();
        }

        public static CustomClothingItem? FindById(Guid id)
        {
            return CustomClothing.FindById(id);
        }

        public static void Insert(CustomClothingItem item)
        {
            CustomClothing.Insert(item);
        }

        public static void Update(CustomClothingItem item)
        {
            CustomClothing.Update(item);
        }
        
        public static List<CustomClothingItem> GetFeatured(int limit = 20) // expermintal
        {
            return CustomClothing.FindAll()
                .Take(limit) 
                .ToList();
        }
        
        public static List<CustomClothingItem> GetHot(int limit = 20) // expermintal
        {
            return CustomClothing.FindAll()
                .Take(limit)
                .ToList();
        }

        public static long GetOrCreateImportAccountId()
        {
            var existing = PlayerDB.Players.FindOne(x => x.Player != null && x.Player.Username == "Import");
            if (existing != null)
                return existing.PlayerId;

            var importPlayer = new FullPlayer
            {
                PlatformIds = new List<mPlatformID>(),
                Player = new Player
                {
                    Username = "Import",
                    DisplayName = "Import",
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = DateTime.UtcNow,
                    ProfileImage = "DefaultPFP.png",
                    AvailableUsernameChanges = 0,
                },
                PlayerRoles = new List<PlayerRoles> { PlayerRoles.Developer },
                AuthToken = Guid.NewGuid().ToString()
            };

            PlayerDB.Players.Insert(importPlayer);
            return importPlayer.PlayerId;
        }
    }
}