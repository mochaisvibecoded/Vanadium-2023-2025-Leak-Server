using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class LeaderboardDBClasses
    {
        public class LeaderboardEntry
        {
            [BsonId]
            public long EntryId { get; set; }
            public long PlayerId { get; set; }
            public long RoomId { get; set; }
            public int StatChannel { get; set; }
            public int Score { get; set; }
            public int FilterType { get; set; }
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public class LeaderboardRow
        {
            public long playerId { get; set; }
            public int rank { get; set; }
            public int score { get; set; }
        }

        public class GetRanksRequest
        {
            public int FilterType { get; set; }
            public long PlayerId { get; set; }
            public int RankEnd { get; set; }
            public int RankStart { get; set; }
            public long RoomId { get; set; }
            public bool SortAscending { get; set; }
            public int StatChannel { get; set; }
            public int Timeframe { get; set; }
        }

        public class GetRanksResponse
        {
            public List<LeaderboardRow> rows { get; set; } = new();
        }

        public class GetNearbyScoresRequest : GetRanksRequest
        {
            public int WindowSize { get; set; }
        }

        public class GetNearbyScoresResponse
        {
            public List<LeaderboardRow> rows { get; set; } = new();
        }

        public class CheckAndSetStatRequest
        {
            public int CurrentStatValue { get; set; }
            public long RoomId { get; set; }
            public int StatChannel { get; set; }
            public int StatValue { get; set; }
        }

        public class CheckAndSetStatResponse
        {
            public string? error { get; set; }
            public bool success { get; set; }
            public LeaderboardRow? value { get; set; }
        }
    }
}