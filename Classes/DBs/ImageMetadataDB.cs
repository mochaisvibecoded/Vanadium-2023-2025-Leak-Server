using LiteDB;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

public static partial class ImageMetadataDB
{
    private static readonly string DbPath = Path.Combine("Data", "DBs", "Images.db");

    private static readonly Lazy<LiteDatabase> _dbInstance = new(() => new LiteDatabase("Filename=" + DbPath + ";Connection=shared"));
    public static LiteDatabase GetDb() => _dbInstance.Value;

    public static FullSavedImage InsertImage(FullSavedImage image)
    {
        var db = GetDb();
        var col = db.GetCollection<FullSavedImage>("images");
        col.EnsureIndex(x => x.Id, true);

        if (image.Id == 0)
        {
            var last = col.Query()
                          .OrderByDescending(x => x.Id)
                          .Limit(1)
                          .FirstOrDefault();

            image.Id = (last?.Id ?? 0) + 1;
        }

        col.Insert(image);
        return image;
    }

    public static ulong? GetPlayerIdFromImageName(string imageName)
    {
        if (string.IsNullOrEmpty(imageName))
            return null;

        var db = GetDb();
        var col = db.GetCollection<FullSavedImage>("images");

        col.EnsureIndex(x => x.ImageName);

        var record = col.FindOne(x => x.ImageName == imageName);

        return record?.PlayerId;
    }

    public static List<FullSavedImage> GetRoomImages(
        ulong playerId,
        long roomId,
        int sort = 0,
        int filter = 0,
        int take = 50,
        int skip = 0)
    {
        var db = GetDb();
        var col = db.GetCollection<FullSavedImage>("images");

        var query = col.Query()
               .Where(x => x.RoomId == roomId &&
                           x.Accessibility == ImageAccessibility.Public);

        switch (filter)
        {
            case 2:
                query = query.Where(x => x.PlayerId == playerId);
                break;

            case 3:
                query = query.Where(x => x.CheerCount > 0);
                break;
        }

        if (sort == 0 && filter == 1)
        {
            query = query.OrderByDescending(x => x.CreatedAt);
        }
        else
        {
            query = sort switch
            {
                1 => query.OrderByDescending(x => x.CheerCount),
                2 => query.OrderByDescending(x => x.CreatedAt),
                3 => query.OrderByDescending(x => x.CommentCount),
                _ => query.OrderBy(x => x.CreatedAt),
            };
        }

        return query
               .Skip(skip)
               .Limit(take)
               .ToList();
    }

    
    public static bool ModifyDescription(string imageName, long playerId, string description)
		{
		    var db = GetDb();
		    var col = db.GetCollection<FullSavedImage>("images");
		    var image = col.FindOne(x => x.ImageName == imageName);

		    if (image == null || (long)image.PlayerId != playerId)
		        return false;

		    image.Description = description;
		    col.Update(image);
		    return true;
		}
    
    public static List<FullSavedImage> GetGlobalImages()
    {
        var db = GetDb();
        var col = db.GetCollection<FullSavedImage>("images");

        return col.Query()
                  .Where(x => x.Accessibility == ImageAccessibility.Public)
                  .OrderByDescending(x => x.CheerCount)
                  .ToList();
    }

    public static List<FullSavedImage> GetGlobalImagesPublic(int skip = 0, int take = 10, DateTime? since = null)
    {
        var db = GetDb();
        var col = db.GetCollection<FullSavedImage>("images");

        var query = col.Query()
                       .Where(x => x.Accessibility == ImageAccessibility.Public);

        if (since.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= since.Value);
        }

