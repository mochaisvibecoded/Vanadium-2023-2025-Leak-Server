using System;
using System.Globalization;
using System.Text;

namespace VanadiumGuard.Protocol
{
    /// <summary>What is holding the private half of the attestation key.</summary>
    public enum AttestationBacking
    {
        /// <summary>No key at all. The session falls back to HMAC only.</summary>
        None = 0,

        /// <summary>
        /// A TPM, through the Windows Platform Crypto Provider. The private key physically
        /// cannot leave the chip, so a signature proves the request was made on this machine.
        /// </summary>
        Tpm = 1,

        /// <summary>
        /// A software key held by the OS key store. Better than nothing for continuity, but
        /// it is extractable by anyone with the same access the guard has, so the server
        /// treats it as unattested.
        /// </summary>
        Software = 2,
    }

    /// <summary>
    /// A device key and the proof that this client holds its private half, presented once at
    /// session open.
    /// <para>
    /// This exists because of the limit stated in the README: the HMAC session key lives in
    /// the client process, so anyone with a debugger can lift it and sign whatever they like.
    /// Replay, sequence and nonce checks do not help there - the attacker is not replaying
    /// anything, they are minting fresh, correctly-ordered requests. A TPM key is the one
    /// thing on a consumer machine that a debugger cannot walk off with: it signs inside the
    /// chip and the private half never exists in process memory.
    /// </para>
    /// <para>
    /// It does not make the client trustworthy. An attacker on the machine can still ask the
    /// TPM to sign things, exactly as the guard does. What it removes is the ability to lift
    /// a key and forge that machine traffic from somewhere else, and it binds every request
    /// to a piece of hardware that can be banned.
    /// </para>
    /// </summary>
    public sealed class DeviceAttestation
    {
        public AttestationBacking Backing { get; set; } = AttestationBacking.None;

        /// <summary>Base64 SubjectPublicKeyInfo for the ECDSA P-256 public key.</summary>
        public string PublicKey { get; set; } = string.Empty;

        /// <summary>Named so a future key type does not silently verify under the wrong rules.</summary>
        public string Algorithm { get; set; } = AlgorithmEs256;

        public long TimestampUnixMs { get; set; }

        public string Nonce { get; set; } = string.Empty;

        /// <summary>Base64 signature over <see cref="BuildProofString"/>, IEEE P1363 form.</summary>
        public string ProofSignature { get; set; } = string.Empty;

        public const string AlgorithmEs256 = "ES256";

        public bool IsPresent => Backing != AttestationBacking.None && PublicKey.Length > 0;

        /// <summary>
        /// The string the proof signature covers. Lives here, next to the canonical string
        /// for signed requests, so the two ends cannot drift apart.
        /// <para>
        /// The player id and the build are bound in so a proof captured from one session
        /// cannot be presented for a different account. The nonce is chosen by the client
        /// rather than issued by the server, which would normally be a weakness - but a
        /// stolen proof buys nothing on its own here, because every subsequent request has to
        /// be signed by the same private key, and that key is in the chip.
        /// </para>
        /// </summary>
        public static string BuildProofString(
            string playerId, string gameBuild, long timestampUnixMs, string nonce, string publicKeyBase64)
        {
            var sb = new StringBuilder();
            sb.Append("VG-ATTEST-V1").Append('\n');
            sb.Append(playerId ?? string.Empty).Append('\n');
            sb.Append(gameBuild ?? string.Empty).Append('\n');
            sb.Append(timestampUnixMs.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(nonce ?? string.Empty).Append('\n');
            sb.Append(publicKeyBase64 ?? string.Empty);
            return sb.ToString();
        }

        /// <summary>
        /// Short, stable identifier for the key, for logs and ban records. A hash rather than
        /// the key itself so a log line does not carry an eighty character blob.
        /// </summary>
        public static string Thumbprint(string publicKeyBase64)
        {
            if (string.IsNullOrEmpty(publicKeyBase64))
                return string.Empty;

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(publicKeyBase64));
                return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 32);
            }
        }
    }
}
