using System;
using System.Collections.Generic;

namespace VanadiumGuard.Server.Bans
{
    /// <summary>One machine as the service knows it, and every account seen on it.</summary>
    public sealed class MachineRecord
    {
        public string Composite { get; set; } = string.Empty;

        /// <summary>
        /// The component hashes this machine reported. Kept so a ban can still recognise the
        /// machine after one part is swapped and the composite changes.
        /// </summary>
        public Dictionary<string, string> Components { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Accounts that have opened a guard session from this machine.</summary>
        public List<string> PlayerIds { get; set; } = new List<string>();

        public DateTimeOffset FirstSeenUtc { get; set; }

        public DateTimeOffset LastSeenUtc { get; set; }

        /// <summary>Null when the machine is not banned. A lifted ban is removed, not flagged.</summary>
        public MachineBan? Ban { get; set; }
    }

    /// <summary>
    /// A hardware ban.
    /// <para>
    /// Every field here exists to answer a question someone will eventually ask about this
    /// record: who did it, when, why, and until when. A ban list with no answers to those is
    /// a liability rather than a tool - the appeal that cannot be evaluated gets denied by
    /// default, which is the failure mode worth engineering against.
    /// </para>
    /// </summary>
    public sealed class MachineBan
    {
        public string Reason { get; set; } = string.Empty;

        /// <summary>Identity of the moderator or system that issued it, recorded verbatim.</summary>
        public string IssuedBy { get; set; } = string.Empty;

        public DateTimeOffset IssuedUtc { get; set; }

        /// <summary>Null bans permanently.</summary>
        public DateTimeOffset? ExpiresUtc { get; set; }

        /// <summary>
        /// The accounts linked to the machine when the ban was issued. A snapshot, so the
        /// reviewer can see what the moderator was looking at rather than what the link
        /// table happens to say today.
        /// </summary>
        public List<string> LinkedPlayerIdsAtIssue { get; set; } = new List<string>();

        public bool IsActive(DateTimeOffset now)
        {
            return ExpiresUtc == null || ExpiresUtc > now;
        }
    }
}
