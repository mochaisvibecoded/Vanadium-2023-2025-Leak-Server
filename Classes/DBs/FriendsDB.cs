using LiteDB;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.FriendsDBClasses;

namespace Vanadium.Classes.DBs
{
    public class FriendsDB
    {
        public static LiteDatabase FriendsDBFile =
            new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Friends.db") + ";Connection=shared");

        private static ILiteCollection<FriendEntry> Col =>
            FriendsDBFile.GetCollection<FriendEntry>("Friends");

        private static FriendEntry? FindEntry(long playerId, long otherPlayerId)
        {
            return Col.FindOne(x =>
                x.PlayerID == playerId &&
                x.Relationship.OtherPlayerID == otherPlayerId);
        }

        private static FriendEntry GetOrCreate(long playerId, long otherPlayerId)
        {
            var entry = FindEntry(playerId, otherPlayerId);
            if (entry != null)
                return entry;

            entry = new FriendEntry
            {
                Id = ObjectId.NewObjectId(),
                PlayerID = playerId,
                Relationship = new RelationshipDetail
                {
                    Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    PlayerID = playerId,
                    OtherPlayerID = otherPlayerId,
                    RelationshipType = RelationshipType.None,
                    Favorited = ReciprocalStatus.None,
                    Ignored = ReciprocalStatus.None,
                    Muted = ReciprocalStatus.None
                }
            };

            Col.Insert(entry);
            return entry;
        }

        private static void Update(FriendEntry entry)
        {
            Col.Update(entry);
        }

        public static List<RelationshipDetail> GetRelationships(long playerId)
        {
            return Col.Find(x => x.PlayerID == playerId)
                      .Select(x => x.Relationship)
                      .ToList();
        }

        public static RelationshipDetail SendFriendRequest(long fromPlayer, long toPlayer)
        {
            if (fromPlayer == toPlayer)
                return new RelationshipDetail { PlayerID = fromPlayer, OtherPlayerID = toPlayer };

            var my = GetOrCreate(fromPlayer, toPlayer);
            var their = GetOrCreate(toPlayer, fromPlayer);

            if (my.Relationship.RelationshipType == RelationshipType.Friend)
                return my.Relationship;

            if (their.Relationship.RelationshipType == RelationshipType.Sent ||
                my.Relationship.RelationshipType == RelationshipType.Received)
            {
                my.Relationship.RelationshipType = RelationshipType.Friend;
                their.Relationship.RelationshipType = RelationshipType.Friend;

                Update(my);
                Update(their);

                return my.Relationship;
            }

            my.Relationship.RelationshipType = RelationshipType.Sent;
            their.Relationship.RelationshipType = RelationshipType.Received;

            Update(my);
            Update(their);

            return my.Relationship;
        }

        public static RelationshipDetail? AcceptFriendRequest(long playerId, long otherPlayerId)
        {
            var my = GetOrCreate(playerId, otherPlayerId);
            var their = GetOrCreate(otherPlayerId, playerId);

            if (their.Relationship.RelationshipType != RelationshipType.Sent ||
                my.Relationship.RelationshipType != RelationshipType.Received)
                return null;

            my.Relationship.RelationshipType = RelationshipType.Friend;
            their.Relationship.RelationshipType = RelationshipType.Friend;

            Update(my);
            Update(their);

            return my.Relationship;
        }

        public static RelationshipDetail RemoveFriend(long playerA, long playerB)
        {
            var a = GetOrCreate(playerA, playerB);
            var b = GetOrCreate(playerB, playerA);

            a.Relationship.RelationshipType = RelationshipType.None;
            b.Relationship.RelationshipType = RelationshipType.None;

            Update(a);
            Update(b);

            return a.Relationship;
        }

        public static RelationshipDetail UpdateMute(long ownerId, long targetId, ReciprocalStatus status)
        {
            var my = GetOrCreate(ownerId, targetId);
            var their = GetOrCreate(targetId, ownerId);

            my.Relationship.Muted = status;

            if (status == ReciprocalStatus.None)
            {
                if (their.Relationship.Muted == ReciprocalStatus.Mutual)
                    their.Relationship.Muted = ReciprocalStatus.Local;
                else if (their.Relationship.Muted == ReciprocalStatus.Remote)
                    their.Relationship.Muted = ReciprocalStatus.None;
            }
            else
            {
                if (their.Relationship.Muted == ReciprocalStatus.Local || their.Relationship.Muted == ReciprocalStatus.Mutual)
                    their.Relationship.Muted = ReciprocalStatus.Mutual;
                else
                    their.Relationship.Muted = ReciprocalStatus.Remote;
            }

            Update(my);
            Update(their);

            return my.Relationship;
        }

        public static RelationshipDetail UpdateIgnore(long ownerId, long targetId, ReciprocalStatus status)
        {
            var my = GetOrCreate(ownerId, targetId);
            var their = GetOrCreate(targetId, ownerId);

            my.Relationship.Ignored = status;

            if (status == ReciprocalStatus.None)
            {
                if (their.Relationship.Ignored == ReciprocalStatus.Mutual)
                    their.Relationship.Ignored = ReciprocalStatus.Local;
                else if (their.Relationship.Ignored == ReciprocalStatus.Remote)
                    their.Relationship.Ignored = ReciprocalStatus.None;
            }
            else
            {
                if (their.Relationship.Ignored == ReciprocalStatus.Local || their.Relationship.Ignored == ReciprocalStatus.Mutual)
                    their.Relationship.Ignored = ReciprocalStatus.Mutual;
                else
                    their.Relationship.Ignored = ReciprocalStatus.Remote;
            }

            Update(my);
            Update(their);

            return my.Relationship;
        }

        public static RelationshipDetail SetFavorite(long ownerId, long targetId, ReciprocalStatus status)
        {
            var rel = GetOrCreate(ownerId, targetId);

            rel.Relationship.Favorited = status;

            Update(rel);

            return rel.Relationship;
        }

        public static void DeleteFriends(long playerId)
        {
            if (playerId == 0)
                return;

            Col.DeleteMany(x => x.PlayerID == playerId);

            foreach (var rel in Col.Find(x => x.Relationship.OtherPlayerID == playerId))
            {
                rel.Relationship.RelationshipType = RelationshipType.None;
                rel.Relationship.Muted = ReciprocalStatus.None;
                rel.Relationship.Ignored = ReciprocalStatus.None;
                rel.Relationship.Favorited = ReciprocalStatus.None;

                Update(rel);
            }
        }
    }
}