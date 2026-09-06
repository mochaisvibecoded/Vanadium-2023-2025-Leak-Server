using LiteDB;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class InstanceDBClasses
    {
        public class InstanceEntry
        {
            [BsonId]
            public long RoomInstanceId { get; set; }
            public long RoomId { get; set; }
            public long SubRoomId { get; set; }
            public bool IsPrivate { get; set; }
            public bool IsFull { get; set; }
            public bool IsDeleted { get; set; } = false;
            public int MaxCapacity { get; set; }
            public string Name { get; set; } = "";
            public string Location { get; set; } = "";
            public string PhotonRoomId { get; set; } = "";
            public string PhotonRegion { get; set; } = "us";
            public string PhotonRegionId { get; set; } = "us";
            public string DataBlob { get; set; } = "";
            public string PhotonRealtimeAppId { get; set; } = "";
            public string PhotonVoiceAppId { get; set; } = "";
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            public RoomInstance ToRoomInstance() => new RoomInstance
            {
                roomInstanceId = RoomInstanceId,
                roomId = RoomId,
                subRoomId = SubRoomId,
                isPrivate = IsPrivate,
                isFull = IsFull,
                maxCapacity = MaxCapacity,
                Name = Name,
                location = Location,
                photonRoomId = PhotonRoomId,
                photonRegion = PhotonRegion,
                photonRegionId = PhotonRegionId,
                dataBlob = DataBlob
            };
        }
    }
}