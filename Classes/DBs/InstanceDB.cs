using LiteDB;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.InstanceDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Classes.DBs
{
    public class InstanceDB
    {
        public static readonly LiteDatabase InstanceDBFile =
            new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Instances.db") + ";Connection=shared");

        private static readonly ILiteCollection<InstanceEntry> Col =
            InstanceDBFile.GetCollection<InstanceEntry>("Instances");

        static InstanceDB()
        {
            Col.EnsureIndex(x => x.RoomId);
            Col.EnsureIndex(x => x.SubRoomId);
            Col.EnsureIndex(x => x.IsDeleted);
            Col.EnsureIndex(x => x.IsPrivate);
        }

        // DO NOT TOUCH
        
        private static readonly string[] RealtimeAppIds = new[] // i made these on my own photon dashboard
        {
            "616d6ba8-edb1-47f8-a0de-31c8ce426685",
            "a7c732cd-5cea-4839-b0b3-ba8e9af14b06",
            "90d94f73-9797-409d-84ab-ed3992177612",
            "fac97be5-4239-4309-af46-c35bc163fe5d",
            "550c2912-37e3-4d4c-ae3f-a5f417fcba40",
            "70229ffe-5049-4814-9b40-fb86ee99387e",
            "683dc19a-277b-4cc8-81b2-a7ffdfb3a5cc",
            "530184ee-bd74-4bc0-b8e9-a21c1504f0a2",
            "efa6fb5c-94ef-4d73-8fac-7fb8e1e66d19",
			"53a7e346-0fe9-4320-b8d1-791491c5b220"
        };

        private static readonly string[] VoiceAppIds = new[] // i made these on my own photon dashboard
        {
            "b3a7f09a-b167-4ba1-87bb-2603da5a39be",
            "7e71f40c-d1fa-4e48-8851-981402e630e5",
            "3e010413-c8c8-48df-95cf-bf7f2bf430e6",
            "706df054-e107-4c35-94eb-f374bf607b45",
            "28846001-24c9-4d2a-9d60-44a3cf5af73f",
            "341b39d4-5c8f-47a4-a22a-dd5e3b9e3dbc",
            "9e24d9e9-34af-49c4-b490-d22f67913e6d",
            "f05846da-20a5-449b-a32d-c8a67071dc41",
            "e24a71b3-cefd-4f51-8e09-c26f9f173e17",
            "2ee14608-23c2-4d51-85be-baa1c1bc1c54"
        };

        private static readonly Random _rng = new Random();

        private static string PickLeastUsed(string[] pool, Func<string, int> countUsage)
        {
            var counts = pool.Select(id => (id, count: countUsage(id))).ToList();
            int min = counts.Min(x => x.count);
            var candidates = counts.Where(x => x.count == min).Select(x => x.id).ToList();
            return candidates[_rng.Next(candidates.Count)];
        }

        public static (string realtimeAppId, string voiceAppId) AssignPhotonIds(long roomInstanceId)
        {
            var existing = Get(roomInstanceId);
            if (existing != null && !string.IsNullOrEmpty(existing.PhotonRealtimeAppId) && !string.IsNullOrEmpty(existing.PhotonVoiceAppId))
                return (existing.PhotonRealtimeAppId, existing.PhotonVoiceAppId);

            var allActive = Col.Find(x => !x.IsDeleted).ToList();

            string realtime = PickLeastUsed(RealtimeAppIds, id =>
                allActive.Count(e => e.PhotonRealtimeAppId == id));

            string voice = PickLeastUsed(VoiceAppIds, id =>
                allActive.Count(e => e.PhotonVoiceAppId == id));

            if (existing != null)
            {
                existing.PhotonRealtimeAppId = realtime;
                existing.PhotonVoiceAppId = voice;
                Col.Update(existing);
            }

            return (realtime, voice);
        }

        public static InstanceEntry Upsert(RoomInstance ri, bool isPrivate)
        {
            bool isNew = Col.FindById(ri.roomInstanceId) == null;
            var entry = Col.FindById(ri.roomInstanceId) ?? new InstanceEntry { RoomInstanceId = ri.roomInstanceId };
            entry.RoomId = ri.roomId;
            entry.SubRoomId = ri.subRoomId;
            entry.IsPrivate = isPrivate;
            entry.IsFull = ri.isFull;
            entry.MaxCapacity = ri.maxCapacity;
            entry.Name = ri.Name;
            entry.Location = ri.location;
            entry.PhotonRoomId = ri.photonRoomId;
            entry.PhotonRegion = ri.photonRegion ?? "us";
            entry.PhotonRegionId = ri.photonRegionId ?? "us";
            entry.DataBlob = ri.dataBlob ?? "";
            entry.IsDeleted = false;

            if (isNew || string.IsNullOrEmpty(entry.PhotonRealtimeAppId) || string.IsNullOrEmpty(entry.PhotonVoiceAppId))
            {
                var allActive = Col.Find(x => !x.IsDeleted && x.RoomInstanceId != ri.roomInstanceId).ToList();

                entry.PhotonRealtimeAppId = PickLeastUsed(RealtimeAppIds, id =>
                    allActive.Count(e => e.PhotonRealtimeAppId == id));

                entry.PhotonVoiceAppId = PickLeastUsed(VoiceAppIds, id =>
                    allActive.Count(e => e.PhotonVoiceAppId == id));
            }

            Col.Upsert(entry);
            return entry;
        }

        public static InstanceEntry? Get(long roomInstanceId)
            => Col.FindOne(x => x.RoomInstanceId == roomInstanceId && !x.IsDeleted);

        public static List<InstanceEntry> GetAllForRoom(long roomId, bool? isPrivate = null)
        {
            return Col.Find(x =>
                x.RoomId == roomId &&
                !x.IsDeleted &&
                (isPrivate == null || x.IsPrivate == isPrivate))
                .ToList();
        }

        public static InstanceEntry? GetActivePrivate(long roomId, long subRoomId)
            => Col.FindOne(x =>
                x.RoomId == roomId &&
                x.SubRoomId == subRoomId &&
                x.IsPrivate &&
                !x.IsDeleted);

        public static void MarkFull(long roomInstanceId, bool isFull)
        {
            var entry = Col.FindById(roomInstanceId);
            if (entry == null) return;
            entry.IsFull = isFull;
            Col.Update(entry);
        }

        public static void MarkPrivate(long roomInstanceId)
        {
            var entry = Col.FindById(roomInstanceId);
            if (entry == null) return;
            entry.IsPrivate = true;
            Col.Update(entry);
        }

        public static void MarkPublic(long roomInstanceId)
        {
            var entry = Col.FindById(roomInstanceId);
            if (entry == null) return;
            entry.IsPrivate = false;
            Col.Update(entry);
        }

        public static void Delete(long roomInstanceId)
        {
            var entry = Col.FindById(roomInstanceId);
            if (entry == null) return;
            entry.IsDeleted = true;
            Col.Update(entry);
        }

        public static void PruneEmpty()
        {
            var all = Col.Find(x => !x.IsDeleted).ToList();
            foreach (var entry in all)
            {
                int count = PlayerDB.Players
                    .FindAll()
                    .Count(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == entry.RoomInstanceId);

                if (count == 0)
                {
                    entry.IsDeleted = true;
                    Col.Update(entry);
                }
            }
        }

        public static int CountPlayers(long roomInstanceId)
            => PlayerDB.Players
                .FindAll()
                .Count(p => p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.roomInstanceId == roomInstanceId);

        public static bool IsFull(long roomInstanceId)
        {
            var entry = Get(roomInstanceId);
            if (entry == null) return false;
            return entry.IsFull || CountPlayers(roomInstanceId) >= entry.MaxCapacity;
        }
    }
}