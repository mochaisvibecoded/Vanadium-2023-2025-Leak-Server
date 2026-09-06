using System;
using System.Collections.Generic;
using System.Linq;
using VanadiumGuard.Protocol;

namespace VanadiumGuard.Server.Sessions
{
    /// <summary>
    /// Everything the service knows about one player's guard for one play session.
    /// <para>
    /// Instances are mutated from concurrent requests, so all access goes through the
    /// instance lock. Sessions are short-lived and low in number relative to request rate,
    /// so a lock per session is cheaper and far easier to reason about than making every
    /// field individually atomic.
    /// </para>
    /// </summary>
    public sealed class GuardSession
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, RecordedDetection> _detections = new Dictionary<string, RecordedDetection>();
        private readonly HashSet<string> _seenNonces = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, MemoryChallenge> _openChallenges = new Dictionary<string, MemoryChallenge>();

        public GuardSession(string sessionId, byte[] sessionKey, string playerId, string gameSessionId, string gameBuild)
        {
            SessionId = sessionId;
            SessionKey = sessionKey;
            PlayerId = playerId;
            GameSessionId = gameSessionId;
            GameBuild = gameBuild;
            OpenedUtc = DateTimeOffset.UtcNow;
            _lastSeenUtc = OpenedUtc;
        }

        public string SessionId { get; }

        public byte[] SessionKey { get; }

        public string PlayerId { get; }

        public string GameSessionId { get; }

        public string GameBuild { get; }

        public DateTimeOffset OpenedUtc { get; }

        // DateTimeOffset is wider than a machine word, so these are read under the lock
        // rather than exposed as plain auto-properties. The sweeper reads them from a
        // different thread than the request handlers that write them.
        private DateTimeOffset _lastSeenUtc;
        private DateTimeOffset? _lastHeartbeatUtc;

        public DateTimeOffset LastSeenUtc
        {
            get { lock (_gate) { return _lastSeenUtc; } }
        }

        public DateTimeOffset? LastHeartbeatUtc
        {
            get { lock (_gate) { return _lastHeartbeatUtc; } }
        }

        public string PolicyVersion { get; set; } = "0";

        /// <summary>
        /// Composite machine id this session opened from, empty when the client could not
        /// identify the machine. Held so a hardware ban issued mid-match can find the live
        /// sessions it applies to.
        /// </summary>
        public string MachineComposite { get; set; } = string.Empty;

        /// <summary>
        /// Base64 public key bound at session open, empty when the client had none. When set,
        /// every signed request must also carry a signature from its private half.
        /// </summary>
        public string AttestationKey { get; set; } = string.Empty;

        /// <summary>What is holding that key. Only Tpm counts as attested.</summary>
        public AttestationBacking KeyBacking { get; set; } = AttestationBacking.None;

        /// <summary>
        /// True when requests on this session are proven to come from the machine that opened
        /// it. A software key is deliberately not enough: the guard can read it, so anyone
        /// with the same access can too.
        /// </summary>
        public bool IsAttested => KeyBacking == AttestationBacking.Tpm && AttestationKey.Length > 0;

        /// <summary>Set once a disconnect has been issued, so it is not issued repeatedly.</summary>
        public bool Terminated { get; private set; }

        /// <summary>Highest sequence number accepted. Replays and reorders are rejected.</summary>
        public long LastSequence { get; private set; }

        /// <summary>
        /// Set when something outside the scoring path has already decided this session is
        /// over - today, a hardware ban issued while the player was mid-match. Held so the
        /// player gets the real reason instead of a generic termination code.
        /// </summary>
        private Verdict? _forced;

        public Verdict? ForcedVerdict
        {
            get { lock (_gate) { return _forced; } }
        }

        public void Force(Verdict verdict)
        {
            lock (_gate)
            {
                _forced = verdict;
                Terminated = true;
            }
        }

        public sealed class RecordedDetection
        {
            public Detection Detection { get; init; } = null!;
            public double Score { get; set; }
            public DateTimeOffset FirstUtc { get; init; }
            public DateTimeOffset LastUtc { get; set; }
        }

        /// <summary>
        /// Checks a request's sequence and nonce. Returns false for a replay.
        /// </summary>
        public bool AcceptRequest(long sequence, string nonce)
        {
            lock (_gate)
            {
                if (sequence <= LastSequence)
                    return false;

                if (!_seenNonces.Add(nonce))
                    return false;

                // The nonce set is bounded by the sequence check anyway; trim it so a long
                // session cannot grow it without limit.
                if (_seenNonces.Count > 4096)
                    _seenNonces.Clear();

                LastSequence = sequence;
                _lastSeenUtc = DateTimeOffset.UtcNow;
                return true;
            }
        }

        public void MarkHeartbeat()
        {
            lock (_gate)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                _lastHeartbeatUtc = now;
                _lastSeenUtc = now;
            }
        }

        public void MarkTerminated()
        {
            lock (_gate)
            {
                Terminated = true;
            }
        }

        /// <summary>
        /// Records a detection and its score contribution. Repeats of the same subject add
        /// a heavily damped amount, so a detector firing in a loop cannot inflate a score
        /// past the threshold on its own.
        /// </summary>
        public void Record(Detection detection, double score)
        {
            lock (_gate)
            {
                string key = detection.DedupeKey();

                if (_detections.TryGetValue(key, out RecordedDetection? existing))
                {
                    existing.LastUtc = DateTimeOffset.UtcNow;
                    existing.Score += score * 0.1;
                    existing.Detection.OccurrenceCount += detection.OccurrenceCount;
                    return;
                }

                _detections[key] = new RecordedDetection
                {
                    Detection = detection,
                    Score = score,
                    FirstUtc = DateTimeOffset.UtcNow,
                    LastUtc = DateTimeOffset.UtcNow,
                };
            }
        }

        /// <summary>Total risk score, capped at 100.</summary>
        public int RiskScore()
        {
            lock (_gate)
            {
                double total = _detections.Values.Sum(d => d.Score);
                return (int)Math.Clamp(total, 0, 100);
            }
        }

        public IReadOnlyList<RecordedDetection> Detections()
        {
            lock (_gate)
            {
                return _detections.Values
                    .OrderByDescending(d => d.Score)
                    .ToList();
            }
        }

        public void AddChallenge(MemoryChallenge challenge)
        {
            lock (_gate)
            {
                _openChallenges[challenge.ChallengeId] = challenge;
            }
        }

        /// <summary>Takes a challenge out of the open set. Null means unknown or already answered.</summary>
        public MemoryChallenge? ClaimChallenge(string challengeId)
        {
            lock (_gate)
            {
                if (!_openChallenges.TryGetValue(challengeId, out MemoryChallenge? challenge))
                    return null;

                _openChallenges.Remove(challengeId);
                return challenge;
            }
        }

        /// <summary>Challenges issued long enough ago that an answer should have arrived.</summary>
        public IReadOnlyList<MemoryChallenge> ExpiredChallenges(TimeSpan age)
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow - age;

            lock (_gate)
            {
                var expired = _openChallenges.Values.Where(c => c.IssuedUtc < cutoff).ToList();
                foreach (MemoryChallenge challenge in expired)
                    _openChallenges.Remove(challenge.ChallengeId);
                return expired;
            }
        }
    }
}
