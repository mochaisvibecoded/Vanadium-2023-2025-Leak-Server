using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using VanadiumGuard.Protocol;
using VanadiumGuard.Server.Sessions;

namespace VanadiumGuard.Server.Policy
{
    /// <summary>
    /// Turns detections into a verdict. This is the only place in the system where a
    /// decision about a player is made, and it runs on the server where the player cannot
    /// reach it.
    /// </summary>
    public sealed class PolicyEngine
    {
        /// <summary>Score at which the session is ended.</summary>
        public const int DisconnectThreshold = 80;

        /// <summary>Score at which the player is told something is wrong but keeps playing.</summary>
        public const int WarnThreshold = 40;

        private readonly ILogger<PolicyEngine> _logger;

        public PolicyEngine(ILogger<PolicyEngine> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Records a batch against the session and returns what the client should do.
        /// </summary>
        public Verdict Evaluate(GuardSession session, IReadOnlyList<Detection> detections)
        {
            bool decisive = false;

            foreach (Detection detection in detections)
            {
                DetectionCatalog.Entry entry = DetectionCatalog.For(detection.Code);

                if (entry.Weight <= 0)
                {
                    _logger.LogDebug("Ignoring unweighted code {Code} from {Session}", detection.Code, session.SessionId);
                    continue;
                }

                int confidence = Math.Clamp(detection.Confidence, 0, 100);

                // Confidence scales the weight. A detector rolled out at MaxConfidence 0
                // contributes exactly nothing, which is what makes a safe rollout possible.
                double score = entry.Weight * (confidence / 100.0);
                session.Record(detection, score);

                // Decisive codes still have to arrive at high confidence. A weak reading of
                // a serious signal is evidence, not a conviction.
                if (entry.Decisive && confidence >= 85)
                {
                    decisive = true;
                    _logger.LogWarning(
                        "Decisive detection {Code} subject={Subject} conf={Confidence} player={Player} session={Session}",
                        detection.Code, detection.Subject, confidence, session.PlayerId, session.SessionId);
                }
            }

            return VerdictFor(session, decisive);
        }

        /// <summary>Re-derives a verdict without adding anything, for heartbeats and status.</summary>
        public Verdict Current(GuardSession session)
        {
            return VerdictFor(session, decisive: false);
        }

        private Verdict VerdictFor(GuardSession session, bool decisive)
        {
            // A forced verdict outranks the score. A hardware ban is not a risk judgement
            // and must not be re-derived from one.
            Verdict? forced = session.ForcedVerdict;
            if (forced != null)
                return forced;

            if (session.Terminated)
            {
                return new Verdict
                {
                    Action = VerdictAction.Disconnect,
                    ReasonCode = "VG-TERM",
                    PlayerMessage = "This session was ended by an anti-cheat check.",
                };
            }

            int score = session.RiskScore();

            if (decisive || score >= DisconnectThreshold)
            {
                session.MarkTerminated();

                _logger.LogWarning(
                    "Disconnecting player={Player} session={Session} score={Score} decisive={Decisive}",
                    session.PlayerId, session.SessionId, score, decisive);

                return new Verdict
                {
                    Action = VerdictAction.Disconnect,
                    ReasonCode = "VG-" + score.ToString("D3"),
                    PlayerMessage =
                        "You have been removed from this session by an anti-cheat check. " +
                        "If you believe this is wrong, contact support and quote this code.",

                    // A short grace period so the client can flush the batch that triggered
                    // this. Without it the most important evidence is the evidence lost.
                    DelayMs = 2000,
                };
            }

            if (score >= WarnThreshold)
            {
                return new Verdict
                {
                    Action = VerdictAction.Warn,
                    ReasonCode = "VG-W" + score.ToString("D3"),
                    PlayerMessage =
                        "Close any debugging or memory tools before continuing. " +
                        "Leaving them open may end your session.",
                };
            }

            return new Verdict { Action = VerdictAction.Continue };
        }

        /// <summary>
        /// Raises a detection the server itself observed, e.g. a bad signature or a guard
        /// that went quiet. These never come from the client, which is exactly why they are
        /// worth something.
        /// </summary>
        public void RecordServerSide(GuardSession session, DetectionCode code, string subject, int confidence,
            IDictionary<string, string>? evidence = null)
        {
            var detection = new Detection
            {
                Code = code,
                Severity = Severity.High,
                Confidence = confidence,
                Subject = subject,
                Detector = "server",
                FirstObservedUtc = DateTimeOffset.UtcNow,
                LastObservedUtc = DateTimeOffset.UtcNow,
            };

            if (evidence != null)
            {
                foreach (KeyValuePair<string, string> pair in evidence)
                    detection.Evidence[pair.Key] = pair.Value;
            }

            DetectionCatalog.Entry entry = DetectionCatalog.For(code);
            session.Record(detection, entry.Weight * (confidence / 100.0));

            _logger.LogWarning(
                "Server-side detection {Code} subject={Subject} player={Player}",
                code, subject, session.PlayerId);
        }
    }
}
