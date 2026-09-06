namespace VanadiumGuard.Protocol
{
    /// <summary>The only actions a client may take, and only when the server says so.</summary>
    public enum VerdictAction
    {
        /// <summary>Keep playing. The default for everything below the review threshold.</summary>
        Continue = 0,
        /// <summary>Show the player a message. For recoverable states, e.g. "close that tool".</summary>
        Warn = 1,
        /// <summary>Leave the session cleanly with a reason code. No crash, no data loss.</summary>
        Disconnect = 2,
    }

    /// <summary>Server response to a report or heartbeat.</summary>
    public sealed class Verdict
    {
        public VerdictAction Action { get; set; } = VerdictAction.Continue;

        /// <summary>Short stable code shown to the player and logged, e.g. VG-1001.</summary>
        public string? ReasonCode { get; set; }

        /// <summary>Player-facing text. Deliberately vague about what was detected.</summary>
        public string? PlayerMessage { get; set; }

        /// <summary>Grace period before the client acts, so an in-flight report can finish.</summary>
        public int DelayMs { get; set; }
    }
}
