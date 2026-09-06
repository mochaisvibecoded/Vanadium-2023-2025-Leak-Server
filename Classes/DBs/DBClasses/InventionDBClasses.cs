using LiteDB;
using static Vanadium.Classes.DBs.DBClasses.RoomDBClasses;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class InventionDBClasses
    {
        public class Invention
        {
            [BsonId]
            public long InventionId { get; set; }
            public string ReplicationId { get; set; } = Guid.NewGuid().ToString();
            public long CreatorPlayerId { get; set; }
            public long CreationRoomId { get; set; } = 1;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string LongDescription { get; set; } = string.Empty;
            public string ImageName { get; set; } = string.Empty;
            public int UgcVersion { get; set; } = 1;
            public bool ForceCannotPublish { get; set; } = false;
            public int CurrentVersionNumber { get; set; } = 1;
            public int LatestVersionNumber { get; set; } = 1;
            public List<InventionVersion> Versions { get; set; } = new();
            public InventionVersion? CurrentVersion { get; set; }
            public RoomAccessibility Accessibility { get; set; } = RoomAccessibility.Private;
            public bool IsPublished { get; set; } = false;
            public bool IsFeatured { get; set; } = false;
            public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? FirstPublishedAt { get; set; } = DateTime.MinValue;
            public int NumPlayersHaveUsedInRoom { get; set; } = 0;
            public int NumDownloads { get; set; } = 0;
            public int CheerCount { get; set; } = 0;
            public int CreatorPermission { get; set; } = 100;
            public int GeneralPermission { get; set; } = 100;
            public bool IsAGInvention { get; set; } = false;
            public bool IsCertifiedInvention { get; set; } = false;
            public bool IsRecRoomApproved { get; set; } = false;
            public int Price { get; set; } = 0;
            public bool AllowTrial { get; set; } = false;
            public bool HideFromPlayer { get; set; } = false;
            public string DisplayMetadataJson { get; set; } = string.Empty;
            public int inkCost { get; set; } = 1;
            public List<InventionTag> Tags { get; set; } = new();
            public List<long> CheeredIds { get; set; } = new();
            public List<long> DownloadIds { get; set; } = new();
            public List<long> ReferencedInventions { get; set; } = new();
        }

        public class InventionVersion
        {
            public long InventionId { get; set; }
            public string ReplicationId { get; set; } = Guid.NewGuid().ToString();
            public int VersionNumber { get; set; } = 1;
            public string BlobName { get; set; } = string.Empty;
            public string BlobHash { get; set; } = string.Empty;
            public int InstantiationCost { get; set; }
            public int LightsCost { get; set; }
            public int ChipsCost { get; set; }
            public int CloudVariablesCost { get; set; }
            public int AICost { get; set; }
            public int inkCost { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class InventionData
        {
            public bool AllowTrial { get; set; }
            public int CheerCount { get; set; }
            public string CreatedAt { get; set; } = string.Empty;
            public int CreatorPermission { get; set; }
            public long CreatorPlayerId { get; set; }
            public int CurrentVersionNumber { get; set; }
            public string Description { get; set; } = string.Empty;
            public string LongDescription { get; set; } = string.Empty;
            public int GeneralPermission { get; set; }
            public bool HideFromPlayer { get; set; }
            public string ImageName { get; set; } = string.Empty;
            public long InventionId { get; set; }
            public bool IsAGInvention { get; set; }
            public bool IsCertifiedInvention { get; set; }
            public bool IsPublished { get; set; }
            public bool IsFeatured { get; set; }
            public bool IsRecRoomApproved { get; set; }
            public string ModifiedAt { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int NumDownloads { get; set; }
            public int NumPlayersHaveUsedInRoom { get; set; }
            public int Price { get; set; }
            public string ReplicationId { get; set; } = string.Empty;
            public int UgcVersion { get; set; }
            public bool ForceCannotPublish { get; set; }
            public int LatestVersionNumber { get; set; }
            public List<InventionVersion> Versions { get; set; } = new();
            public InventionVersion? CurrentVersion { get; set; }
            public int Accessibility { get; set; }
            public DateTime? FirstPublishedAt { get; set; } = DateTime.MinValue;
            public long CreationRoomId { get; set; }
            public string DisplayMetadataJson { get; set; } = string.Empty;
            public List<long> ReferencedInventions { get; set; } = new();
            public List<InventionTag> Tags { get; set; } = new();
        }

        public class InventionTag
        {
            public string Tag { get; set; } = string.Empty;
            public int Type { get; set; } = 0;
        }

        public class SaveInventionRequest
        {
            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string name { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("description")]
            public string description { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("imageName")]
            public string imageName { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("instantiationCost")]
            public int instantiationCost { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("lightsCost")]
            public int lightsCost { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("chipsCost")]
            public int chipsCost { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("cloudVariablesCost")]
            public int cloudVariablesCost { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("aiCost")]
            public int aiCost { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("inkCost")]
            public int inkCost { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("creationRoomId")]
            public long creationRoomId { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("inventionDataFilename")]
            public string inventionDataFilename { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("referencedInventions")]
            public List<long> referencedInventions { get; set; } = new();
            [System.Text.Json.Serialization.JsonPropertyName("creatorAccountRole")]
            public int creatorAccountRole { get; set; }
        }

        public class AddVersionRequest
        {
            public long inventionId { get; set; }
            public int instantiationCost { get; set; }
            public int lightsCost { get; set; }
            public int chipsCost { get; set; }
            public int cloudVariablesCost { get; set; }
            public int aiCost { get; set; }
            public int inkCost { get; set; }
            public long creationRoomId { get; set; } = -1;
            public string? inventionDataFilename { get; set; }
            public List<long>? referencedInventions { get; set; }
        }

        public class InventionBatchRequest
        {
            public List<long> InventionIds { get; set; } = new();
        }

        public class SetTagsRequest
        {
            public long InventionId { get; set; }
            public List<string> AutoTags { get; set; } = new();
            public List<string> CustomTags { get; set; } = new();
        }

        public class SetTagsResponse
        {
            public int Result { get; set; }
            public List<string> Tags { get; set; } = new();
        }

        public class DetailsResponse
        {
            public List<TagData> Tags { get; set; } = new();
        }

        public class TagData
        {
            public string Tag { get; set; } = string.Empty;
            public int Type { get; set; }
        }

        public class PersonalDetailsResponse
        {
            public bool IsCheering { get; set; } = false;
        }

        public enum InventionPermissions // moved to its own enum class
        {
            Unassigned = 0,
            LimitedOneUseOnly = 10,
            DisallowKeyLock = 15,
            UseOnly = 20,
            EditAndSave = 40,
            Publish = 60,
            Charge = 80,
            Unlimited = 100
        }

        public enum InventionReportCategory
        {
            RacismOrDiscriminatoryContent = 0,
            SexualContent = 1,
            MisleadingNamePictureOrDescription = 3,
            Other = 4
        }

        public class InventionResponse
        {
            public int StatusCode { get; set; }
            public Invention Invention { get; set; }
            public InventionVersion InventionVersion { get; set; }
        }

        public class BuyInventionResponse
        {
            public InventionResponse InventionResponse { get; set; }
            public StorefrontsDBClasses.BuyForFreeGiftButtonResponse BalanceUpdateResponse { get; set; }
        }

        public class ReportInvention
        {
            public long InventionId { get; set; }
            public InventionReportCategory ReportCategory { get; set; }
            public string? Details { get; set; }
        }

        public class CheerInventionRequest
        {
            public bool Cheer { get; set; }
            public long InventionId { get; set; }
        }
    }
}