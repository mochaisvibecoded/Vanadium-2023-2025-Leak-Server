using System;

namespace VanadiumGuard.Protocol
{
    /// <summary>
    /// A server-issued request for the client to hash a region of its own memory.
    /// <para>
    /// This is the anti-tamper primitive a stubbed-out guard cannot fake. The server picks
    /// the module, offset and salt at random every time, so a valid answer requires actually
    /// reading the live image, and a captured answer fails against the next salt.
    /// </para>
    /// </summary>
    public sealed class MemoryChallenge
    {
        public string ChallengeId { get; set; } = string.Empty;

        /// <summary>Module base name, e.g. GameAssembly.dll. Must be on the policy allowlist.</summary>
        public string Module { get; set; } = string.Empty;

        /// <summary>Offset from the module base address.</summary>
        public uint Rva { get; set; }

        /// <summary>Bytes to hash. Clamped to ProtocolConstants.MaxChallengeReadLength.</summary>
        public int Length { get; set; }

        /// <summary>Hex salt prefixed to the bytes before hashing. Never reused.</summary>
        public string Salt { get; set; } = string.Empty;

        public DateTimeOffset IssuedUtc { get; set; }
    }

    /// <summary>The client reply to a <see cref="MemoryChallenge"/>.</summary>
    public sealed class ChallengeAnswer
    {
        public string ChallengeId { get; set; } = string.Empty;

        /// <summary>Lowercase hex SHA-256 over salt bytes followed by the memory bytes.</summary>
        public string Hash { get; set; } = string.Empty;

        /// <summary>False when the client could not resolve the module or read the region.</summary>
        public bool Readable { get; set; }

        /// <summary>Populated when Readable is false.</summary>
        public string? Error { get; set; }
    }
}
