using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VanadiumGuard.Protocol;
using VanadiumGuard.Server.Sessions;

namespace VanadiumGuard.Server.Security
{
    /// <summary>Outcome of validating a signed request.</summary>
    public enum SignedRequestStatus
    {
        Ok,
        MissingHeaders,
        UnknownSession,
        ClockSkew,
        BadSignature,
        BadAttestation,
        Replay,
        BadBody,
    }

    public sealed class SignedRequest<T>
        where T : class
    {
        public SignedRequestStatus Status { get; init; }
        public GuardSession? Session { get; init; }
        public T? Payload { get; init; }

        public bool IsValid => Status == SignedRequestStatus.Ok && Session != null && Payload != null;
    }

    /// <summary>
    /// Reads and validates a signed request body.
    /// <para>
    /// The order of checks matters. The session is resolved first so a failure can be
    /// attributed to a player, then the signature, then replay. A request that fails
    /// signature or replay is not merely rejected: it is recorded against the session as
    /// a detection, because a client sending malformed signatures is a client someone has
    /// been editing.
    /// </para>
    /// </summary>
    public static class SignedRequestReader
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public static async Task<SignedRequest<T>> ReadAsync<T>(
            HttpContext context, ISessionStore sessions, string path)
            where T : class
        {
            string? sessionId = context.Request.Headers[ProtocolConstants.HeaderSession];
            string? timestampRaw = context.Request.Headers[ProtocolConstants.HeaderTimestamp];
            string? nonce = context.Request.Headers[ProtocolConstants.HeaderNonce];
            string? signature = context.Request.Headers[ProtocolConstants.HeaderSignature];
            string? sequenceRaw = context.Request.Headers[ProtocolConstants.HeaderSequence];

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(timestampRaw)
                || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(signature)
                || string.IsNullOrEmpty(sequenceRaw))
            {
                return new SignedRequest<T> { Status = SignedRequestStatus.MissingHeaders };
            }

            GuardSession? session = sessions.Get(sessionId);
            if (session == null)
                return new SignedRequest<T> { Status = SignedRequestStatus.UnknownSession };

            if (!long.TryParse(timestampRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long timestamp)
                || !long.TryParse(sequenceRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence))
            {
                return new SignedRequest<T> { Status = SignedRequestStatus.MissingHeaders, Session = session };
            }

            double skew = Math.Abs((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(timestamp)).TotalSeconds);
            if (skew > ProtocolConstants.MaxClockSkewSeconds)
                return new SignedRequest<T> { Status = SignedRequestStatus.ClockSkew, Session = session };

            byte[] body;
            using (var buffer = new MemoryStream())
            {
                await context.Request.Body.CopyToAsync(buffer);
                body = buffer.ToArray();
            }

            string canonical = RequestSigner.BuildCanonicalString("POST", path, timestamp, nonce, sequence, body);

            if (!RequestSigner.Verify(session.SessionKey, canonical, signature))
                return new SignedRequest<T> { Status = SignedRequestStatus.BadSignature, Session = session };

            // The device key signs the same canonical string. Checked only when the session
            // actually bound a key, so a client that never had one is not suddenly failing.
            if (session.AttestationKey.Length > 0)
            {
                string? attested = context.Request.Headers[ProtocolConstants.HeaderAttestation];

                // A session that presented a key and then stops signing with it is the
                // interesting case: that is exactly what lifting the HMAC key and driving the
                // session from another machine looks like from here.
                if (string.IsNullOrEmpty(attested)
                    || !AttestationVerifier.Verify(session.AttestationKey, canonical, attested))
                {
                    return new SignedRequest<T> { Status = SignedRequestStatus.BadAttestation, Session = session };
                }
            }

            // Only after the signature verifies, so an unauthenticated caller cannot burn
            // sequence numbers and lock a legitimate client out of its own session.
            if (!session.AcceptRequest(sequence, nonce))
                return new SignedRequest<T> { Status = SignedRequestStatus.Replay, Session = session };

            try
            {
                T? payload = JsonSerializer.Deserialize<T>(body, Json);
                if (payload == null)
                    return new SignedRequest<T> { Status = SignedRequestStatus.BadBody, Session = session };

                return new SignedRequest<T>
                {
                    Status = SignedRequestStatus.Ok,
                    Session = session,
                    Payload = payload,
                };
            }
            catch (JsonException)
            {
                return new SignedRequest<T> { Status = SignedRequestStatus.BadBody, Session = session };
            }
        }
    }
}
