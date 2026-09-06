using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class FriendsDBClasses
    {
        public class FriendEntry
        {
            [BsonId]
            public ObjectId? Id { get; set; }
            public long PlayerID { get; set; }
            public RelationshipDetail Relationship { get; set; } = new RelationshipDetail();
        }

        public class RelationshipDetail
        {
            public long Id { get; set; }
            public long PlayerID { get; set; }
            public long OtherPlayerID { get; set; }
            public RelationshipType RelationshipType { get; set; }
            public ReciprocalStatus Muted { get; set; }
            public ReciprocalStatus Ignored { get; set; }
            public ReciprocalStatus Favorited { get; set; }
            public int VoiceVolume { get; set; } = 100;
        }

        public enum RelationshipType
        {
            None,
            Sent,
            Received,
            Friend,
        }

        [Flags]
        public enum FriendInfo
        {
            None = 0,
            Favorited = 1,
            Muted = 2,
            Ignored = 4,
            Blocked = 5
        }

        public enum ReciprocalStatus
        {
            None,
            Local,
            Remote,
            Mutual
        }
    }
}