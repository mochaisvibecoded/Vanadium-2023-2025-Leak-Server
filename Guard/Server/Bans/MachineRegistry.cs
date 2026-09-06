using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VanadiumGuard.Protocol;

namespace VanadiumGuard.Server.Bans
{
    /// <summary>
    /// The machine link table and the hardware ban list, held in memory and persisted to one
    /// JSON file.
    /// <para>
    /// Two decisions are worth stating outright. A ban matches on the composite id or on any
    /// single strong component, because someone who swaps a disk to get back in should not
    /// thereby be a new person - but only strong components count, since matching on a CPU
    /// model would ban everyone who bought the same processor. And a ban here refuses the
    /// guard session, which is a different thing from banning an account: the account ban
    /// belongs to the game's own moderation system, and this service does not reach into it.
    /// </para>
    /// <para>
    /// A file is the right store at this scale and the wrong one past it. Everything goes
    /// through this class precisely so the swap to a real database is one file to rewrite.
    /// </para>
    /// </summary>
    public sealed class MachineRegistry
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly ILogger<MachineRegistry> _logger;
        private readonly string _path;
        private readonly object _gate = new object();

        private readonly Dictionary<string, MachineRecord> _byComposite =
            new Dictionary<string, MachineRecord>(StringComparer.Ordinal);

        public MachineRegistry(ILogger<MachineRegistry> logger, string banDirectory)
        {
            _logger = logger;
            _path = Path.Combine(banDirectory, "machines.json");

            Load();
        }

        /// <summary>
        /// Records that a player opened a session from this machine, and returns the stored
        /// record. Unusable fingerprints are ignored rather than stored under an empty key.
        /// </summary>
        public MachineRecord? Observe(MachineFingerprint? fingerprint, string playerId)
        {
            if (fingerprint == null || !fingerprint.IsUsable)
                return null;

            bool changed = false;
            MachineRecord record;

            lock (_gate)
            {
                if (!_byComposite.TryGetValue(fingerprint.Composite, out MachineRecord? existing))
                {
                    existing = new MachineRecord
                    {
                        Composite = fingerprint.Composite,
                        Components = new Dictionary<string, string>(fingerprint.Components, StringComparer.Ordinal),
                        FirstSeenUtc = DateTimeOffset.UtcNow,
                    };

                    _byComposite[fingerprint.Composite] = existing;
                    changed = true;
                }
                else if (MergeComponents(existing, fingerprint))
                {
                    changed = true;
                }

                existing.LastSeenUtc = DateTimeOffset.UtcNow;

                if (!string.IsNullOrEmpty(playerId) && !existing.PlayerIds.Contains(playerId, StringComparer.Ordinal))
                {
                    existing.PlayerIds.Add(playerId);
                    changed = true;
                }

                record = existing;

                // LastSeen alone is not worth a write on every session open. Anything that
                // changes what a moderator would see is.
                if (changed)
                    Save();
            }

            return record;
        }

        /// <summary>
        /// The active ban covering this machine, or null. Checks the exact machine first,
        /// then every banned machine for a shared strong component.
        /// </summary>
        public (MachineRecord Record, MachineBan Ban)? FindActiveBan(MachineFingerprint? fingerprint)
        {
            if (fingerprint == null || !fingerprint.IsUsable)
                return null;

            DateTimeOffset now = DateTimeOffset.UtcNow;

            lock (_gate)
            {
                if (_byComposite.TryGetValue(fingerprint.Composite, out MachineRecord? exact)
                    && exact.Ban != null
                    && exact.Ban.IsActive(now))
                {
                    return (exact, exact.Ban);
                }

                foreach (MachineRecord record in _byComposite.Values)
                {
                    if (record.Ban == null || !record.Ban.IsActive(now))
                        continue;

                    if (SharesStrongComponent(record, fingerprint))
                        return (record, record.Ban);
                }
            }

            return null;
        }

        /// <summary>
        /// Bans one machine by its composite id. Returns false when the machine has never
        /// been seen: banning a hash nobody has reported would put a record in the list that
        /// can never match anything and can never be reviewed.
        /// </summary>
        public bool Ban(string composite, string reason, string issuedBy, DateTimeOffset? expiresUtc)
        {
            if (string.IsNullOrWhiteSpace(composite))
                return false;

            lock (_gate)
            {
                if (!_byComposite.TryGetValue(composite, out MachineRecord? record))
                    return false;

                record.Ban = new MachineBan
                {
                    Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim(),
                    IssuedBy = string.IsNullOrWhiteSpace(issuedBy) ? "unknown" : issuedBy.Trim(),
                    IssuedUtc = DateTimeOffset.UtcNow,
                    ExpiresUtc = expiresUtc,
                    LinkedPlayerIdsAtIssue = new List<string>(record.PlayerIds),
                };

                Save();
            }

            _logger.LogWarning(
                "Machine {Composite} banned by {IssuedBy} until {Expires}: {Reason}",
                composite, issuedBy, expiresUtc?.ToString("O") ?? "permanent", reason);

            return true;
        }

