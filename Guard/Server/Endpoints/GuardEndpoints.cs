using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VanadiumGuard.Protocol;
using VanadiumGuard.Server.Bans;
using VanadiumGuard.Server.Challenges;
using VanadiumGuard.Server.Hosting;
using VanadiumGuard.Server.Policy;
using VanadiumGuard.Server.Security;
using VanadiumGuard.Server.Sessions;

namespace VanadiumGuard.Server.Endpoints
{
    /// <summary>Log category marker, so endpoint logs are not filed under an unrelated type.</summary>
    public sealed class GuardApi
    {
    }

    public static class GuardEndpoints
    {
        public static void MapGuardEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapPost("/v1/session/open", OpenSessionAsync);
            app.MapPost("/v1/session/report", ReportAsync);
            app.MapPost("/v1/session/heartbeat", HeartbeatAsync);
            app.MapGet("/v1/internal/session/{gameSessionId}/status", Status);

            // Hardware bans. Internal, key-gated, and never reachable from a game client:
            // these endpoints can end a session and can keep a person out permanently.
            app.MapPost("/v1/internal/machine/ban", BanMachine);
            app.MapPost("/v1/internal/machine/unban", UnbanMachine);
            app.MapGet("/v1/internal/machine/{composite}", GetMachine);
            app.MapGet("/v1/internal/player/{playerId}/machines", GetPlayerMachines);
            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        }

