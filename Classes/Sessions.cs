using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.InstanceDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using static Vanadium.Classes.DBs.DBClasses.RoomDBClasses;
using static Vanadium.Classes.ServerConfig;

namespace Vanadium.Classes
{
    public class Sessions // yo
    {
        private static readonly Random _rng = new();
        private static readonly object _lock = new();

        private static RoomInstance BuildInstance(string roomName, string dataBlob, SubRooms sub, long roomId, bool isPrivate)
        {
            long id = _rng.NextInt64(100_000_000_000L, long.MaxValue);
            var ri = new RoomInstance
            {
                Name = $"^{roomName}",
                dataBlob = dataBlob,
                isPrivate = isPrivate,
                location = sub.UnitySceneId,
                maxCapacity = sub.MaxPlayers,
                photonRegion = "us",
                photonRegionId = "us",
                photonRoomId = $"{roomName}-{id}-room",
                roomId = roomId,
                roomInstanceId = id,
                subRoomId = sub.SubRoomId
            };
            InstanceDB.Upsert(ri, isPrivate);
            return ri;
        }

        private static SubRooms? ResolveSubRoom(Room roomData, long? subRoomId)
        {
            if (subRoomId.HasValue)
                return roomData.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId.Value);

            var publicSubs = roomData.SubRooms?
                .Where(s => s.Accessibility == RoomAccessibility.Public)
                .ToList();

            if (publicSubs?.Count > 0)
                return publicSubs[Random.Shared.Next(publicSubs.Count)];

            return roomData.SubRooms?.FirstOrDefault();
        }

        private static void AutoFollowPlayers(long playerId, RoomInstance instance)
        {
            foreach (var other in PlayerDB.Players.FindAll()
                .Where(p => p.PlayerId != playerId &&
                            p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == instance.roomInstanceId))
                PlayerDB.UpdatePlayerHeartbeat(other.PlayerId, instance);
        }

        private static Heartbeat? ApplyPlayerId(long playerId, Heartbeat? hb)
        {
            if (hb != null) hb.playerId = playerId;
            return hb;
        }

        private static HashSet<long> GetFriendIds(long playerId)
            => FriendsDB.GetRelationships(playerId)
                .Where(r => r.RelationshipType == FriendsDBClasses.RelationshipType.Friend)
                .Select(r => r.OtherPlayerID)
                .ToHashSet();

        private static void Prune(long roomId)
        {
            foreach (var entry in InstanceDB.GetAllForRoom(roomId))
            {
                if (InstanceDB.CountPlayers(entry.RoomInstanceId) == 0)
                    InstanceDB.Delete(entry.RoomInstanceId);
            }
        }

        private static RoomInstance? FindBestPublicInstance(long playerId, long roomId, long? subRoomId, long? excludeInstanceId = null)
        {
            var friendIds = GetFriendIds(playerId);

            var allPlayers = PlayerDB.Players.FindAll()
                .Where(p =>
                    p.Player?.PlayerExtra?.Heartbeat?.isOnline == true &&
                    p.Player.PlayerExtra.Heartbeat.roomInstance != null &&
                    p.Player.PlayerExtra.Heartbeat.roomInstance.roomId == roomId &&
                    !p.Player.PlayerExtra.Heartbeat.roomInstance.isPrivate &&
                    !p.Player.PlayerExtra.Heartbeat.roomInstance.isFull &&
                    (excludeInstanceId == null || p.Player.PlayerExtra.Heartbeat.roomInstance.roomInstanceId != excludeInstanceId))
                .ToList();

            var candidates = allPlayers
                .Select(p => p.Player!.PlayerExtra!.Heartbeat!.roomInstance!)
                .Where(ri => !subRoomId.HasValue || ri.subRoomId == subRoomId.Value)
                .GroupBy(ri => ri.roomInstanceId)
                .Select(g => g.First())
                .Where(ri => InstanceDB.CountPlayers(ri.roomInstanceId) < ri.maxCapacity)
                .ToList();

            var friendInstance = candidates
                .Where(ri => allPlayers.Any(p =>
                    friendIds.Contains(p.PlayerId) &&
                    p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == ri.roomInstanceId))
                .OrderByDescending(ri => InstanceDB.CountPlayers(ri.roomInstanceId))
                .FirstOrDefault();

            return friendInstance ?? candidates
                .OrderByDescending(ri => InstanceDB.CountPlayers(ri.roomInstanceId))
                .FirstOrDefault();
        }

