using System;
using System.Collections.Generic;

namespace VanadiumGuard.Protocol
{
    /// <summary>
    /// One observation. The client fills these in and ships them; it draws no conclusions.
    /// </summary>
    public sealed class Detection
    {
        public DetectionCode Code { get; set; }

        public Severity Severity { get; set; }

        /// <summary>
        /// 0-100. How sure the client is that the observation is what it looks like, rather
        /// than a legitimate program that happens to match. The policy engine multiplies
        /// weight by confidence, so a noisy heuristic can stay enabled at low confidence
        /// without ever reaching a disconnect on its own.
        /// </summary>
        public int Confidence { get; set; }

        /// <summary>Normalised subject: module base name, process image name, method signature.</summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>Which detector produced this, for triage.</summary>
        public string Detector { get; set; } = string.Empty;

        /// <summary>Supporting detail. Keep it small; it is stored verbatim.</summary>
        public Dictionary<string, string> Evidence { get; set; } = new Dictionary<string, string>();

        public DateTimeOffset FirstObservedUtc { get; set; }

        public DateTimeOffset LastObservedUtc { get; set; }

        /// <summary>Times this exact Code plus Subject pair was seen before the batch flushed.</summary>
        public int OccurrenceCount { get; set; } = 1;

        /// <summary>Dedupe key used by both the client sink and the server store.</summary>
        public string DedupeKey()
        {
            return ((int)Code).ToString() + "|" + Subject;
        }
    }
}
