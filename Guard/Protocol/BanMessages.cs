using System;

namespace VanadiumGuard.Protocol
{
    /// <summary>
    /// Body of a 403 from /v1/session/open. The client shows this and stops; it is the one
    /// case where the guard refuses to run rather than running and reporting.
    /// </summary>
    public sealed class SessionRejection
    {
        /// <summary>Stable code the player can quote to support, e.g. VG-7001.</summary>
        public string ReasonCode { get; set; } = string.Empty;

        /// <summary>Player-facing text. Says a machine is blocked, not how it was recognised.</summary>
        public string PlayerMessage { get; set; } = string.Empty;

        /// <summary>Null for a permanent ban.</summary>
        public DateTimeOffset? ExpiresUtc { get; set; }
    }

    /// <summary>
    /// POST /v1/internal/machine/ban. Internal, called by moderation tooling with the
    /// internal API key - never by a game client.
    /// </summary>
    public sealed class MachineBanRequest
    {
        /// <summary>Composite id of the machine to ban. Takes precedence over PlayerId.</summary>
        public string? Composite { get; set; }

        /// <summary>
        /// Ban whichever machines this player has been seen on. Convenient for moderators,
        /// who know an account and not a hash.
        /// </summary>
        public string? PlayerId { get; set; }

        public string Reason { get; set; } = string.Empty;

        /// <summary>Who issued it. Recorded verbatim so bans can be traced to a person.</summary>
        public string IssuedBy { get; set; } = string.Empty;

        /// <summary>Null or absent bans permanently.</summary>
        public DateTimeOffset? ExpiresUtc { get; set; }
    }

    public sealed class MachineUnbanRequest
    {
        public string Composite { get; set; } = string.Empty;

        public string LiftedBy { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>One machine as the service knows it. Returned by the lookup endpoints.</summary>
    public sealed class MachineRecordView
    {
        public string Composite { get; set; } = string.Empty;

        public string[] PlayerIds { get; set; } = Array.Empty<string>();

        public DateTimeOffset FirstSeenUtc { get; set; }

        public DateTimeOffset LastSeenUtc { get; set; }

        public bool Banned { get; set; }

        public string? BanReason { get; set; }

        public string? BannedBy { get; set; }

        public DateTimeOffset? BannedUtc { get; set; }

        public DateTimeOffset? ExpiresUtc { get; set; }
    }

    public sealed class MachineBanResult
    {
        /// <summary>Composites actually banned by this call.</summary>
        public string[] Banned { get; set; } = Array.Empty<string>();

        /// <summary>Live guard sessions disconnected as a result.</summary>
        public int SessionsEnded { get; set; }
    }
}
