using System;
using System.IO;
using LiteDB;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.AntiCheatDBClasses;

namespace Vanadium.Classes.DBs
{
    public static class AntiCheatDB
    {
        public static LiteDatabase DBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "AntiCheat.db") + ";Connection=shared");
        public static readonly ILiteCollection<Detection> Detections = DBFile.GetCollection<Detection>("Detections");

        static AntiCheatDB()
        {
            Detections.EnsureIndex(x => x.PlayerId);
            Detections.EnsureIndex(x => x.SteamId);
        }

        public static Detection Record(long playerId, string username, ulong steamId, string type,
            string reason, string modVersion, long? roomId, TimeSpan dedupWindow, out bool isNew)
        {
            var cutoff = DateTime.UtcNow - dedupWindow;
            var existing = Detections.FindOne(d =>
                d.PlayerId == playerId && d.Type == type && d.Reason == reason && d.LastSeen >= cutoff);

            if (existing != null)
            {
                existing.Count++;
                existing.LastSeen = DateTime.UtcNow;
                Detections.Update(existing);
                isNew = false;
                return existing;
            }

            var detection = new Detection
            {
                Id = ObjectId.NewObjectId(),
                PlayerId = playerId,
                Username = username,
                SteamId = steamId,
                Type = type,
                Reason = reason,
                ModVersion = modVersion,
                RoomId = roomId,
                DetectedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow,
                Count = 1
            };
            Detections.Insert(detection);
            isNew = true;
            return detection;
        }
    }
}
