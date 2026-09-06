using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class CustomClothingDBClasses
    {
        public class CustomClothingItem
        {
            [BsonId]
            public Guid Id { get; set; } = Guid.NewGuid();
            public long CreatorAccountId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int Price { get; set; }
            public bool IsFeatured { get; set; } = false;
            public int? BaseAvatarItemId { get; set; }
            public string BaseAvatarItemColor { get; set; } = "#FFFFFF";
            public string DesignFilename { get; set; } = string.Empty;
            public string ThumbnailImageFilename { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
            public ClothingAccessibility Accessibility { get; set; } = ClothingAccessibility.Public;
            public ClothingPreviewOrientation PreviewOrientation { get; set; } = ClothingPreviewOrientation.Front;

            [BsonIgnore]
            public Dictionary<string, object> ToDictionary()
            {
                var data = new Dictionary<string, object>
                {
                    ["CustomAvatarItemId"] = Id.ToString(),
                    ["CreatorAccountId"] = CreatorAccountId,
                    ["Name"] = Name,
                    ["Description"] = Description,
                    ["Price"] = Price,
                    ["Accessibility"] = (int)Accessibility,
                    ["IsFeatured"] = IsFeatured,
                    ["BaseAvatarItemColor"] = BaseAvatarItemColor,
                    ["DesignFilename"] = DesignFilename,
                    ["ThumbnailImageFilename"] = ThumbnailImageFilename,
                    ["CreatedAt"] = CreatedAt.ToString("O"),
                    ["ModifiedAt"] = ModifiedAt.ToString("O"),
                    ["PreviewOrientation"] = (int)PreviewOrientation,
                };
                if (BaseAvatarItemId.HasValue)
                    data["BaseAvatarItemId"] = BaseAvatarItemId.Value;
                return data;
            }
        }

        public class CustomClothingMetaRequest
        {
            public required string Name { get; set; }
            public required string Description { get; set; }
            public required int Price { get; set; }
            public int? BaseAvatarItemId { get; set; }
            public required string BaseAvatarItemColor { get; set; }
            public required ClothingAccessibility Accessibility { get; set; }
            //public required ClothingPreviewOrientation PreviewOrientation { get; set; }
        }

        public class RecNetCustomClothingDto
        {
            public Guid CustomAvatarItemId { get; set; }
            public long CreatorAccountId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int Price { get; set; }
            public ClothingAccessibility Accessibility { get; set; }
            public bool IsFeatured { get; set; }
            public string BaseAvatarItemColor { get; set; } = string.Empty;
            public string DesignFilename { get; set; } = string.Empty;
            public string ThumbnailImageFilename { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime ModifiedAt { get; set; }
            public int? BaseAvatarItemId { get; set; }
            public ClothingPreviewOrientation PreviewOrientation { get; set; } = ClothingPreviewOrientation.Front;
        }

        public enum ClothingAccessibility
        {
            Public = 0,
            Private = 1,
            JuniorFriendly = 2
        }

        public enum ClothingPreviewOrientation
        {
            Front = 0,
            Back = 1,
            Side = 2
        }
    }
}