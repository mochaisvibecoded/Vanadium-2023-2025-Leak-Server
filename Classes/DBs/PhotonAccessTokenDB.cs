using LiteDB;
using Vanadium.Classes.DBs.DBClasses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Vanadium.Classes.DBs.DBClasses.PhotonAccessTokenDBClasses;

namespace Vanadium.Classes.DBs
{
    public class PhotonAccessTokenDB
    {
        public static LiteDatabase PhotonDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "PhotonAccessToken.db") + ";Connection=shared");
        public static readonly ILiteCollection<SubroomPermissionSet> PermissionSets = PhotonDBFile.GetCollection<SubroomPermissionSet>("SubroomPermissions");

        public static void Setup()
        {
            PermissionSets.EnsureIndex(x => x.RoomId);
            PermissionSets.EnsureIndex(x => x.SubRoomId);
        }

        public static long GetNextId()
        {
            if (PermissionSets.Count() == 0) return 1;
            return PermissionSets.Max(x => x.Id) + 1;
        }

        public static SubroomPermissionSet? GetPermissions(long roomId, long subRoomId)
        {
            return PermissionSets.FindOne(x => x.RoomId == roomId && x.SubRoomId == subRoomId);
        }

        public static SubroomPermissionSet SetPermissions(long roomId, long subRoomId, List<StoredPermissionEntry> incomingPermissions)
        {
            var existing = PermissionSets.FindOne(x => x.RoomId == roomId && x.SubRoomId == subRoomId);

            if (existing == null)
            {
                existing = new SubroomPermissionSet
                {
                    Id = GetNextId(),
                    RoomId = roomId,
                    SubRoomId = subRoomId,
                    Permissions = new List<StoredPermissionEntry>(),
                    UpdatedAt = DateTime.UtcNow
                };
            }

            foreach (var incoming in incomingPermissions)
            {
                var match = existing.Permissions.FirstOrDefault(p =>
                    p.Permission == incoming.Permission && p.Role == incoming.Role && p.Type == incoming.Type);

                if (match == null)
                {
                    existing.Permissions.Add(new StoredPermissionEntry
                    {
                        Permission = incoming.Permission,
                        Role = incoming.Role,
                        Type = incoming.Type,
                        Override = incoming.Override,
                        Value = incoming.Value
                    });
                }
                else
                {
                    match.Override = incoming.Override;
                    match.Value = incoming.Value;
                }
            }

            existing.UpdatedAt = DateTime.UtcNow;

            if (existing.Id == 0)
                existing.Id = GetNextId();

            PermissionSets.Upsert(existing);
            return existing;
        }

        public static void DeletePermissions(long roomId, long subRoomId)
        {
            var existing = PermissionSets.FindOne(x => x.RoomId == roomId && x.SubRoomId == subRoomId);
            if (existing != null)
                PermissionSets.Delete(existing.Id);
        }
    }
}