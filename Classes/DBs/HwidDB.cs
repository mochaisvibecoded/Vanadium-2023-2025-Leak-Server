using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.HwidDBClasses;

namespace Vanadium.Classes.DBs
{
    /// <summary>
    /// Machine identities, the accounts seen on them, and the hardware ban list.
    /// <para>
    /// A hardware ban and an account ban are separate things here on purpose. An account ban
    /// stops one person playing on one account. A hardware ban stops a machine opening any
    /// account at all, which is a heavier tool with a wider blast radius: shared computers,
    /// households, and internet cafes all put unrelated people behind one hwid. That is why
    /// banning a machine also bans the accounts linked to it - so a moderator can see the
    /// consequence in the account list rather than discovering it later - and why every ban
    /// records who issued it and why.
    /// </para>
    /// </summary>
    public static class HwidDB
    {
        public static LiteDatabase DBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Hwid.db") + ";Connection=shared");
        public static readonly ILiteCollection<HwidRecord> Records = DBFile.GetCollection<HwidRecord>("Hwids");

        /// <summary>
        /// Links a machine to an account and returns the banned accounts already on it.
        /// Unchanged behaviour: this is the ban-evasion path, and it is deliberately separate
        /// from the hardware ban list below.
        /// </summary>
        public static List<long> LinkAndGetBannedSiblings(string hwid, long playerId)
        {
            var record = Records.FindById(hwid);
            if (record == null)
            {
                record = new HwidRecord
                {
                    Hwid = hwid,
                    PlayerIds = new List<long> { playerId },
                    FirstSeen = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow
                };
                Records.Insert(record);
            }
            else
            {
                if (!record.PlayerIds.Contains(playerId))
                    record.PlayerIds.Add(playerId);
                record.LastSeen = DateTime.UtcNow;
                Records.Update(record);
            }

            return record.PlayerIds
                .Where(id => id != playerId && PlayerDB.IsBanned(id))
                .ToList();
        }

        public static HwidRecord GetRecord(string hwid)
        {
            return string.IsNullOrEmpty(hwid) ? null : Records.FindById(hwid);
        }

        /// <summary>
        /// Every machine under an active hardware ban, newest first. This is the review
        /// queue: a ban list nobody can enumerate is a ban list nobody audits.
        /// </summary>
        public static List<HwidRecord> AllBanned()
        {
            var now = DateTime.UtcNow;

            return Records.FindAll()
                .Where(r => r.BanIsActive(now))
                .OrderByDescending(r => r.BannedAt ?? DateTime.MinValue)
                .ToList();
        }

        /// <summary>True while an active hardware ban covers this machine.</summary>
        public static bool IsHwidBanned(string hwid)
        {
            var record = GetRecord(hwid);
            return record != null && record.BanIsActive(DateTime.UtcNow);
        }

        /// <summary>
        /// Every machine this account has been seen on. Filtered in memory rather than in a
        /// query: LiteDB does not reliably translate Contains over a stored list, and this
        /// runs on moderation paths, not on the client check-in path.
        /// </summary>
        public static List<HwidRecord> RecordsForPlayer(long playerId)
        {
            return Records.FindAll().Where(r => r.PlayerIds != null && r.PlayerIds.Contains(playerId)).ToList();
        }

        /// <summary>
        /// Bans one machine. Returns the accounts that were banned as a result, which is the
        /// number a moderator actually needs to see before they decide this was the right
        /// call - a machine with eleven accounts on it is a different decision from one with
        /// a single account.
        /// <para>
        /// Refuses a machine it has never seen: a ban on an unknown hwid can never match
        /// anything, and it clutters the list with rows nobody can review.
        /// </para>
        /// </summary>
        public static List<long> BanHwid(string hwid, string reason, string bannedBy, DateTime? expiresAt, bool banLinkedAccounts = true)
        {
            var record = GetRecord(hwid);
            if (record == null)
                return null;

            record.IsBanned = true;
            record.BanReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
            record.BannedBy = string.IsNullOrWhiteSpace(bannedBy) ? "unknown" : bannedBy.Trim();
            record.BannedAt = DateTime.UtcNow;
            record.BanExpiresAt = expiresAt;
            record.LinkedAtBan = new List<long>(record.PlayerIds);
            Records.Update(record);

            var bannedAccounts = new List<long>();

            if (banLinkedAccounts)
            {
                foreach (var playerId in record.PlayerIds)
                {
                    if (PlayerDB.IsBanned(playerId))
                        continue;

                    if (PlayerDB.ApplyBan(playerId, record.BanReason))
                        bannedAccounts.Add(playerId);
                }
            }

            Console.WriteLine($"[AntiCheat] HWID banned by {record.BannedBy}: {hwid} ({record.PlayerIds.Count} linked, {bannedAccounts.Count} newly banned) - {record.BanReason}");

            return bannedAccounts;
        }

        /// <summary>
        /// Bans every machine an account has been seen on. This is the form a moderator
        /// reaches for, because they have an account in front of them and not a hash.
        /// </summary>
        public static (List<string> Hwids, List<long> Accounts) BanMachinesOfPlayer(
            long playerId, string reason, string bannedBy, DateTime? expiresAt)
        {
            var hwids = new List<string>();
            var accounts = new List<long>();

            foreach (var record in RecordsForPlayer(playerId))
            {
                var banned = BanHwid(record.Hwid, reason, bannedBy, expiresAt);
                if (banned == null)
                    continue;

                hwids.Add(record.Hwid);

                foreach (var id in banned)
                {
                    if (!accounts.Contains(id))
                        accounts.Add(id);
                }
            }

            return (hwids, accounts);
        }

        /// <summary>
        /// Lifts a hardware ban. Deliberately does not unban the accounts that were banned
        /// alongside it: some of them were banned for their own behaviour before the machine
        /// ever came up, and quietly reinstating those would undo moderation nobody asked to
        /// undo. Accounts are lifted individually, on purpose, by a person.
        /// </summary>
        public static bool UnbanHwid(string hwid, string liftedBy)
        {
            var record = GetRecord(hwid);
            if (record == null || !record.IsBanned)
                return false;

            record.IsBanned = false;
            record.BanReason = null;
            record.BannedBy = null;
            record.BannedAt = null;
            record.BanExpiresAt = null;
            Records.Update(record);

            Console.WriteLine($"[AntiCheat] HWID unbanned by {liftedBy}: {hwid}");
            return true;
        }
    }
}
