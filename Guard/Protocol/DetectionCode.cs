namespace VanadiumGuard.Protocol
{
    /// <summary>
    /// Stable identifiers for everything the guard can observe. These values are persisted
    /// and appear in review tooling, so they are never reused or renumbered.
    /// </summary>
    public enum DetectionCode
    {
        Unknown = 0,

        // 1xxx - modules loaded into the game process
        BlacklistedModuleLoaded = 1001,
        ModuleFromUntrustedPath = 1002,
        ModuleFromVolatilePath = 1003,
        ModuleUnbackedByDisk = 1004,

        // 2xxx - the rest of the machine
        BlacklistedProcess = 2001,
        BlacklistedWindow = 2002,
        DebuggerAttached = 2003,
        HttpInterceptionTool = 2004,

        // 3xxx - native code integrity
        NtExportPrologueModified = 3001,
        GameCodeModified = 3002,

        // 4xxx - managed (CLR) integrity
        ForeignHarmonyPatch = 4001,
        EnforcementMethodPatched = 4002,

        // 5xxx - IL2CPP method integrity
        Il2CppMethodHooked = 5001,

        // 6xxx - guard integrity. Raised by the SERVER, never by the client.
        ChallengeFailed = 6001,
        ChallengeUnreadable = 6002,
        GuardSilent = 6003,
        SignatureInvalid = 6004,
        SequenceReplay = 6005,
        PolicyRefused = 6006,

        // 7xxx - hardware bans. Raised by the SERVER; the client only carries the identity.
        MachineBanned = 7001,
        MachineLinkedToBan = 7002,
        MachineUnidentifiable = 7003,

        // 8xxx - device attestation. Raised by the SERVER.
        AttestationInvalid = 8001,
        AttestationMissing = 8002,
    }
}
