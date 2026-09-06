using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VanadiumGuard.Protocol
{
    /// <summary>
    /// HMAC-SHA256 request signing, shared verbatim by client and server so the canonical
    /// string can never drift between the two.
    /// <para>
    /// Threat model, stated plainly: the session key lives in the client process, so an
    /// attacker with a debugger can extract it and sign whatever they like. Signing is not
    /// what stops cheating. It stops the cheap attacks - replaying someone else's clean
    /// reports, forging reports for another player, and tampering with a report in transit -
    /// and it forces the expensive attack to keep the guard alive and answering memory
    /// challenges, which is what the server actually watches for.
    /// </para>
    /// </summary>
    public static class RequestSigner
    {
        /// <summary>
        /// Builds the string that gets signed. Every field that could be swapped by an
        /// attacker is bound in: method, path, time, nonce, sequence and a body digest.
        /// </summary>
        public static string BuildCanonicalString(
            string method,
            string path,
            long timestampUnixMs,
            string nonce,
            long sequence,
            byte[] body)
        {
            string bodyHash;
            using (var sha = SHA256.Create())
            {
                bodyHash = Convert.ToBase64String(sha.ComputeHash(body ?? Array.Empty<byte>()));
            }

            var sb = new StringBuilder();
            sb.Append(method.ToUpperInvariant()).Append('\n');
            sb.Append(path).Append('\n');
            sb.Append(timestampUnixMs.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(nonce).Append('\n');
            sb.Append(sequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(bodyHash);
            return sb.ToString();
        }

        public static string Sign(byte[] key, string canonicalString)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalString)));
            }
        }

        /// <summary>Constant-time comparison. Do not replace with string equality.</summary>
        public static bool Verify(byte[] key, string canonicalString, string providedSignature)
        {
            if (string.IsNullOrEmpty(providedSignature))
                return false;

            string expected = Sign(key, canonicalString);
            return FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(providedSignature));
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static string NewNonce()
        {
            byte[] buf = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(buf);
            }
            return Convert.ToHexString(buf).ToLowerInvariant();
        }

        public static byte[] NewSessionKey()
        {
            byte[] buf = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(buf);
            }
            return buf;
        }

        /// <summary>Lowercase hex SHA-256 over salt bytes followed by payload bytes.</summary>
        public static string HashWithSalt(byte[] salt, byte[] payload)
        {
            using (var sha = SHA256.Create())
            {
                byte[] combined = new byte[salt.Length + payload.Length];
                Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
                Buffer.BlockCopy(payload, 0, combined, salt.Length, payload.Length);
                return Convert.ToHexString(sha.ComputeHash(combined)).ToLowerInvariant();
            }
        }
    }
}
