using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class MatchDBClasses
    {
        public class InviteDTO
        {
            public long InviteId { get; set; }
            public string Name { get; set; }
            public int InviteMode { get; set; } = 0;
        }

        public class PlayerOnlineDTO
        {
            public bool SenderIsMyFavoriteFriend { get; set; }
        }
    }
}