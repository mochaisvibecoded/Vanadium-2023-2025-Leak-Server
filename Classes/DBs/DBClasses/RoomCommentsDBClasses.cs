using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class RoomCommentsDBClasses
    {
        public class RoomComment
        {
            [BsonId]
            public long commentId { get; set; }
            public long subRoomId { get; set; }
            public long roomId { get; set; }
            public long accountId { get; set; }
            public DateTime createdAt { get; set; } = DateTime.UtcNow;
            public float positionX { get; set; }
            public float positionY { get; set; }
            public float positionZ { get; set; }
            public string message { get; set; } = "";
            public int style { get; set; } = 0;
            public bool unread { get; set; } = true;
        }

        public class CreateCommentRequest
        {
            public string message { get; set; } = "";
            public long subRoomId { get; set; }
            public int style { get; set; } = 0;
            public float positionX { get; set; }
            public float positionY { get; set; }
            public float positionZ { get; set; }
        }
    }
}