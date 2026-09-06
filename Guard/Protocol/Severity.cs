namespace VanadiumGuard.Protocol
{
    /// <summary>
    /// How much a detection is worth on its own. Severity is a property of the signal;
    /// what to do about it is decided by the server policy engine, never by the client.
    /// </summary>
    public enum Severity
    {
        /// <summary>Recorded for context. Never contributes to a verdict.</summary>
        Info = 0,
        /// <summary>Weak signal. Meaningful only in aggregate.</summary>
        Low = 1,
        /// <summary>Suspicious but individually explainable.</summary>
        Medium = 2,
        /// <summary>Hard to explain without tampering.</summary>
        High = 3,
        /// <summary>Direct evidence of memory or code tampering.</summary>
        Critical = 4,
    }
}
