using System;
using System.Collections.Generic;

namespace VanadiumGuard.Protocol
{
    /// <summary>
    /// A machine identity, as reported by the client at session open.
    /// <para>
    /// Every value in here is already a salted hash when it leaves the player's machine.
    /// The service never sees a disk serial or a motherboard serial, only opaque strings it
    /// can compare for equality. That is deliberate: a ban list is a long-lived store of
    /// data about people who are not around to consent to it, so it holds the least it can
    /// while still doing its job.
    /// </para>
    /// <para>
    /// The composite is the primary identity. The individual components exist so a ban can
    /// survive one part being swapped: someone who replaces a disk keeps the same
    /// motherboard and the same Windows install id, and matching on any single strong
    /// component still recognises the machine. That is also why a component must be strong
    /// to count - matching on a CPU model would ban everyone who bought the same processor.
    /// </para>
    /// </summary>
    public sealed class MachineFingerprint
    {
        /// <summary>Hex SHA-256 over every component. Empty when collection failed.</summary>
        public string Composite { get; set; } = string.Empty;

        /// <summary>Per-source hashes, keyed by the names in <see cref="MachineComponents"/>.</summary>
        public Dictionary<string, string> Components { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public DateTimeOffset CollectedUtc { get; set; }

        public bool IsUsable => !string.IsNullOrEmpty(Composite);

        /// <summary>
        /// True when both fingerprints name the same machine: the same composite, or any
        /// shared strong component. Weak components are carried for triage and are never
        /// enough on their own.
        /// </summary>
        public bool Matches(MachineFingerprint? other)
        {
            if (other == null || !IsUsable || !other.IsUsable)
                return false;

            if (string.Equals(Composite, other.Composite, StringComparison.Ordinal))
                return true;

            foreach (string name in MachineComponents.Strong)
            {
                if (Components.TryGetValue(name, out string? mine)
                    && other.Components.TryGetValue(name, out string? theirs)
                    && !string.IsNullOrEmpty(mine)
                    && string.Equals(mine, theirs, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Which strong components two fingerprints agree on. For audit logs.</summary>
        public IReadOnlyList<string> SharedStrongComponents(MachineFingerprint? other)
        {
            var shared = new List<string>();

            if (other == null)
                return shared;

            foreach (string name in MachineComponents.Strong)
            {
                if (Components.TryGetValue(name, out string? mine)
                    && other.Components.TryGetValue(name, out string? theirs)
                    && !string.IsNullOrEmpty(mine)
                    && string.Equals(mine, theirs, StringComparison.Ordinal))
                {
                    shared.Add(name);
                }
            }

            return shared;
        }
    }

    /// <summary>
    /// Component names, and which of them identify one machine rather than one model of
    /// machine. Names are persisted in ban records, so they are never renamed.
    /// </summary>
    public static class MachineComponents
    {
        /// <summary>Windows installation id. Survives hardware changes, dies on reinstall.</summary>
        public const string MachineGuid = "machineGuid";

        /// <summary>Volume serial of the system drive. Dies on reformat.</summary>
        public const string SystemVolume = "systemVolume";

        /// <summary>Motherboard identity, serial included. The most durable component there is.</summary>
        public const string Baseboard = "baseboard";

        /// <summary>BIOS vendor, version and date. Weak: it names a model, not a machine.</summary>
        public const string Bios = "bios";

        /// <summary>Processor identifier string. A model, not a machine - weak by design.</summary>
        public const string Processor = "processor";

        /// <summary>OS build. Triage only; changes with every update.</summary>
        public const string OsBuild = "osBuild";

        /// <summary>
        /// Components specific enough to identify one machine. Only these are used to
        /// recognise a banned machine whose composite has changed.
        /// </summary>
        public static readonly string[] Strong = { MachineGuid, SystemVolume, Baseboard };

        /// <summary>Carried for review tooling, never used for matching.</summary>
        public static readonly string[] Weak = { Bios, Processor, OsBuild };
    }
}
