using System;
using System.Collections.Generic;
using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public static class HwidDBClasses
    {
        /// <summary>
        /// One machine, and every account that has ever reported from it.
        /// <para>
        /// The ban fields live on the record rather than in a second collection so that a
        /// lookup by hwid answers both questions - who is this machine, and is it banned -
        /// in a single read, on the path that runs for every client that checks in.
        /// </para>
        /// </summary>
        public class HwidRecord
        {
            [BsonId]
            public string Hwid { get; set; }
            public List<long> PlayerIds { get; set; } = new();
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }

            /// <summary>True while the machine itself is banned, separately from any account.</summary>
            public bool IsBanned { get; set; }

            /// <summary>Why. Required when banning; a blank reason makes a ban unreviewable.</summary>
            public string BanReason { get; set; }

            /// <summary>Player id of the moderator who issued it, or a system name.</summary>
            public string BannedBy { get; set; }

            public DateTime? BannedAt { get; set; }

            /// <summary>Null bans permanently.</summary>
            public DateTime? BanExpiresAt { get; set; }

            /// <summary>
            /// Accounts linked to the machine at the moment of the ban. A snapshot: it shows
            /// what the moderator was looking at, which is not always what the link table
            /// says later.
            /// </summary>
            public List<long> LinkedAtBan { get; set; } = new();

            public bool BanIsActive(DateTime utcNow) =>
                IsBanned && (BanExpiresAt == null || BanExpiresAt > utcNow);
        }
    }
}