        /// <summary>
        /// Bans every machine this player has been seen on. This is how a moderator uses the
        /// feature in practice: they know an account, not a hash.
        /// </summary>
        public IReadOnlyList<string> BanMachinesOf(string playerId, string reason, string issuedBy, DateTimeOffset? expiresUtc)
        {
            var banned = new List<string>();

            foreach (MachineRecord record in ForPlayer(playerId))
            {
                if (Ban(record.Composite, reason, issuedBy, expiresUtc))
                    banned.Add(record.Composite);
            }

            if (banned.Count == 0)
                _logger.LogInformation("No machines on record for player {Player}; nothing banned", playerId);

            return banned;
        }

        /// <summary>Lifts a ban. The record stays; only the ban is removed.</summary>
        public bool Unban(string composite, string liftedBy, string reason)
        {
            lock (_gate)
            {
                if (!_byComposite.TryGetValue(composite, out MachineRecord? record) || record.Ban == null)
                    return false;

                record.Ban = null;
                Save();
            }

            _logger.LogWarning("Machine {Composite} unbanned by {LiftedBy}: {Reason}", composite, liftedBy, reason);
            return true;
        }

        public MachineRecord? Get(string composite)
        {
            lock (_gate)
            {
                return _byComposite.TryGetValue(composite, out MachineRecord? record) ? record : null;
            }
        }

        public IReadOnlyList<MachineRecord> ForPlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
                return Array.Empty<MachineRecord>();

            lock (_gate)
            {
                return _byComposite.Values
                    .Where(r => r.PlayerIds.Contains(playerId, StringComparer.Ordinal))
                    .ToList();
            }
        }

        /// <summary>
        /// True when the record and the fingerprint agree on a component specific enough to
        /// mean the same machine.
        /// </summary>
        private static bool SharesStrongComponent(MachineRecord record, MachineFingerprint fingerprint)
        {
            foreach (string name in MachineComponents.Strong)
            {
                if (record.Components.TryGetValue(name, out string? stored)
                    && fingerprint.Components.TryGetValue(name, out string? offered)
                    && !string.IsNullOrEmpty(stored)
                    && string.Equals(stored, offered, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Folds newly reported components into a stored record. Components are added but
        /// never overwritten: a machine that suddenly reports a different motherboard hash
        /// under the same composite is either a hardware change or an attempt to overwrite
        /// the thing a ban matches on, and keeping the first value read makes both harmless.
        /// </summary>
        private static bool MergeComponents(MachineRecord record, MachineFingerprint fingerprint)
        {
            bool changed = false;

            foreach (KeyValuePair<string, string> pair in fingerprint.Components)
            {
                if (!record.Components.ContainsKey(pair.Key))
                {
                    record.Components[pair.Key] = pair.Value;
                    changed = true;
                }
            }

            return changed;
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    _logger.LogInformation("No machine registry at {Path}; starting empty", _path);
                    return;
                }

                MachineRecord[]? records = JsonSerializer.Deserialize<MachineRecord[]>(
                    File.ReadAllText(_path), Json);

                if (records == null)
                    return;

                foreach (MachineRecord record in records)
                {
                    if (!string.IsNullOrEmpty(record.Composite))
                        _byComposite[record.Composite] = record;
                }

                int banned = _byComposite.Values.Count(r => r.Ban != null);
                _logger.LogInformation("Machine registry loaded: {Total} machines, {Banned} banned", _byComposite.Count, banned);
            }
            catch (Exception ex)
            {
                // Starting with an empty ban list would silently let every banned machine
                // back in, so this is loud and the file is left alone for a human to look at.
                _logger.LogError(ex, "Machine registry at {Path} could not be read; hardware bans are NOT in effect", _path);
            }
        }

        /// <summary>
        /// Writes the registry through a temporary file. Called with the lock held.
        /// <para>
        /// The rename matters more than it looks: this file is the only record of who is
        /// banned, and a process killed midway through a plain overwrite would leave a
        /// truncated file that fails to parse and unbans everyone at the next start.
        /// </para>
        /// </summary>
        private void Save()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_byComposite.Values.ToArray(), Json));

                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Machine registry could not be written to {Path}", _path);
            }
        }
    }
}
