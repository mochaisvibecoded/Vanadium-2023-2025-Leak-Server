using System;
using System.Security.Cryptography;
using System.Text;
using VanadiumGuard.Protocol;

namespace VanadiumGuard.Server.Security
{
    /// <summary>Why an attestation was rejected, for logs and detections.</summary>
    public enum AttestationResult
    {
        /// <summary>Proof verified. The key is bound to the session.</summary>
        Ok,

        /// <summary>No attestation offered. Falls back to HMAC-only; not an error.</summary>
        Absent,

        /// <summary>Key material was unusable, or the algorithm was not one we verify.</summary>
        Malformed,

        /// <summary>The proof did not verify under the offered key.</summary>
        BadProof,

        /// <summary>The proof was too old to accept.</summary>
        Stale,
    }

    /// <summary>
    /// Verifies device attestations at session open, and the per-request signatures that
    /// follow.
    /// <para>
    /// Worth being precise about what this buys, because it is easy to oversell. It does not
    /// make the client honest: an attacker sitting on the machine can ask the TPM to sign
    /// whatever the guard would have signed. What it removes is the ability to extract a key
    /// and drive a session from somewhere else, which is what the HMAC scheme cannot stop,
    /// and it pins every request to one piece of hardware that can then be banned.
    /// </para>
    /// </summary>
    public static class AttestationVerifier
    {
        /// <summary>
        /// How old a proof of possession may be. Tighter than the request clock skew because
        /// this is presented once, at the start, and has no sequence number behind it.
        /// </summary>
        public static readonly TimeSpan MaxProofAge = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Checks the proof of possession presented at session open. Returns Absent, not an
        /// error, when no attestation was offered.
        /// </summary>
        public static AttestationResult VerifyProof(
            DeviceAttestation? attestation, string playerId, string gameBuild)
        {
            if (attestation == null || !attestation.IsPresent)
                return AttestationResult.Absent;

            if (!string.Equals(attestation.Algorithm, DeviceAttestation.AlgorithmEs256, StringComparison.Ordinal))
                return AttestationResult.Malformed;

            double age = Math.Abs(
                (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(attestation.TimestampUnixMs))
                .TotalSeconds);

            if (age > MaxProofAge.TotalSeconds)
                return AttestationResult.Stale;

            string proof = DeviceAttestation.BuildProofString(
                playerId, gameBuild, attestation.TimestampUnixMs, attestation.Nonce, attestation.PublicKey);

            return Verify(attestation.PublicKey, proof, attestation.ProofSignature)
                ? AttestationResult.Ok
                : AttestationResult.BadProof;
        }

        /// <summary>
        /// Verifies one signature made by a bound device key over the canonical request
        /// string. Returns false for anything it cannot check, including unusable key
        /// material - a key that stops parsing mid-session is not something to wave through.
        /// </summary>
        public static bool Verify(string publicKeyBase64, string payload, string signatureBase64)
        {
            if (string.IsNullOrEmpty(publicKeyBase64) || string.IsNullOrEmpty(signatureBase64))
                return false;

            try
            {
                byte[] spki = Convert.FromBase64String(publicKeyBase64);
                byte[] signature = Convert.FromBase64String(signatureBase64);

                using ECDsa key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(spki, out _);

                // Default signature format on both ends is IEEE P1363 fixed-field, which is
                // what ECDsa.SignData produces on the client. Do not switch one side to DER
                // without switching the other.
                return key.VerifyData(
                    Encoding.UTF8.GetBytes(payload), signature, HashAlgorithmName.SHA256);
            }
            catch (Exception)
            {
                // Bad base64, a key that is not P-256, a truncated SPKI. All the same answer.
                return false;
            }
        }
    }
}
