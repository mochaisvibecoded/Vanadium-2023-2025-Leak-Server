using LiteDB;

public static partial class ImageMetadataDB
{
    public class FullSavedImage
    {
        [BsonId]
        public int Id { get; set; }
        public ImageAccessibility Accessibility { get; set; }
        public bool AccessibilityLocked { get; set; } = false;
        public int CheerCount { get; set; } = 0;
        public int CommentCount { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? Description { get; set; }
        public string ImageName { get; set; }
        public ulong PlayerEventId { get; set; }
        public ulong PlayerId { get; set; }
        public long RoomId { get; set; }
        public SavedImageTypeEnum Type { get; set; }
        public List<ulong> TaggedPlayerIds { get; set; } = new();
        public bool DevLocked { get; set; } = false;
    }

    public class ImageV6
    {
        public ImageAccessibility Accessibility { get; set; }
        public bool AccessibilityLocked { get; set; } = false;
        public int CheerCount { get; set; } = 0;
        public int CommentCount { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? Description { get; set; }
        public int Id { get; set; }
        public string ImageName { get; set; }
        public ulong PlayerEventId { get; set; }
        public ulong PlayerId { get; set; }
        public long RoomId { get; set; }
        public List<ulong> TaggedPlayerIds { get; set; } = new();
        public SavedImageTypeEnum Type { get; set; }
    }

    public class ImagesPlayer
    {
        public ImageAccessibility Accessibility { get; set; }
        public bool AccessibilityLocked { get; set; } = false;
        public int CheerCount { get; set; } = 0;
        public long ClubId { get; set; } = 0;
        public int CommentCount { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? Description { get; set; }
        public string ImageName { get; set; }
        public ulong PlayerEventId { get; set; }
        public ulong PlayerId { get; set; }
        public long RoomId { get; set; }
        public int SavedImageId { get; set; }
        public SavedImageTypeEnum SavedImageType { get; set; }
    }

    public class ImageMetadata
    {
        public List<ulong> playerIds { get; set; } = new();
        public SavedImageTypeEnum savedImageType { get; set; }
        public long roomId { get; set; }
        public ImageAccessibility accessibility { get; set; }
        public string? description { get; set; }
    }
    
    public class ModifyDescriptionRequest
	{
	    public string ImageName { get; set; } = string.Empty;
	    public string Description { get; set; } = string.Empty;
	}

    public class Cheer
    {
        [BsonId]
        public int Id { get; set; }
        public int SavedImageId { get; set; }
        public ulong PlayerId { get; set; }
    }

    public class CheerRequest
    {
        public int SavedImageId { get; set; }
        public bool Cheer { get; set; }
    }

    public enum SavedImageTypeEnum
    {
        None,
        ShareCamera,
        OutfitThumbnail,
        RoomThumbnail,
        ProfileThumbnail,
        InventionThumbnail,
        PlayerEventThumbnail,
        RoomLoadScreen
    }

    public enum ImageAccessibility
    {
        Private = 0,
        Public = 1,
    }
}