        return query.OrderByDescending(x => x.CheerCount)
                    .Limit(take)
                    .Skip(skip)
                    .ToList();
    }

    public static List<ImagesPlayer> GetPlayerImages(ulong playeridthatrequesting, ulong playerid)
    {
        var db = GetDb();
        var col = db.GetCollection<FullSavedImage>("images");

        var query = col.Query().Where(x => x.PlayerId == playerid);

        if (playeridthatrequesting != playerid)
        {
            query = query.Where(x => x.Accessibility == ImageAccessibility.Public);
        }

        return query.ToEnumerable()
                    .Select(MapToImagesPlayer)
                    .ToList();
    }

    private static ImagesPlayer MapToImagesPlayer(FullSavedImage x)
    {
        return new ImagesPlayer
        {
            Accessibility = x.Accessibility,
            AccessibilityLocked = x.AccessibilityLocked,
            CheerCount = x.CheerCount,
            CommentCount = x.CommentCount,
            CreatedAt = x.CreatedAt,
            Description = x.Description,
            ImageName = x.ImageName,
            PlayerEventId = x.PlayerEventId,
            PlayerId = x.PlayerId,
            RoomId = x.RoomId,
            SavedImageId = x.Id,
            SavedImageType = x.Type
        };
    }

    public static void UpdateImageCheerCount(int savedImageId, int cheerCount)
    {
        var db = GetDb();
        var col = db.GetCollection<FullSavedImage>("images");
        var img = col.FindById(savedImageId);
        if (img != null)
        {
            img.CheerCount = cheerCount;
            col.Update(img);
        }
    }

    public static bool ToggleCheer(int savedImageId, ulong playerId, bool cheer)
    {
        var db = GetDb();
        var col = db.GetCollection<Cheer>("cheers");

        var existing = col.Find(x => x.SavedImageId == savedImageId).FirstOrDefault(x => x.PlayerId == playerId);

        if (cheer && existing == null)
        {
            var last = col.Query().OrderByDescending(x => x.Id).Limit(1).FirstOrDefault(); // fix for bsonid stuff
            int newId = (last?.Id ?? 0) + 1;

            col.Insert(new Cheer { Id = newId, SavedImageId = savedImageId, PlayerId = playerId });
        }
        else if (!cheer && existing != null)
        {
            col.Delete(existing.Id);
        }

        return col.Find(x => x.SavedImageId == savedImageId).Any(x => x.PlayerId == playerId);
    }

    public static int GetCheerCount(int savedImageId)
    {
        var db = GetDb();
        return db.GetCollection<Cheer>("cheers").Count(x => x.SavedImageId == savedImageId);
    }

    public static bool HasUserCheered(int savedImageId, ulong playerId)
	{
    	var db = GetDb();
    	var col = db.GetCollection<Cheer>("cheers");
    	try
    	{
        	return col.Find(x => x.SavedImageId == savedImageId).Any(x => x.PlayerId == playerId);
    	}
    	catch (InvalidCastException)
    	{
        	db.DropCollection("cheers");
        	return false;
    	}
	}

    public static ImageV6 GetImageByName(string name)
    {
        var db = GetDb();
        var col = db.GetCollection<FullSavedImage>("images");
        var record = col.FindOne(x => x.ImageName == name);

        if (record == null) return null;

        return new ImageV6
        {
            Accessibility = record.Accessibility,
            AccessibilityLocked = record.AccessibilityLocked,
            CheerCount = record.CheerCount,
            CommentCount = record.CommentCount,
            CreatedAt = record.CreatedAt,
            Description = record.Description,
            Id = record.Id,
            ImageName = record.ImageName,
            PlayerEventId = record.PlayerEventId,
            PlayerId = record.PlayerId,
            RoomId = record.RoomId,
            TaggedPlayerIds = record.TaggedPlayerIds,
            Type = record.Type,
        };
    }

    public static void DeleteImagesFromPlayerId(ulong playerId)
    {
        var db = GetDb();
        var imageCol = db.GetCollection<FullSavedImage>("images");
        var cheerCol = db.GetCollection<Cheer>("cheers");

        var imagesToDelete = imageCol.Query().Where(x => x.PlayerId == playerId).ToList();

        if (imagesToDelete.Count == 0)
            return;

        foreach (var img in imagesToDelete)
        {
            imageCol.Delete(img.Id);
            cheerCol.DeleteMany(c => c.SavedImageId == img.Id);
        }

        Console.WriteLine($"[Delete] Removed {imagesToDelete.Count} images and their cheers for PlayerId {playerId}");
    }

    public static List<ImagesPlayer> GetImagesByIds(List<int> ids)
    {
        if (ids == null || ids.Count == 0)
            return new List<ImagesPlayer>();

        var db = GetDb();
        var col = db.GetCollection<FullSavedImage>("images");

        return col.Query()
                .Where(x => ids.Contains(x.Id))
                .ToEnumerable()
                .Select(MapToImagesPlayer)
                .ToList();
    }

    public static string SaveImageFile(byte[] data, long playerId, bool tripped = false)
    {
        string dir = Path.Combine("Data", "Images", playerId.ToString());
        if (tripped)
        {
            dir = Path.Combine("Data", "BadImages", playerId.ToString());
        }
        Directory.CreateDirectory(dir);

        string fileName = $"{Guid.NewGuid()}.jpg";
        string path = Path.Combine(dir, fileName);

        File.WriteAllBytes(path, data);

        return fileName;
    }
}