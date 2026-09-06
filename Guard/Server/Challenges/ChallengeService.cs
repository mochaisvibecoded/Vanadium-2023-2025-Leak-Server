using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VanadiumGuard.Protocol;
using VanadiumGuard.Server.Policy;
using VanadiumGuard.Server.Sessions;

namespace VanadiumGuard.Server.Challenges
{
    /// <summary>
    /// Issues memory challenges and grades the answers.
    /// <para>
    /// A challenge asks the client to hash a random region of a known module with a random
    /// salt. Three outcomes matter: the right hash means a real guard is running inside a
    /// real image; the wrong hash means the memory it read does not match what shipped;
    /// no answer at all means the guard is not running, which is graded by the liveness
    /// sweeper rather than here.
    /// </para>
    /// </summary>
    public sealed class ChallengeService
    {
        /// <summary>How long a client has to answer before the challenge is written off.</summary>
        public static readonly TimeSpan AnswerWindow = TimeSpan.FromSeconds(60);

        private const int MinReadLength = 256;

        private readonly ILogger<ChallengeService> _logger;
        private readonly IReferenceImageStore _images;
        private readonly PolicyEngine _policy;

        public ChallengeService(ILogger<ChallengeService> logger, IReferenceImageStore images, PolicyEngine policy)
        {
            _logger = logger;
            _images = images;
            _policy = policy;
        }

        /// <summary>Builds the next challenge for a session, or null if none can be issued.</summary>
        public MemoryChallenge? Issue(GuardSession session, GuardPolicy policy)
        {
            string[] modules = policy.ChallengeableModules;
            if (modules.Length == 0)
                return null;

            // Try each module once, in random order, so an unavailable reference image for
            // one module does not silently stop challenges altogether.
            foreach (string module in Shuffle(modules))
            {
                long size = _images.Size(session.GameBuild, module);
                if (size < MinReadLength)
                    continue;

                int length = RandomNumberGenerator.GetInt32(MinReadLength, ProtocolConstants.MaxChallengeReadLength + 1);
                if (length > size)
                    length = (int)Math.Min(size, ProtocolConstants.MaxChallengeReadLength);

                uint rva = (uint)RandomNumberGenerator.GetInt32(0, (int)Math.Min(int.MaxValue, size - length + 1));

                var salt = new byte[16];
                RandomNumberGenerator.Fill(salt);

                var challenge = new MemoryChallenge
                {
                    ChallengeId = Guid.NewGuid().ToString("N"),
                    Module = module,
                    Rva = rva,
                    Length = length,
                    Salt = Convert.ToHexString(salt).ToLowerInvariant(),
                    IssuedUtc = DateTimeOffset.UtcNow,
                };

                session.AddChallenge(challenge);
                return challenge;
            }

            return null;
        }

        /// <summary>Grades answers, recording detections for anything that does not add up.</summary>
        public void Grade(GuardSession session, IReadOnlyList<ChallengeAnswer> answers)
        {
            foreach (ChallengeAnswer answer in answers)
            {
                MemoryChallenge? challenge = session.ClaimChallenge(answer.ChallengeId);

                if (challenge == null)
                {
                    // An answer to something never asked, or asked twice. Not worth a
                    // detection on its own, but worth knowing about.
                    _logger.LogInformation(
                        "Unsolicited challenge answer {Id} from session {Session}",
                        answer.ChallengeId, session.SessionId);
                    continue;
                }

                if (!answer.Readable)
                {
                    _policy.RecordServerSide(
                        session, DetectionCode.ChallengeUnreadable, challenge.Module, 60,
                        new Dictionary<string, string>
                        {
                            ["error"] = answer.Error ?? "unspecified",
                            ["rva"] = challenge.Rva.ToString(),
                        });
                    continue;
                }

                byte[]? reference = _images.Read(session.GameBuild, challenge.Module, challenge.Rva, challenge.Length);

                if (reference == null)
                {
                    // No reference image, so this answer proves liveness but nothing else.
                    // Explicitly NOT a detection: failing players because the operator has
                    // not published a build manifest would be the worst kind of bug.
                    _logger.LogDebug(
                        "No reference for build={Build} module={Module}; challenge {Id} counts as liveness only",
                        session.GameBuild, challenge.Module, challenge.ChallengeId);
                    continue;
                }

                string expected = RequestSigner.HashWithSalt(Convert.FromHexString(challenge.Salt), reference);

                if (string.Equals(expected, answer.Hash, StringComparison.OrdinalIgnoreCase))
                    continue;

                _policy.RecordServerSide(
                    session, DetectionCode.ChallengeFailed, challenge.Module, 95,
                    new Dictionary<string, string>
                    {
                        ["rva"] = "0x" + challenge.Rva.ToString("X"),
                        ["length"] = challenge.Length.ToString(),
                        ["expected"] = expected,
                        ["received"] = answer.Hash,
                    });
            }
        }

        private static IEnumerable<string> Shuffle(string[] source)
        {
            var copy = (string[])source.Clone();

            for (int i = copy.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (copy[i], copy[j]) = (copy[j], copy[i]);
            }

            return copy;
        }
    }
}
