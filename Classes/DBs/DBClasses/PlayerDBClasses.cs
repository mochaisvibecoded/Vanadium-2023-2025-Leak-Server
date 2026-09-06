using System;
using System.Text.Json.Serialization;
using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class PlayerDBClasses
    {
        public class FullPlayer
        {
            [BsonId]
            public long PlayerId { get; set; }
            public List<mPlatformID> PlatformIds { get; set; } = new();
            public List<string>? DeviceIds { get; set; } = new();
            public string? AuthToken { get; set; }
            public string? Password { get; set; }
            public List<PlayerRoles> PlayerRoles { get; set; } = new();
            public Player? Player { get; set; }
            public string RefreshToken { get; set; } = "";
            public DateTime RefreshTokenExpires { get; set; } = new DateTime(9000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        public class Player
        {
            public string? Username { get; set; }
            public string? DisplayName { get; set; }
            public string? DisplayEmoji { get; set; }
            public string? Bio { get; set; }
            public int AvailableUsernameChanges { get; set; } = 3;
            public DateTime? LastUsernameChangeAt { get; set; }
            public bool NameLocked { get; set; } = false;
            public bool? IsJunior { get; set; }
            public bool AvoidJuniors { get; set; } = true;
            public int Level { get; set; } = 1;
            public int XP { get; set; } = 0;
            public int XPToday { get; set; } = 0;
            public DateTime XPTodayDate { get; set; } = DateTime.MinValue;
            public DateTime LastXPAwardedAt { get; set; } = DateTime.MinValue;
            public long CurrentXPRoomId { get; set; } = 0;
            public Dictionary<long, int> RoomXPEarnedToday { get; set; } = new();
            public string? ProfileImage { get; set; }
            public string? BannerImage { get; set; }
            public string? Email { get; set; }
            public int PronounFlags { get; set; }
            public int IdentityFlags { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastLoginAt { get; set; }
            public DateTime? Birthday { get; set; }
            public CurrentAuthSession? CurrentAuthSession { get; set; } = new CurrentAuthSession();
            public Reputation Reputation { get; set; } = new Reputation();
            public List<long> VisitedRooms { get; set; } = new();
            public List<RoomVisit> RoomVisits { get; set; } = new();
            public List<long> CheeredRooms { get; set; } = new List<long>();
            public List<long> FavoritedRooms { get; set; } = new List<long>();
            public PlayerExtra PlayerExtra { get; set; } = new PlayerExtra();
            public List<PlayerRelationship> Relationships { get; set; } = new List<PlayerRelationship>();
            public string? DiscordUserId { get; set; }
            public string? DiscordUsername { get; set; }
            public bool DiscordLinked { get; set; } = false;
            public DateTime? DiscordLinkedAt { get; set; }
        }

        public class RoomVisit
        {
            public long RoomId { get; set; }
            public DateTime LastVisitedAt { get; set; }
        }

        public class RoomPlayerData
        {
            public long RoomId { get; set; }
            public string Data { get; set; } = string.Empty;
        }

        public class PlayerDTOBase
        {
            public long accountId { get; set; }
            public DateTime createdAt { get; set; }
            public string? displayName { get; set; }
            public string? displayEmoji { get; set; }
            public bool? isJunior { get; set; }
            public int platforms { get; set; }
            public string? profileImage { get; set; }
            public string? bannerImage { get; set; }
            public string? username { get; set; }
            public int personalPronouns { get; set; }
            public int identityFlags { get; set; }
        }

        public class PlayerDTO : PlayerDTOBase { }

        public class PlayerMeDTO : PlayerDTOBase
        {
            public int availableUsernameChanges { get; set; } = 3;
            public DateTime? birthday { get; set; }
            public string? email { get; set; }
            public string? phone { get; set; }
            public Reputation? reputation { get; set; }
        }

        public class CurrentAuthSession
        {
            public string? AuthSigningKey { get; set; }
        }

        public class PlayerRelationship
        {
            public long PlayerId { get; set; }
            public int Favorited { get; set; } = 0;
            public int Ignored { get; set; } = 0;
            public int Muted { get; set; } = 0;
            public int RelationshipType { get; set; } = 0;
        }

        public class OwnedEquipmentItem
        {
            public string PrefabName { get; set; } = "";
            public string ModificationGuid { get; set; } = "";
            public bool Favorited { get; set; } = false;
            public int PlatformMask { get; set; } = -1;
            public string FriendlyName { get; set; } = "";
            public string Tooltip { get; set; } = "";
            public int Rarity { get; set; } = 0;
        }

        public class UpdateEquipmentRequest
        {
            public bool Favorited { get; set; }
            public string FriendlyName { get; set; } = "";
            public string ModificationGuid { get; set; } = "";
            public string PrefabName { get; set; } = "";
            public int Rarity { get; set; }
            public string? Tooltip { get; set; }
        }

        public class PlayerExtra
        {
            public Avatar Avatar { get; set; } = new Avatar();
            public List<string> AvatarItems { get; set; } = new();
            public List<SavedOutfit> SavedAvatars { get; set; } = new();
            public ModerationBlockDetails? ModerationBlockDetails { get; set; } = new ModerationBlockDetails();
            public List<Setting> Settings { get; set; } = new();
            public Heartbeat Heartbeat { get; set; } = new Heartbeat();
            public List<PlayerCurrency> Currencies { get; set; } = new();
            public List<RoomPlayerData> RoomPlayerData { get; set; } = new();
            public long DormRoomId { get; set; } = 0;
            public long PlayerClubId { get; set; } = 0;
            public int PhotoTaggingSetting { get; set; } = 0;
            public List<MessageData> Messages { get; set; } = new();
            public Influencer Influencer { get; set; } = new Influencer();

            // anything below is not in original code sonsaN

            public List<OwnedEquipmentItem> OwnedEquipment { get; set; } = new();
            public int ImagesUploadedToday { get; set; } = 0;
            public DateTime ImagesUploadedTodayDate { get; set; } = DateTime.MinValue;
            public List<RoomDBClasses.PermissionEntry> CurrentPermissions { get; set; } = new();
            public ToxModData? ToxMod { get; set; }
            public JuniorStatusData? JuniorStatus { get; set; }
            public DateTime? RoomChangeRestrictedUntil { get; set; }
            public PersonalDetails PersonalDetails { get; set; } = new PersonalDetails();
            public long HomeClubhouseId { get; set; } = 0;
        }

        public class PlayerCurrency
        {
            public int Balance { get; set; }
            public CurrencyType CurrencyType { get; set; }
            public BalanceType BalanceType { get; set; }
        }

        public class Avatar
        {
            public List<CustomAvatarItem> CustomAvatarItems { get; set; } = new();
            public string OutfitSelections { get; set; } = "";
            public string OutfitSelectionsV2 { get; set; } = "";
            public string FaceFeatures { get; set; } = "";
            public string SkinColor { get; set; } = "";
            public string HairColor { get; set; } = "";
            public string? CustomizationSettings { get; set; }
            public int DataVersion { get; set; } = 0;
        }
        
        public class OutfitSelection
        {
            public string? CustomAvatarItemId { get; set; }
            public int BodyPart { get; set; }
            public string? BakedUnityAssetFileName { get; set; }
            public object? AdditionalConfiguration { get; set; }
        }

        public class CustomAvatarItem
        {
            public int BodyPart { get; set; }
            public string CustomAvatarItemId { get; set; }
        }

        public class Heartbeat
        {
            public string appVersion { get; set; } = ServerConfig.GameVersion.ToString();
            public DeviceClasses? deviceClass { get; set; } = DeviceClasses.Unknown;
            public MatchmakingErrorCode? errorCode { get; set; } = null;
            public bool isOnline { get; set; } = false;
            public long playerId { get; set; } = 0;
            public RoomInstance? roomInstance { get; set; } = null;
            public StatusVisibility statusVisibility { get; set; } = StatusVisibility.Online;
            public int vrMovementMode { get; set; } = 0;

            public string? photonAuthToken { get; set; }
            public string? photonRealtimeAppId { get; set; }
            public string? photonVoiceAppId { get; set; }
            public string? photonChatAppId { get; set; }
            public string? photonRegion { get; set; }
            public string? photonRoomId { get; set; }
            public string? voiceConnectionInfo { get; set; }
            public string? voiceServerId { get; set; }
            public object? experiments { get; set; }
        }

        public class Subscriptions
        {
            public long playerId { get; set; } = 0;
            public int subscriberCount { get; set; } = 0;
        }

        public class RoomInstance
        {
            public bool encryptVoiceChat { get; set; }
            public long clubId { get; set; } = 0;
            public string? dataBlob { get; set; }
            public long eventId { get; set; } = 0;
            public bool isFull { get; set; }
            public bool isInProgress { get; set; }
            public bool isPrivate { get; set; }
            public string location { get; set; }
            public int maxCapacity { get; set; }
            public string Name { get; set; }
            public string photonRegion { get; set; }
            public string photonRegionId { get; set; }
            public string photonRoomId { get; set; }
            public string roomCode { get; set; } = "";
            public long roomId { get; set; }
            public long roomInstanceId { get; set; }
            public RoomInstanceType roomInstanceType { get; set; }
            public long subRoomId { get; set; }
        }

        public class Reputation
        {
            public long AccountId { get; set; }
            public bool IsCheerful { get; set; }
            public double Noteriety { get; set; }
            public CheerCategory SelectedCheer { get; set; }
            public int CheerCredit { get; set; } = 20;
            public int CheerGeneral { get; set; }
            public int CheerHelpful { get; set; }
            public int CheerCreative { get; set; }
            public int CheerGreatHost { get; set; }
            public int CheerSportsman { get; set; }
            public int SubscriberCount { get; set; }
            public int SubscribedCount { get; set; }
        }

        public class ModerationBlockDetails
        {
            public ReportCategory ReportCategory { get; set; } = ReportCategory.Moderator;
            public int Duration { get; set; } = 0;
            public long GameSessionId { get; set; } = 0;
            public bool? IsBan { get; set; } = false;
            public bool? IsHostKick { get; set; } = false;
            public bool? IsVoiceModAutoban { get; set; } = false; // wow when did this day finally come 9/1/2026 ts feel like heaven boi
            public bool? IsDeviceBan { get; set; } = false;
            public bool? IsWarning { get; set; } = false;
            public string? Message { get; set; } = "";
            public ulong? PlayerIdReporter { get; set; } = null;
            public string? VoteKickReason { get; set; } = null;
            public DateTime? TimeoutStartedAt { get; set; } = null;
            public string? AssociatedAccountUsername { get; set; } = null;
            [JsonIgnore]
            public long ModerationSetUnixTime { get; set; } = 0;
            [JsonIgnore]
            public ulong BannedByPlayerId { get; set; } = 0;
        }

        public class JuniorStatusData
        {
            public string? Reason { get; set; }
            public string? SetBy { get; set; }
            public DateTime? SetAt { get; set; }
        }

        public class ToxModData
        {
            public int Strikes { get; set; } = 0;
            public long ActiveBanExpiresUnixTime { get; set; } = 0;
            public string? ActiveBanCategory { get; set; }
            public long LastOffenseUnixTime { get; set; } = 0;
            public List<ToxModOffense> History { get; set; } = new();
        }

        public class ToxModOffense
        {
            public long UnixTime { get; set; }
            public string? Category { get; set; }
            public long DurationSeconds { get; set; }
            public string? Note { get; set; }
            public string? IssuedBy { get; set; }
        }

        // gay
        public class MessageData
        {
            public long Id { get; set; }
            public long FromPlayerId { get; set; }
            public DateTime SentTime { get; set; }
            public MessageType Type { get; set; }
            public string? Data { get; set; }
            public long? RoomId { get; set; }
            public long? PlayerEventId { get; set; }
        }

        public enum MessageType // we dont need another MessageType enum, and WebsocketEvents.cs has more than this because this is from a 2022 dump
        {
            GameInvite,
            GameInviteDeclined,
            GameJoinFailed,
            PartyActivitySwitch,
            FriendInvite,
            VoteToKick,
            GameInviteV2,
            PartyActivitySwitchV2,
            RequestGameInvite = 10,
            RequestGameInviteDeclined,
            FriendStatusOnline = 20,
            TextMessage = 30,
            FriendRequestAccepted = 40,
            PlayerCheer = 50,
            PlayerCheerAnonymous,
            RoomCoOwnerAdded = 60,
            RoomCoOwnerRemoved,
            RoomCoOwnerInvited,
            CreatorPublishedNewRoom = 70,
            PlayerAttendingEvent = 80,
            PlayerEventInvitation,
            GroupInvitation = 90,
            PlayerJoinedGroup,
            CoachMessage = 100
        }
        // gay

        public class Setting
        {
            public required string Key { get; set; }
            public required string Value { get; set; }
        }

        public class mPlatformID
        {
            public Platforms Platform { get; set; }
            public string PlatformId { get; set; }
        }

        public class CachedLogins
        {
            public Platforms platform { get; set; }
            public string? platformId { get; set; }
            public long accountId { get; set; }
            public DateTime? lastLoginTime { get; set; }
            public bool requirePassword { get; set; }
        }

        public class PlayerProgressionDTO
        {
            public long PlayerId { get; set; }
            public int Level { get; set; } = 1;
            public int XP { get; set; } = 0;
        }

        public class PersonalDetails //invention
        {
            public bool? IsCheering { get; set; } = false;
            public List<long> inventionsCheered { get; set; } = new List<long>();
        }
        
        public class OutfitLegacyData
        {
            public string? SelectionsV1 { get; set; }
            public string? SelectionsV2 { get; set; }
            public string? FaceFeatures { get; set; }
            public string? SkinColor { get; set; }
            public string? HairColor { get; set; }
        }
        
        public class SavedOutfitRequest
        {
            public int DataVersion { get; set; }
            public OutfitLegacyData? LegacyData { get; set; }
            public string? CustomizationSettings { get; set; }
            public List<OutfitSelection>? Selections { get; set; }
            public int Slot { get; set; }
            public string? Name { get; set; }
            public int Accessibility { get; set; }
            public string? ThumbnailFileName { get; set; }
        }

        public class SavedOutfit : Avatar // 2023 & under - discontinued now
        {
    		public int Slot { get; set; }
    		public string? PreviewImageName { get; set; }
    		public string? Name { get; set; }
    		public int Accessibility { get; set; } = 0;
    		public string? ThumbnailFileName { get; set; }
    		public List<OutfitSelection>? Selections { get; set; }
        }

        public class Influencer
        {
            public string CreatorCode { get; set; } = "";
            public bool IsInfluencer { get; set; } = false;
            public long SupportingInfluencer { get; set; } = 0;
        }

        public class DeleteMessagesRequest
        {
            public List<long> MessageIds { get; set; } = new();
        }

        public class Rep
        {
            public long AccountId { get; set; }
            public bool IsCheerful { get; set; }
            public int Noteriety { get; set; }
            public CheerCategory SelectedCheer { get; set; }
            public int CheerCredit { get; set; }
            public int CheerGeneral { get; set; }
            public int CheerHelpful { get; set; }
            public int CheerCreative { get; set; }
            public int CheerGreatHost { get; set; }
            public int CheerSportsman { get; set; }
        }

        public class SetPlayerPhotoTaggingSettingRequest
        {
            public PlayerPhotoTaggingSetting Setting { get; set; }
        }

        public class ImageMetadata
        {
            public long? roomId { get; set; }
            public string? description { get; set; }
            public int accessibility { get; set; }
            public List<long>? playerIds { get; set; }
            public SavedImageTypeEnum savedImageType { get; set; }
        }

        public class SavedAvatarRequest
        {
            public int Slot { get; set; }
            public string? OutfitSelections { get; set; }
            public string? OutfitSelectionsV2 { get; set; }
            public string? FaceFeatures { get; set; }
            public string? SkinColor { get; set; }
            public string? HairColor { get; set; }
            public string? PreviewImageName { get; set; }
            public string? Name { get; set; }
            public List<object>? CustomAvatarItems { get; set; }
        }

        public enum PlayerPhotoTaggingSetting
        {
            Anyone,
            Friends,
            NoOne
        }

        public enum SavedImageTypeEnum
        {
            General = 0,
            OutfitThumbnail = 1
        }

        public enum Platforms
        {
            All = -1,
            Steam,
            Oculus,
            PlayStation,
            Xbox,
            RecNet,
            IOS,
            GooglePlay,
            Standalone,
            Pico,
            Switch
        }

        public enum PlayerRoles
        {
            Screenshare,
            Moderator,
            Developer,
            Keepsake,
            betastudio,
            limitsv2,
            influencer,
            gameClient
        }

        public enum ReportCategory
        {
            Moderator = -1,
            Unknown,
            DEPRECATED_MicrophoneAbuse,
            Harassment,
            Cheating,
            DEPRECATED_ImmatureBehavior,
            AFK,
            Misc,
            Underage,
            VoteKick = 10,
            MisleadingPurchases,
            CoC_Underage = 100,
            CoC_Sexual,
            CoC_Discrimination,
            CoC_Trolling,
            CoC_NameOrProfile,
            InappropriateClothing = 200,
            IssuingInaccurateReports = 1000
        }

        public enum CheerCategory
        {
            None = -1,
            General = 0,
            Helpful = 10,
            Sportmanship = 20,
            GreatHost = 30,
            Creative = 40,
            RecRoomDeveloper = 9000
        }

        public enum MatchmakingErrorCode
        {
            UnknownError = -1,
            Success = 0,
            NoSuchGame = 1,
            PlayerNotOnline = 2,
            InsufficientSpace = 3,
            EventNotStarted = 4,
            EventAlreadyFinished = 5,
            BlockedFromRoom = 7,
            JuniorNotAllowed = 11,
            Banned = 12,
            AlreadyInBestInstance = 13,
            InsufficientRelationship = 14,
            UpdateRequired = 16,
            AlreadyInTargetInstance,
            UGCNotAllowed = 19,
            NoSuchRoom = 20,
            RoomIsNotActive = 22,
            RoomBlockedByCreator = 23,
            RoomIsPrivate = 25,
            RoomInstanceIsPrivate = 26,
            DeviceClassNotSupported = 30,
            DeviceClassNotSupportedByRoomOwner = 31,
            MovementModeNotSupportedByRoomOwner = 32,
            EventIsPrivate = 35,
            RoomInviteExpired = 40,
            NoAvailableRegion = 45,
            NotorietyTooPoor = 50,
            BannedFromRoom = 55,
            NoSuchRoomPlaylist = 60,
            RoomPlaylistIsNotActive,
            RoomPlaylistIsPrivate,
            NoSuchClub = 70,
            ClubHasNoClubhouse = 71,
            ClubIsNotActive = 73,
            NotAMemberOfClub = 74,
            BannedFromClub = 75,
            InstanceJoinNotPermitted = 76,
            LevelTooLow = 77,
            DeveloperOnly = 81,
            MetaJuniorAccountRestriction = 83,
            AccountDoesNotExist = 85
        }

        public enum DeviceClasses
        {
            Unknown,
            VR,
            Screen,
            Mobile,
            VRLow,
            Quest2
        }

        public enum CurrencyType
        {
            Invalid,
            LaserTagTickets,
            RecCenterTokens = 2,
            LostSkullsGold = 100,
            DraculaSilver,
            RecRoyale_Season1 = 200,
            RoomCurrency = 300
        }

        public enum BalanceType
        {
            NonPurchasedNotUsableInP2P = -2,
            NonPurchasedDefault,
            SteamPurchased,
            OculusPurchased,
            PlayStationPurchased,
            MicrosoftPurchased,
            IOSPurchased = 5,
            GooglePlayPurchased,
            PlayStationNonPurchasedP2P = 100,
            NonPlayStationNonPurchasedP2P,
            NonPurchasedEarnedByP2P = 1000
        }

        public enum StatusVisibility
        {
            Online,
            Away,
            Offline,
            Unknown = 100
        }

        public enum RoomInstanceType
        {
            Public,
            Private,
            Dormroom,
            Event,
            Meetup,
            Clubhouse
        }

        public enum HileType
        {
            Obscured,
            Time,
            Inject,
            GiftCount,
            Engine,
            UnknownDll,
            ImageSignature,
            AvatarHack,
            NetworkCertificatePublicKey = 100,
            NetworkCertificateIssuer,
            NetworkCertificateMissing,
            NetworkCertificateMismatch,
            AutosaveChecksumMismatch = 150,
            AutosaveSubRoomIdMismatch,
            AutosaveChecksumException,
            Photon_MissingHash = 200,
            Photon_CorruptHash,
            Photon_DifferentHash = 203,
            AppData_Runtime_LengthMismatch = 300,
            AppData_Runtime_LastWriteTimeMismatch,
            AppData_Runtime_FileModified,
            AppData_Boot_InvalidSignature = 310,
            AppData_Boot_UnableToVerifySignatures,
            Config_MissingHash = 320,
            Config_DifferentHash,
            Photon_InstantiateTool = 400,
            Memory_Hash_Mismatch = 500,
            Driver_Invalid_Signature = 600
        }
    }
}