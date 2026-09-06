using System;

namespace VanadiumGuard.Protocol
{
    /// <summary>POST /v1/session/open. Authenticated with the player's normal game token.</summary>
    public sealed class SessionOpenRequest
    {
        public int ProtocolVersion { get; set; } = ProtocolConstants.Version;

        /// <summary>Opaque player id from the game's own auth system.</summary>
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>Game build identifier. Selects the reference images used for challenges.</summary>
        public string GameBuild { get; set; } = string.Empty;

        /// <summary>Assembly version of the guard client, so old builds can be refused.</summary>
        public string GuardVersion { get; set; } = string.Empty;

        /// <summary>Windows build string, for triaging platform-specific false positives.</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>
        /// Correlates this guard session with the match/room session the game server knows
        /// about, so the server can tell "guard went quiet" from "player quit".
        /// </summary>
        public string GameSessionId { get; set; } = string.Empty;

        /// <summary>
        /// Hashed machine identity, used for hardware bans. Absent when the client could
        /// not collect one; the server decides what an unidentifiable machine is worth
        /// rather than the client deciding for it.
        /// </summary>
        public MachineFingerprint? Machine { get; set; }

        /// <summary>
        /// Device key and proof of possession. Null when the machine has no usable key, which
        /// is a supported state: the session then relies on the HMAC alone, exactly as before.
        /// </summary>
        public DeviceAttestation? Attestation { get; set; }
    }

    public sealed class SessionOpenResponse
    {
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Base64 HMAC key for signing subsequent requests. Session-scoped.</summary>
        public string SessionKey { get; set; } = string.Empty;

        public GuardPolicy Policy { get; set; } = new GuardPolicy();

        /// <summary>Server time, so a client with a skewed clock can still sign correctly.</summary>
        public DateTimeOffset ServerTimeUtc { get; set; }
    }

    /// <summary>POST /v1/session/report. Signed. Carries a batch of detections.</summary>
    public sealed class ReportRequest
    {
        public Detection[] Detections { get; set; } = Array.Empty<Detection>();

        /// <summary>Policy revision the client is running, so drift can be spotted.</summary>
        public string PolicyVersion { get; set; } = "0";

        /// <summary>Detections dropped because the client queue was full.</summary>
        public int DroppedCount { get; set; }
    }

    public sealed class ReportResponse
    {
        public Verdict Verdict { get; set; } = new Verdict();

        /// <summary>Number of detections the server accepted, for client-side log parity.</summary>
        public int Accepted { get; set; }
    }

    /// <summary>POST /v1/session/heartbeat. Signed. Liveness plus challenge answers.</summary>
    public sealed class HeartbeatRequest
    {
        /// <summary>Answers to challenges issued in previous responses.</summary>
        public ChallengeAnswer[] Answers { get; set; } = Array.Empty<ChallengeAnswer>();

        /// <summary>Milliseconds since the guard started. Monotonic; a reset means a reload.</summary>
        public long UptimeMs { get; set; }

        /// <summary>Detections currently queued but not yet reported.</summary>
        public int QueueDepth { get; set; }
    }

    public sealed class HeartbeatResponse
    {
        public Verdict Verdict { get; set; } = new Verdict();

        /// <summary>Challenges to answer on the next heartbeat.</summary>
        public MemoryChallenge[] Challenges { get; set; } = Array.Empty<MemoryChallenge>();

        /// <summary>Set when the server has a newer policy; the client re-opens its session.</summary>
        public string? NewPolicyVersion { get; set; }
    }

    /// <summary>
    /// GET /v1/internal/session/{gameSessionId}/status, called by the game server rather
    /// than the client. This is the join point that makes the whole thing worth running:
    /// gameplay authority asks the anti-cheat service what it thinks, instead of the
    /// client deciding its own fate.
    /// </summary>
    public sealed class GuardStatusResponse
    {
        public bool GuardPresent { get; set; }

        public bool GuardHealthy { get; set; }

        /// <summary>
        /// True when every request this session has made was co-signed by a TPM-backed key.
        /// Lets the gameplay authority treat an unattested session differently without this
        /// service deciding for it.
        /// </summary>
        public bool Attested { get; set; }

        /// <summary>Accumulated risk score, 0-100.</summary>
        public int RiskScore { get; set; }

        public DateTimeOffset? LastHeartbeatUtc { get; set; }

        /// <summary>Detection codes seen this session, most severe first.</summary>
        public int[] Codes { get; set; } = Array.Empty<int>();

        public VerdictAction RecommendedAction { get; set; }
    }
}
