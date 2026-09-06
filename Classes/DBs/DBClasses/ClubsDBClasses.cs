using System;
using System.Collections.Generic;
using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class ClubsDBClasses
    {
        public class Club
        {
            [BsonId]
            public long ClubId { get; set; }

            public string Name { get; set; } = "";
            public string? Description { get; set; }
            public string Category { get; set; } = "Social";

            public int Joinability { get; set; } = 0;

            public int Visibility { get; set; } = 1;

            public bool AllowJuniors { get; set; } = true;
            public int MinLevel { get; set; } = 0;
            public ClubType ClubType { get; set; } = ClubType.Generic;
            public bool IsRRO { get; set; } = false;

            public int State { get; set; } = 0;

            public long CreatorAccountId { get; set; }
            public long? ClubhouseRoomId { get; set; }

            public string MainImageName { get; set; } = "DefaultRoomImage.png";

            public int MemberCount { get; set; } = 1;

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            public List<ClubMember> Members { get; set; } = new();

            public List<ClubAdditionalImage> AdditionalImages { get; set; } = new();

            public List<string> CustomTags { get; set; } = new();

            public List<ClubPermissions> Permissions { get; set; } = new();

            public List<long> BannedMemberIds { get; set; } = new();

            public bool IsDeleted { get; set; } = false;
        }

        public class ClubMember
        {
            public long MemberId { get; set; }
            public long AccountId { get; set; }
            public int MemberType { get; set; } = 10;
            public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
            public bool IsPendingApproval { get; set; } = false;
        }


        public class ClubAdditionalImage
        {
            public int Index { get; set; }
            public string ImageName { get; set; } = "";
        }

        public class ClubPermissions
        {
            [BsonId]
            public long ClubPermissionsId { get; set; }

            public long ClubId { get; set; }

            public int Type { get; set; }

            public bool ApproveMember { get; set; }
            public bool BanUnban { get; set; }
            public bool CreateEvent { get; set; }
            public bool EditDetails { get; set; }
            public bool EditPermissionSettings { get; set; }
            public bool PostAnnouncement { get; set; }
        }

        public class ClubDbCounters
        {
            [BsonId]
            public string Key { get; set; } = "";
            public long Value { get; set; }
        }

        public class ClubAnnouncement
        {
            [BsonId]
            public long AnnouncementId { get; set; }

            public long ClubId { get; set; }

            public long CreatorAccountId { get; set; }

            public string Title { get; set; } = "";

            public string Body { get; set; } = "";

            public string? ImageName { get; set; }

            public string? Meta { get; set; }

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            public Dictionary<long, long> LastReadAnnouncementIdByAccount { get; set; } = new();
        }

        public class ClubAnnouncementDTO
        {
            public long AnnouncementId { get; set; }
            public long ClubId { get; set; }
            public long CreatorAccountId { get; set; }
            public string Title { get; set; } = "";
            public string Body { get; set; } = "";
            public string? ImageName { get; set; }
            public string? Meta { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class ClubAnnouncementListDTO
        {
            public long ClubId { get; set; }
            public List<ClubAnnouncementDTO> Announcements { get; set; } = new();
            public long? LastAnnouncementId { get; set; }
            public long LastReadAnnouncementId { get; set; }
        }

        public class ClubSummaryDTO
        {
            public long ClubId { get; set; }
            public string Name { get; set; } = "";
            public string? Description { get; set; }
            public string Category { get; set; } = "Social";
            public int Joinability { get; set; }
            public int Visibility { get; set; }
            public bool AllowJuniors { get; set; }
            public int MinLevel { get; set; }
            public ClubType ClubType { get; set; }
            public bool IsRRO { get; set; }
            public int State { get; set; }
            public long CreatorAccountId { get; set; }
            public long? ClubhouseRoomId { get; set; }
            public string MainImageName { get; set; } = "";
            public int MemberCount { get; set; }
        }

        public class ClubDetailsDTO
        {
            public long ClubId { get; set; }
            public ClubSummaryDTO Club { get; set; } = new();
            public List<ClubAdditionalImage> AdditionalImages { get; set; } = new();
            public List<string> CustomTags { get; set; } = new();
            public ClubPermissions? CoownerPermissions { get; set; }
            public ClubPermissions? ModeratorPermissions { get; set; }
            public ClubPermissions? MemberPermissions { get; set; }

            public int MyMembershipType { get; set; }
        }

        public class ClubSearchResult
        {
            public List<ClubSummaryDTO> Clubs { get; set; } = new();
            public string? ContinuationToken { get; set; }
            public int TotalClubs { get; set; }
        }

        public class ClubMemberDTO
        {
            public long MemberId { get; set; }
            public long AccountId { get; set; }
            public long ClubId { get; set; }
            public int MembershipType { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public enum ClubVisibility
        {
            Private = 0,
            Public = 1,
            Unlisted = 2
        }

        public enum ClubJoinability
        {
            Open = 0,
            ApprovalRequired = 1,
            Closed = 2
        }

        public enum ClubMemberType
        {
            Member = 10,
            Moderator = 20,
            CoOwner = 30,
            Owner = 100
        }

        public enum ClubType
        {
            Generic,
            Creator
        }
    }
}