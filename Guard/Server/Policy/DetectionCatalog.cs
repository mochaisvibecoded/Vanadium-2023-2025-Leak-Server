using System.Collections.Generic;
using VanadiumGuard.Protocol;

namespace VanadiumGuard.Server.Policy
{
    /// <summary>
    /// What each detection is worth, and whether it may ever act on its own.
    /// <para>
    /// This table is the whole reason the redesign exists. In the old build every signal was
    /// worth the same thing - immediate death - so a Cheat Engine window and someone having
    /// a file named injector.exe in Downloads had identical consequences. Here a signal has
    /// a weight, and only a handful of signals are trusted enough to end a session by
    /// themselves.
    /// </para>
    /// </summary>
    public static class DetectionCatalog
    {
        public sealed class Entry
        {
            /// <summary>Score contribution at 100 confidence. Scores accumulate toward 100.</summary>
            public double Weight { get; init; }

            /// <summary>
            /// Whether this code alone, at high confidence, justifies removing the player.
            /// Reserved for direct evidence of code modification inside the game process.
            /// </summary>
            public bool Decisive { get; init; }

            /// <summary>Human-readable, used in review tooling and audit logs.</summary>
            public string Description { get; init; } = string.Empty;
        }

        private static readonly Dictionary<DetectionCode, Entry> Entries = new()
        {
            // Modules. Loading a known cheat framework into the process is strong, but the
            // name match is still a name match, so it is not decisive on its own.
            [DetectionCode.BlacklistedModuleLoaded] = new Entry
            {
                Weight = 55, Decisive = false, Description = "Known cheat or hooking library mapped into the game",
            },
            [DetectionCode.ModuleUnbackedByDisk] = new Entry
            {
                Weight = 45, Decisive = false, Description = "Module present without a loader notification (manual map)",
            },
            [DetectionCode.ModuleFromVolatilePath] = new Entry
            {
                Weight = 8, Decisive = false, Description = "Module loaded from a temp or downloads directory",
            },
            [DetectionCode.ModuleFromUntrustedPath] = new Entry
            {
                Weight = 5, Decisive = false, Description = "Module loaded from outside the trusted roots",
            },

            // External environment. Weak by construction: what is open on someone's second
            // monitor is not evidence of what is happening inside the game.
            [DetectionCode.BlacklistedProcess] = new Entry
            {
                Weight = 12, Decisive = false, Description = "A listed tool is running on the machine",
            },
            [DetectionCode.BlacklistedWindow] = new Entry
            {
                Weight = 10, Decisive = false, Description = "A listed tool has a visible window",
            },
            [DetectionCode.DebuggerAttached] = new Entry
            {
                Weight = 60, Decisive = false, Description = "A debugger is attached to the game process",
            },

            // Code integrity. This is the evidence worth acting on: something modified
            // executable memory inside the process, which nothing legitimate does here.
            [DetectionCode.NtExportPrologueModified] = new Entry
            {
                Weight = 70, Decisive = false, Description = "Syscall stub prologue rewritten as a detour",
            },
            [DetectionCode.GameCodeModified] = new Entry
            {
                Weight = 85, Decisive = true, Description = "Game code section modified in memory",
            },
            [DetectionCode.Il2CppMethodHooked] = new Entry
            {
                Weight = 90, Decisive = true, Description = "Inline hook on a watched gameplay method",
            },

            // Managed integrity.
            [DetectionCode.ForeignHarmonyPatch] = new Entry
            {
                Weight = 6, Decisive = false, Description = "Harmony patch from an unrecognised mod",
            },
            [DetectionCode.EnforcementMethodPatched] = new Entry
            {
                Weight = 95, Decisive = true, Description = "The guard or process exit path was patched",
            },

            // Guard integrity, raised server-side. Tampering with the guard is treated as
            // seriously as tampering with the game, because it is done for the same reason.
            [DetectionCode.ChallengeFailed] = new Entry
            {
                Weight = 90, Decisive = true, Description = "Memory challenge answered with the wrong hash",
            },
            [DetectionCode.ChallengeUnreadable] = new Entry
            {
                Weight = 20, Decisive = false, Description = "Client could not read a challenged region",
            },
            [DetectionCode.GuardSilent] = new Entry
            {
                Weight = 50, Decisive = false, Description = "Guard stopped heartbeating while the match was live",
            },
            [DetectionCode.SignatureInvalid] = new Entry
            {
                Weight = 70, Decisive = false, Description = "Request signature did not verify",
            },
            [DetectionCode.SequenceReplay] = new Entry
            {
                Weight = 70, Decisive = false, Description = "Replayed or reordered request sequence",
            },
            [DetectionCode.PolicyRefused] = new Entry
            {
                Weight = 15, Decisive = false, Description = "Client refused to apply the issued policy",
            },

            // Hardware bans. These are recorded for the audit trail, not to move a score:
            // a machine ban is a decision that was already made by a person, and running it
            // back through the scoring path would let a threshold second-guess them.
            [DetectionCode.MachineBanned] = new Entry
            {
                Weight = 0, Decisive = false, Description = "Session opened from a banned machine",
            },
            [DetectionCode.MachineLinkedToBan] = new Entry
            {
                Weight = 0, Decisive = false, Description = "Machine shares hardware with a banned machine",
            },
            [DetectionCode.MachineUnidentifiable] = new Entry
            {
                Weight = 0, Decisive = false, Description = "Client could not produce a machine identity",
            },

            // Device attestation. A session that proved it held a device key and then stopped
            // signing with it is claiming to be somewhere it is not, which is the one thing
            // the HMAC alone can never catch.
            [DetectionCode.AttestationInvalid] = new Entry
            {
                Weight = 70, Decisive = false, Description = "Request not signed by the bound device key",
            },
            [DetectionCode.AttestationMissing] = new Entry
            {
                Weight = 0, Decisive = false, Description = "Session opened without a hardware-backed key",
            },
        };

        public static Entry For(DetectionCode code)
        {
            return Entries.TryGetValue(code, out Entry? entry)
                ? entry
                : new Entry { Weight = 0, Decisive = false, Description = "Unknown detection code" };
        }
    }
}