        public static void MarkInstanceInactive(long roomInstanceId)
        {
            lock (_lock)
                InstanceDB.Delete(roomInstanceId);
        }

        public static async Task MarkInstancePrivate(long roomInstanceId)
        {
            lock (_lock)
                InstanceDB.MarkPrivate(roomInstanceId);

            foreach (var p in PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == roomInstanceId))
            {
                var hb = p.Player!.PlayerExtra!.Heartbeat!;
                hb.playerId = p.PlayerId;
                hb.roomInstance!.isPrivate = true;
                await Controllers.NotificationsController.RefreshHeartbeat(p.PlayerId, hb);
            }
        }

        public static void FakeInstanceFull(long roomInstanceId)
        {
            lock (_lock)
                InstanceDB.MarkFull(roomInstanceId, true);
        }

        public static async Task<(int instancesRefreshed, int playersMoved)> RefreshPublicInstances(long roomId, long subRoomId)
        {
            var roomData = RoomDB.GetRoom(roomId);
            if (roomData == null) return (0, 0);

            var sub = roomData.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
            if (sub == null) return (0, 0);

            var moved = new List<(long playerId, Heartbeat hb)>();
            int refreshed = 0;

            lock (_lock)
            {
                var occupants = PlayerDB.Players.FindAll()
                    .Where(p =>
                        p.Player?.PlayerExtra?.Heartbeat?.isOnline == true &&
                        p.Player.PlayerExtra.Heartbeat.roomInstance != null &&
                        p.Player.PlayerExtra.Heartbeat.roomInstance.roomId == roomId &&
                        p.Player.PlayerExtra.Heartbeat.roomInstance.subRoomId == subRoomId &&
                        !p.Player.PlayerExtra.Heartbeat.roomInstance.isPrivate)
                    .ToList();

                foreach (var group in occupants.GroupBy(p => p.Player!.PlayerExtra!.Heartbeat!.roomInstance!.roomInstanceId))
                {
                    var fresh = BuildInstance(roomData.Name ?? "UnknownRoom", sub.DataBlob ?? "", sub, roomId, isPrivate: false);

                    foreach (var p in group)
                    {
                        var hb = PlayerDB.UpdatePlayerHeartbeat(p.PlayerId, fresh);
                        if (hb == null) continue;
                        hb.playerId = p.PlayerId;
                        moved.Add((p.PlayerId, hb));
                    }

                    InstanceDB.Delete(group.Key);
                    refreshed++;
                }

                Prune(roomId);
            }

            foreach (var (playerId, hb) in moved)
                await Controllers.NotificationsController.RefreshHeartbeat(playerId, hb);

            return (refreshed, moved.Count);
        }

        public static async Task<int> SetInstancesPrivacy(long roomId, long subRoomId, bool isPrivate)
        {
            var updated = new List<(long playerId, Heartbeat hb)>();
            HashSet<long> instanceIds;

            lock (_lock)
            {
                var occupants = PlayerDB.Players.FindAll()
                    .Where(p =>
                        p.Player?.PlayerExtra?.Heartbeat?.isOnline == true &&
                        p.Player.PlayerExtra.Heartbeat.roomInstance != null &&
                        p.Player.PlayerExtra.Heartbeat.roomInstance.roomId == roomId &&
                        p.Player.PlayerExtra.Heartbeat.roomInstance.subRoomId == subRoomId &&
                        p.Player.PlayerExtra.Heartbeat.roomInstance.isPrivate != isPrivate)
                    .ToList();

                instanceIds = occupants
                    .Select(p => p.Player!.PlayerExtra!.Heartbeat!.roomInstance!.roomInstanceId)
                    .ToHashSet();

                foreach (var instanceId in instanceIds)
                {
                    if (isPrivate) InstanceDB.MarkPrivate(instanceId);
                    else InstanceDB.MarkPublic(instanceId);
                }

                foreach (var p in occupants)
                {
                    var hb = p.Player!.PlayerExtra!.Heartbeat!;
                    hb.playerId = p.PlayerId;
                    hb.roomInstance!.isPrivate = isPrivate;
                    updated.Add((p.PlayerId, hb));
                }
            }

            foreach (var (playerId, hb) in updated)
                await Controllers.NotificationsController.RefreshHeartbeat(playerId, hb);

            return instanceIds.Count;
        }

        public static bool IsInstanceActuallyFull(long roomInstanceId)
            => InstanceDB.IsFull(roomInstanceId);

        public static int CountPlayersInInstance(long roomInstanceId)
            => InstanceDB.CountPlayers(roomInstanceId);

        public static Heartbeat? CreateRoom(
            long playerId,
            long roomId,
            long? subRoomId = null,
            string? dataBlobOverride = null,
            int joinMode = 0,
            bool additionalPlayersAutoFollow = false,
            string roomNameOverride = "") // todo remove ^
        {
            var roomData = RoomDB.GetRoom(roomId);
            if (roomData == null) return null;

            bool isPrivateInstance = joinMode == 2;
            bool isPrivateRoom = roomData.Accessibility == RoomAccessibility.Private;

            lock (_lock)
            {
                Prune(roomId);

                if (isPrivateInstance)
                {
                    var sub = ResolveSubRoom(roomData, subRoomId);
                    if (sub == null) return null;
                    var solo = BuildInstance(roomData.Name ?? "UnknownRoom", dataBlobOverride ?? sub.DataBlob ?? "", sub, roomId, isPrivate: true);
                    return ApplyPlayerId(playerId, PlayerDB.UpdatePlayerHeartbeat(playerId, solo));
                }

                if (isPrivateRoom)
                {
                    var sub = ResolveSubRoom(roomData, subRoomId);
                    if (sub == null) return null;

                    var existing = InstanceDB.GetActivePrivate(roomId, sub.SubRoomId);
                    if (existing != null)
                    {
                        int count = InstanceDB.CountPlayers(existing.RoomInstanceId);
                        if (!existing.IsFull && count < existing.MaxCapacity)
                        {
                            var ri = existing.ToRoomInstance();
                            if (additionalPlayersAutoFollow) AutoFollowPlayers(playerId, ri);
                            return ApplyPlayerId(playerId, PlayerDB.UpdatePlayerHeartbeat(playerId, ri));
                        }
                    }

                    var liveInstance = PlayerDB.Players.FindAll()
                        .Where(p =>
                            p.Player?.PlayerExtra?.Heartbeat?.isOnline == true &&
                            p.Player.PlayerExtra.Heartbeat.roomInstance?.roomId == roomId &&
                            p.Player.PlayerExtra.Heartbeat.roomInstance.subRoomId == sub.SubRoomId)
                        .Select(p => p.Player!.PlayerExtra!.Heartbeat!.roomInstance!)
                        .GroupBy(ri => ri.roomInstanceId)
                        .Select(g => g.First())
                        .Where(ri => !ri.isFull && InstanceDB.CountPlayers(ri.roomInstanceId) < ri.maxCapacity)
                        .FirstOrDefault();

                    if (liveInstance != null)
                    {
                        InstanceDB.Upsert(liveInstance, isPrivate: true);
                        if (additionalPlayersAutoFollow) AutoFollowPlayers(playerId, liveInstance);
                        return ApplyPlayerId(playerId, PlayerDB.UpdatePlayerHeartbeat(playerId, liveInstance));
                    }

                    var newInstance = BuildInstance(roomData.Name ?? "UnknownRoom", dataBlobOverride ?? sub.DataBlob ?? "", sub, roomId, isPrivate: true);
                    return ApplyPlayerId(playerId, PlayerDB.UpdatePlayerHeartbeat(playerId, newInstance));
                }

                if (joinMode == 1)
                {
                    var currentHb = PlayerDB.GetPlayerHeartbeat(playerId);
                    long? currentInstanceId = currentHb?.roomInstance?.roomInstanceId;

                    var different = FindBestPublicInstance(playerId, roomId, subRoomId, excludeInstanceId: currentInstanceId);
                    if (different != null)
                    {
                        if (additionalPlayersAutoFollow) AutoFollowPlayers(playerId, different);
                        return ApplyPlayerId(playerId, PlayerDB.UpdatePlayerHeartbeat(playerId, different));
                    }

                    var sub = ResolveSubRoom(roomData, subRoomId);
                    if (sub == null) return null;
                    var fresh = BuildInstance(roomData.Name ?? "UnknownRoom", dataBlobOverride ?? sub.DataBlob ?? "", sub, roomId, isPrivate: false);
                    return ApplyPlayerId(playerId, PlayerDB.UpdatePlayerHeartbeat(playerId, fresh));
                }

                var best = FindBestPublicInstance(playerId, roomId, subRoomId);
                if (best != null)
                {
                    if (additionalPlayersAutoFollow) AutoFollowPlayers(playerId, best);
                    return ApplyPlayerId(playerId, PlayerDB.UpdatePlayerHeartbeat(playerId, best));
                }

                var resolvedSub = ResolveSubRoom(roomData, subRoomId);
                if (resolvedSub == null) return null;
                var created = BuildInstance(roomData.Name ?? "UnknownRoom", dataBlobOverride ?? resolvedSub.DataBlob ?? "", resolvedSub, roomId, isPrivate: false);
                return ApplyPlayerId(playerId, PlayerDB.UpdatePlayerHeartbeat(playerId, created));
            }
        }

        public static Heartbeat CreateDorm(long playerId, string username, long dormRoomId)
        {
            var dorm = RoomDB.GetRoom(dormRoomId);
            var firstSub = dorm?.SubRooms?.FirstOrDefault();
            long subRoomId = firstSub?.SubRoomId ?? 0;

            lock (_lock)
            {
                Prune(dormRoomId);

                var existing = InstanceDB.GetActivePrivate(dormRoomId, subRoomId);
                if (existing != null)
                {
                    int count = InstanceDB.CountPlayers(existing.RoomInstanceId);
                    if (!existing.IsFull && count < existing.MaxCapacity)
                    {
                        var existingRi = existing.ToRoomInstance();
                        var existingHb = PlayerDB.UpdatePlayerHeartbeat(playerId, existingRi);
                        existingHb.playerId = playerId;
                        return existingHb;
                    }
                }

                long instanceId = _rng.NextInt64(100_000_000_000L, long.MaxValue);
                var session = new RoomInstance
                {
                    isPrivate = true,
                    location = "76d98498-60a1-430c-ab76-b54a29b7a163",
                    maxCapacity = 4,
                    Name = $"@{username}'s Dorm",
                    photonRegion = "us",
                    photonRegionId = "us",
                    photonRoomId = $"DormRoom-{instanceId}-room",
                    roomId = dormRoomId,
                    roomInstanceId = instanceId,
                    subRoomId = subRoomId,
                    dataBlob = firstSub?.DataBlob ?? ""
                };

                InstanceDB.Upsert(session, isPrivate: true);
                var hb = PlayerDB.UpdatePlayerHeartbeat(playerId, session);
                hb.playerId = playerId;
                return hb;
            }
        }
    }
}