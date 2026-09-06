using System;
using LiteDB;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class AnnoucementDBClasses
    {
        // from game dump :3
        public class Announcement
        {
            [BsonId]
            public long AnnouncementId { get; set; }
            public AnnouncementType AnnouncementType { get; set; }
            public string? Title { get; set; }
            public string? Body { get; set; }
            public string? ImageName { get; set; }
            public LinkType LinkType { get; set; }
            public string? LinkName { get; set; }
            public string? LinkButtonLabel { get; set; }
            public string? LinkUri { get; set; }
            public Platforms Platform { get; set; } = Platforms.All;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class DirectorAnnouncementRequest : Announcement { public bool Clear { get; set; } = false; }

        public enum AnnouncementType
        {
            Update,
            Contest,
            Store,
            Event,
            Warning,
            Item
        }

        public enum LinkType
        {
            Url,
            AccountId,
            EventId,
            RoomName,
            Storefront,
            [Obsolete] // game said not to use ts?
            ActionCode,
            Item,
            CustomAvatarItem,
            AuthorizedRecNetUrl
        }
    }
}