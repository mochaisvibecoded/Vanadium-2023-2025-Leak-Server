using LiteDB;
using Vanadium.Classes;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.RoomCommentsDBClasses;

namespace Vanadium.Classes.DBs
{
    public class RoomCommentsDB
    {
        public static LiteDatabase CommentsDBFile =
            new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "RoomComments.db") + ";Connection=shared");

        private static ILiteCollection<RoomComment> Col =>
            CommentsDBFile.GetCollection<RoomComment>("RoomComments");

        private static long GetNextCommentId()
        {
            long id;
            do
            {
                id = Random.Shared.NextInt64(1, long.MaxValue);
            } while (Col.FindById(id) != null);
            return id;
        }

        public static RoomComment CreateComment(long roomId, long subRoomId, long accountId, string message, int style, float posX, float posY, float posZ)
        {
            var comment = new RoomComment
            {
                commentId = GetNextCommentId(),
                roomId = roomId,
                subRoomId = subRoomId,
                accountId = accountId,
                message = message,
                style = style,
                positionX = posX,
                positionY = posY,
                positionZ = posZ,
                createdAt = DateTime.UtcNow,
                unread = true
            };

            Col.Insert(comment);
            return comment;
        }

        public static List<RoomComment> GetComments(long roomId, int count, long minId)
        {
            var query = Col.Find(c => c.roomId == roomId);

            if (minId >= 0)
                query = query.Where(c => c.commentId > minId);

            return query
                .OrderByDescending(c => c.createdAt)
                .Take(count)
                .ToList();
        }

        public static RoomComment? GetComment(long commentId)
            => Col.FindById(commentId);

        public static bool MarkAsRead(long roomId, long commentId)
        {
            var comment = Col.FindOne(c => c.roomId == roomId && c.commentId == commentId);
            if (comment == null) return false;

            comment.unread = false;
            return Col.Update(comment);
        }

        public static bool DeleteComment(long commentId)
            => Col.Delete(commentId);

        public static bool DeleteCommentsByRoom(long roomId)
        {
            Col.DeleteMany(c => c.roomId == roomId);
            return true;
        }
    }
}