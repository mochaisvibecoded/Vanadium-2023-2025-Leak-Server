using System;

namespace VanadiumGuard.Protocol
{
    /// <summary>
    /// Everything the client needs in order to know what to look for, delivered by the
    /// server at session open.
    /// <para>
    /// Shipping this from the server rather than compiling it in is the single most useful
    /// operational property of the whole design: a detector that starts producing false
    /// positives in production can be turned off, or dropped to observe-only, without
    /// shipping a new DLL to every player.
    /// </para>
    /// </summary>
    public sealed class GuardPolicy
    {
        /// <summary>Identifies this policy revision. Echoed back on every report.</summary>
        public string PolicyVersion { get; set; } = "0";

        public int ReportIntervalMs { get; set; } = 5000;

        public int HeartbeatIntervalMs { get; set; } = 15000;

        /// <summary>Per-detector switches, keyed by detector name. Missing means disabled.</summary>
        public DetectorSettings[] Detectors { get; set; } = Array.Empty<DetectorSettings>();

        /// <summary>Substrings matched against loaded module base names.</summary>
        public string[] ModuleDenyList { get; set; } = Array.Empty<string>();

        /// <summary>Module name prefixes that are always fine, checked before the deny list.</summary>
        public string[] ModuleAllowPrefixes { get; set; } = Array.Empty<string>();

        /// <summary>Directory prefixes whose modules are trusted, in lowercase.</summary>
        public string[] TrustedPathPrefixes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Exact process image names, lowercase with extension. Exact match only - substring
        /// matching is what made the old build kill players for running Regedit.
        /// </summary>
        public string[] ProcessDenyList { get; set; } = Array.Empty<string>();

        /// <summary>Window class names requiring an accompanying title match to count.</summary>
        public string[] WindowClassDenyList { get; set; } = Array.Empty<string>();

        /// <summary>Window title substrings. Matched case-insensitively.</summary>
        public string[] WindowTitleDenyList { get; set; } = Array.Empty<string>();

        /// <summary>Native exports whose prologues are snapshotted and re-verified.</summary>
        public WatchedExport[] WatchedExports { get; set; } = Array.Empty<WatchedExport>();

        /// <summary>IL2CPP methods to watch for inline hooks.</summary>
        public Il2CppTarget[] Il2CppTargets { get; set; } = Array.Empty<Il2CppTarget>();

        /// <summary>Harmony patch owner ids that are expected and allowed.</summary>
        public string[] AllowedPatchOwners { get; set; } = Array.Empty<string>();

        /// <summary>Modules the server is permitted to issue memory challenges against.</summary>
        public string[] ChallengeableModules { get; set; } = Array.Empty<string>();
    }

    public sealed class DetectorSettings
    {
        public string Name { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        /// <summary>Overrides the detector default. Zero means use the default.</summary>
        public int IntervalMs { get; set; }

        /// <summary>
        /// Caps the confidence of everything this detector emits. Set to 0 to keep a
        /// detector running and reporting while contributing nothing to any verdict,
        /// which is how a new heuristic should always be rolled out.
        /// </summary>
        public int MaxConfidence { get; set; } = 100;
    }

    public sealed class WatchedExport
    {
        public string Module { get; set; } = string.Empty;
        public string Export { get; set; } = string.Empty;
    }

    public sealed class Il2CppTarget
    {
        public string TypeName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public int ParameterCount { get; set; }
    }
}
