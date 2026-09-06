namespace VanadiumGuard.Protocol
{
    /// <summary>Wire-level constants shared by the guard client and the anti-cheat service.</summary>
    public static class ProtocolConstants
    {
        /// <summary>Bumped whenever request/response shapes change incompatibly.</summary>
        public const int Version = 1;

        public const string HeaderSession = "X-VG-Session";
        public const string HeaderTimestamp = "X-VG-Timestamp";
        public const string HeaderNonce = "X-VG-Nonce";
        public const string HeaderSignature = "X-VG-Signature";
        public const string HeaderSequence = "X-VG-Seq";
        public const string HeaderProtocol = "X-VG-Protocol";

        /// <summary>
        /// Per-request signature from the device attestation key, alongside the HMAC. Present
        /// only when the session bound a key at open; absent means HMAC-only, which is a
        /// state the server records rather than refuses.
        /// </summary>
        public const string HeaderAttestation = "X-VG-Attest";

        /// <summary>How far a client clock may drift before signed requests are rejected.</summary>
        public const int MaxClockSkewSeconds = 120;

        /// <summary>Upper bound on a single memory challenge read, enforced on both ends.</summary>
        public const int MaxChallengeReadLength = 4096;

        /// <summary>Detections are dropped rather than queued once the backlog hits this size.</summary>
        public const int MaxQueuedDetections = 256;
    }
}