        /// <summary>
        /// Opens a guard session. This is the only unsigned endpoint, because it is where
        /// the signing key is established. It is authenticated with the player's normal
        /// game token instead, so a caller cannot open a session as someone else.
        /// </summary>
        private static async Task<IResult> OpenSessionAsync(
            HttpContext context,
            SessionOpenRequest request,
            IPlayerAuthenticator authenticator,
            ISessionStore sessions,
            PolicyProvider policies,
            MachineRegistry machines,
            GuardServerOptions options,
            ILogger<GuardApi> logger)
        {
            if (request.ProtocolVersion != ProtocolConstants.Version)
            {
                return Results.Json(
                    new { error = "unsupported protocol version", expected = ProtocolConstants.Version },
                    statusCode: StatusCodes.Status426UpgradeRequired);
            }

            string? bearer = ExtractBearer(context);
            string? playerId = await authenticator.AuthenticateAsync(bearer);

            if (string.IsNullOrEmpty(playerId))
            {
                // No valid token. Refuse unless anonymous sessions are allowed, in which case
                // the session is tied to the machine rather than an account - see
                // GuardServerOptions.AllowAnonymousSessions. An anonymous session still gets a
                // signing key, policy, hardware-ban checks and liveness; it simply is not
                // attributed to a player until the client is wired to send a token.
                if (!options.AllowAnonymousSessions)
                    return Results.Unauthorized();

                playerId = "anon";
            }

            // The authenticated identity wins over whatever the body claimed. Trusting the
            // body here would let any player file detections against any other player.
            if (!string.IsNullOrEmpty(request.PlayerId)
                && !string.Equals(request.PlayerId, playerId, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Session open claimed player {Claimed} but token belongs to {Actual}",
                    request.PlayerId, playerId);
            }

            // Hardware bans are checked before a session exists at all. Refusing here rather
            // than issuing a session and immediately disconnecting it means a banned machine
            // never receives a signing key, never receives the policy, and never learns which
            // detectors are switched on this week.
            (MachineRecord Record, MachineBan Ban)? machineBan = machines.FindActiveBan(request.Machine);

            if (machineBan != null)
            {
                logger.LogWarning(
                    "Refused session for player {Player}: machine {Composite} banned by {IssuedBy} for {Reason}",
                    playerId, machineBan.Value.Record.Composite, machineBan.Value.Ban.IssuedBy, machineBan.Value.Ban.Reason);

                return Results.Json(new SessionRejection
                {
                    ReasonCode = "VG-7001",

                    // Deliberately says nothing about how the machine was recognised. Telling
                    // someone which component matched is telling them which one to change.
                    PlayerMessage =
                        "This system has been blocked from playing. " +
                        "If you believe this is wrong, contact support and quote this code.",
                    ExpiresUtc = machineBan.Value.Ban.ExpiresUtc,
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            bool identified = request.Machine != null && request.Machine.IsUsable;

            if (!identified && !options.AllowUnidentifiedMachines)
            {
                logger.LogWarning("Refused session for player {Player}: no machine identity", playerId);

                return Results.Json(new SessionRejection
                {
                    ReasonCode = "VG-7003",
                    PlayerMessage =
                        "This system could not be verified. " +
                        "Contact support and quote this code.",
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            MachineRecord? machine = machines.Observe(request.Machine, playerId);

            // Device attestation. Verified before the session exists, so a bad proof never
            // gets a signing key issued against it.
            //
            // Verified against the player id the CLIENT put in the request, not the id the
            // server resolved: the client signed the proof over request.PlayerId, so that is
            // what has to be rebuilt to check the signature. They match for a token-authed
            // open; they differ for an anonymous one (client sends empty, server resolves
            // "anon"), and using the resolved id there would reject every honest anonymous
            // session. Identity is still proven by the token above - this only proves the
            // device holds its key and the request is fresh.
            AttestationResult attestation = AttestationVerifier.VerifyProof(
                request.Attestation, request.PlayerId ?? string.Empty, request.GameBuild ?? "unknown");

            if (attestation is AttestationResult.Malformed or AttestationResult.BadProof or AttestationResult.Stale)
            {
                // Presenting a key you cannot prove you hold is not a machine quirk; it is
                // someone constructing requests by hand.
                logger.LogWarning(
                    "Attestation rejected for player {Player}: {Result}", playerId, attestation);

                return Results.Json(new SessionRejection
                {
                    ReasonCode = "VG-8001",
                    PlayerMessage =
                        "This system could not be verified. " +
                        "Contact support and quote this code.",
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            bool attested = attestation == AttestationResult.Ok
                && request.Attestation!.Backing == AttestationBacking.Tpm;

            if (!attested && options.RequireDeviceAttestation)
            {
                logger.LogWarning("Refused session for player {Player}: no TPM attestation", playerId);

                return Results.Json(new SessionRejection
                {
                    ReasonCode = "VG-8002",
                    PlayerMessage =
                        "This system does not support the security features required to play. " +
                        "Contact support and quote this code.",
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            GuardPolicy policy = policies.Current;
            byte[] key = RequestSigner.NewSessionKey();

            var session = new GuardSession(
                sessionId: Guid.NewGuid().ToString("N"),
                sessionKey: key,
                playerId: playerId,
                gameSessionId: request.GameSessionId ?? string.Empty,
                gameBuild: request.GameBuild ?? "unknown")
            {
                PolicyVersion = policy.PolicyVersion,
                MachineComposite = machine?.Composite ?? string.Empty,
                AttestationKey = attestation == AttestationResult.Ok ? request.Attestation!.PublicKey : string.Empty,
                KeyBacking = attestation == AttestationResult.Ok ? request.Attestation!.Backing : AttestationBacking.None,
            };

            sessions.Add(session);

            logger.LogInformation(
                "Session {Session} opened for player {Player} build {Build} guard {Guard} machine {Machine} attestation {Attestation}",
                session.SessionId, playerId, request.GameBuild, request.GuardVersion,
                machine?.Composite ?? "unidentified",
                attested ? "tpm:" + DeviceAttestation.Thumbprint(session.AttestationKey) : attestation.ToString().ToLowerInvariant());

            return Results.Ok(new SessionOpenResponse
            {
                SessionId = session.SessionId,
                SessionKey = Convert.ToBase64String(key),
                Policy = policy,
                ServerTimeUtc = DateTimeOffset.UtcNow,
            });
        }

        private static async Task<IResult> ReportAsync(
            HttpContext context,
            ISessionStore sessions,
            PolicyEngine policyEngine)
        {
            SignedRequest<ReportRequest> signed =
                await SignedRequestReader.ReadAsync<ReportRequest>(context, sessions, "/v1/session/report");

            IResult? rejection = HandleRejection(signed.Status, signed.Session, policyEngine);
            if (rejection != null)
                return rejection;

            GuardSession session = signed.Session!;
            ReportRequest payload = signed.Payload!;

            if (payload.DroppedCount > 0)
            {
                // The client had more to say than it could queue. Worth knowing, because a
                // flood of detections is itself informative.
                policyEngine.RecordServerSide(
                    session, DetectionCode.PolicyRefused, "client-queue-overflow", 30,
                    new Dictionary<string, string> { ["dropped"] = payload.DroppedCount.ToString() });
            }

            Verdict verdict = policyEngine.Evaluate(session, payload.Detections);

            return Results.Ok(new ReportResponse
            {
                Verdict = verdict,
                Accepted = payload.Detections.Length,
            });
        }

        private static async Task<IResult> HeartbeatAsync(
            HttpContext context,
            ISessionStore sessions,
            PolicyEngine policyEngine,
            PolicyProvider policies,
            ChallengeService challenges)
        {
            SignedRequest<HeartbeatRequest> signed =
                await SignedRequestReader.ReadAsync<HeartbeatRequest>(context, sessions, "/v1/session/heartbeat");

            IResult? rejection = HandleRejection(signed.Status, signed.Session, policyEngine);
            if (rejection != null)
                return rejection;

            GuardSession session = signed.Session!;
            session.MarkHeartbeat();

            challenges.Grade(session, signed.Payload!.Answers);

            GuardPolicy policy = policies.Current;
            MemoryChallenge? next = challenges.Issue(session, policy);

            return Results.Ok(new HeartbeatResponse
            {
                Verdict = policyEngine.Current(session),
                Challenges = next == null ? Array.Empty<MemoryChallenge>() : new[] { next },
                NewPolicyVersion = policy.PolicyVersion == session.PolicyVersion ? null : policy.PolicyVersion,
            });
        }

        /// <summary>
        /// What the game server asks before trusting a player. This is the endpoint that
        /// makes the whole design server-authoritative: gameplay code consults the service
        /// rather than the client consulting itself.
        /// </summary>
        private static IResult Status(
            HttpContext context,
            string gameSessionId,
            ISessionStore sessions,
            PolicyEngine policyEngine,
            GuardServerOptions options)
        {
            if (!IsInternalCaller(context, options))
                return Results.Unauthorized();

            GuardSession? session = sessions.GetByGameSession(gameSessionId);

            if (session == null)
            {
                // No guard ever checked in for this match. Whether that is grounds for
                // action is the game server's call, not this service's; it reports the fact.
                return Results.Ok(new GuardStatusResponse
                {
                    GuardPresent = false,
                    GuardHealthy = false,
                    RiskScore = 0,
                    RecommendedAction = VerdictAction.Continue,
                });
            }

            TimeSpan silence = DateTimeOffset.UtcNow - session.LastSeenUtc;
            bool healthy = silence < TimeSpan.FromSeconds(options.SessionIdleTimeoutSeconds);

            return Results.Ok(new GuardStatusResponse
            {
                GuardPresent = true,
                GuardHealthy = healthy,
                Attested = session.IsAttested,
                RiskScore = session.RiskScore(),
                LastHeartbeatUtc = session.LastHeartbeatUtc,
                Codes = session.Detections().Select(d => (int)d.Detection.Code).Distinct().ToArray(),
                RecommendedAction = policyEngine.Current(session).Action,
            });
        }

        /// <summary>
        /// Bans a machine, either by its composite id or by naming a player whose machines
        /// should be banned - moderators know accounts, not hashes.
        /// <para>
        /// Reason and issuer are required, not optional. A ban list where either can be blank
        /// becomes unreviewable within a month, and an unreviewable ban list is one that
        /// never gets a wrong entry removed.
        /// </para>
        /// </summary>
        private static IResult BanMachine(
            HttpContext context,
            MachineBanRequest request,
            MachineRegistry machines,
            ISessionStore sessions,
            GuardServerOptions options,
            ILogger<GuardApi> logger)
        {
            if (!IsInternalCaller(context, options))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "reason is required" });

            if (string.IsNullOrWhiteSpace(request.IssuedBy))
                return Results.BadRequest(new { error = "issuedBy is required" });

            if (request.ExpiresUtc != null && request.ExpiresUtc <= DateTimeOffset.UtcNow)
                return Results.BadRequest(new { error = "expiresUtc is already in the past" });

            var banned = new List<string>();

            if (!string.IsNullOrWhiteSpace(request.Composite))
            {
                if (machines.Ban(request.Composite, request.Reason, request.IssuedBy, request.ExpiresUtc))
                    banned.Add(request.Composite);
            }
            else if (!string.IsNullOrWhiteSpace(request.PlayerId))
            {
                banned.AddRange(
                    machines.BanMachinesOf(request.PlayerId, request.Reason, request.IssuedBy, request.ExpiresUtc));
            }
            else
            {
                return Results.BadRequest(new { error = "composite or playerId is required" });
            }

            if (banned.Count == 0)
            {
                // Nothing was banned because nothing matched. Saying so beats reporting a
                // success the moderator would otherwise believe.
                return Results.NotFound(new { error = "no machine on record for that composite or player" });
            }

            int ended = EndSessionsOnMachines(sessions, banned, logger);

            logger.LogWarning(
                "Hardware ban by {IssuedBy}: {Count} machine(s), {Ended} live session(s) ended, reason {Reason}",
                request.IssuedBy, banned.Count, ended, request.Reason);

            return Results.Ok(new MachineBanResult
            {
                Banned = banned.ToArray(),
                SessionsEnded = ended,
            });
        }

        private static IResult UnbanMachine(
            HttpContext context,
            MachineUnbanRequest request,
            MachineRegistry machines,
            GuardServerOptions options,
            ILogger<GuardApi> logger)
        {
            if (!IsInternalCaller(context, options))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Composite))
                return Results.BadRequest(new { error = "composite is required" });

            if (string.IsNullOrWhiteSpace(request.LiftedBy))
                return Results.BadRequest(new { error = "liftedBy is required" });

            if (!machines.Unban(request.Composite, request.LiftedBy, request.Reason ?? string.Empty))
                return Results.NotFound(new { error = "no active ban on that machine" });

            return Results.Ok(new { unbanned = request.Composite });
        }

        private static IResult GetMachine(
            HttpContext context,
            string composite,
            MachineRegistry machines,
            GuardServerOptions options)
        {
            if (!IsInternalCaller(context, options))
                return Results.Unauthorized();

            MachineRecord? record = machines.Get(composite);

            return record == null
                ? Results.NotFound()
                : Results.Ok(ToView(record));
        }

        private static IResult GetPlayerMachines(
            HttpContext context,
            string playerId,
            MachineRegistry machines,
            GuardServerOptions options)
        {
            if (!IsInternalCaller(context, options))
                return Results.Unauthorized();

            return Results.Ok(machines.ForPlayer(playerId).Select(ToView).ToArray());
        }

        /// <summary>
        /// Ends the live sessions running on machines that were just banned, so a ban issued
        /// mid-match takes effect in seconds rather than at the next launch. The verdict is
        /// forced rather than scored: this is a decision a person already made.
        /// </summary>
        private static int EndSessionsOnMachines(
            ISessionStore sessions, IReadOnlyCollection<string> composites, ILogger<GuardApi> logger)
        {
            var banned = new HashSet<string>(composites, StringComparer.Ordinal);
            int ended = 0;

            foreach (GuardSession session in sessions.All())
            {
                if (string.IsNullOrEmpty(session.MachineComposite)
                    || !banned.Contains(session.MachineComposite)
                    || session.Terminated)
                {
                    continue;
                }

                session.Force(new Verdict
                {
                    Action = VerdictAction.Disconnect,
                    ReasonCode = "VG-7001",
                    PlayerMessage =
                        "This system has been blocked from playing. " +
                        "If you believe this is wrong, contact support and quote this code.",

                    // The same grace the scoring path uses, so a report already in flight
                    // still lands and the evidence survives the disconnect.
                    DelayMs = 2000,
                });

                ended++;

                logger.LogWarning(
                    "Session {Session} for player {Player} ended by a hardware ban",
                    session.SessionId, session.PlayerId);
            }

            return ended;
        }

        private static MachineRecordView ToView(MachineRecord record)
        {
            return new MachineRecordView
            {
                Composite = record.Composite,
                PlayerIds = record.PlayerIds.ToArray(),
                FirstSeenUtc = record.FirstSeenUtc,
                LastSeenUtc = record.LastSeenUtc,
                Banned = record.Ban != null && record.Ban.IsActive(DateTimeOffset.UtcNow),
                BanReason = record.Ban?.Reason,
                BannedBy = record.Ban?.IssuedBy,
                BannedUtc = record.Ban?.IssuedUtc,
                ExpiresUtc = record.Ban?.ExpiresUtc,
            };
        }

        /// <summary>
        /// Maps a validation failure to a response, recording the ones that indicate
        /// tampering rather than an ordinary network problem.
        /// </summary>
        private static IResult? HandleRejection(
            SignedRequestStatus status, GuardSession? session, PolicyEngine policyEngine)
        {
            switch (status)
            {
                case SignedRequestStatus.Ok:
                    return null;

                case SignedRequestStatus.MissingHeaders:
                case SignedRequestStatus.BadBody:
                    return Results.BadRequest(new { error = status.ToString() });

                case SignedRequestStatus.UnknownSession:
                    // Tells the client to reopen rather than retry forever.
                    return Results.Unauthorized();

                case SignedRequestStatus.ClockSkew:
                    return Results.Json(
                        new { error = "clock skew too large" },
                        statusCode: StatusCodes.Status400BadRequest);

                case SignedRequestStatus.BadSignature:
                    if (session != null)
                        policyEngine.RecordServerSide(session, DetectionCode.SignatureInvalid, session.PlayerId, 80);
                    return Results.Unauthorized();

                case SignedRequestStatus.BadAttestation:
                    // The session proved it held a device key at open and is no longer
                    // signing with it. Weighted like a bad signature, because it is the same
                    // claim: these requests are not coming from where they say they are.
                    if (session != null)
                        policyEngine.RecordServerSide(session, DetectionCode.AttestationInvalid, session.PlayerId, 80);
                    return Results.Unauthorized();

                case SignedRequestStatus.Replay:
                    if (session != null)
                        policyEngine.RecordServerSide(session, DetectionCode.SequenceReplay, session.PlayerId, 80);
                    return Results.Json(new { error = "replay" }, statusCode: StatusCodes.Status409Conflict);

                default:
                    return Results.BadRequest();
            }
        }

        private static string? ExtractBearer(HttpContext context)
        {
            string? header = context.Request.Headers.Authorization;

            if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return null;

            return header.Substring("Bearer ".Length).Trim();
        }

        private static bool IsInternalCaller(HttpContext context, GuardServerOptions options)
        {
            if (string.IsNullOrEmpty(options.InternalApiKey))
                return false;

            string? provided = context.Request.Headers["X-VG-Internal-Key"];
            if (string.IsNullOrEmpty(provided))
                return false;

            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(provided),
                System.Text.Encoding.UTF8.GetBytes(options.InternalApiKey));
        }
    }
}
