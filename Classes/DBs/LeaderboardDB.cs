using LiteDB;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.LeaderboardDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using Vanadium.Controllers;
using Vanadium.Utils.NotiController;

namespace Vanadium.Classes.DBs
{
    public class LeaderboardDB
    {
        public static LiteDatabase LeaderboardDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Leaderboard.db") + ";Connection=shared");
        public static readonly ILiteCollection<LeaderboardEntry> Entries = LeaderboardDBFile.GetCollection<LeaderboardEntry>("Entries");

        public static void Setup()
        {
            Entries.EnsureIndex(x => x.PlayerId);
            Entries.EnsureIndex(x => x.RoomId);
            Entries.EnsureIndex(x => x.StatChannel);
            Entries.EnsureIndex(x => x.FilterType);
        }

        private static long GetNextEntryId()
        {
            if (Entries.Count() == 0) return 1;
            return Entries.Max(x => x.EntryId) + 1;
        }

        private static int ComputeRank(long roomId, int statChannel, long playerId, int score)
        {
            return Entries.Count(x =>
                x.RoomId == roomId &&
                x.StatChannel == statChannel &&
                (x.Score > score || (x.Score == score && x.PlayerId < playerId)));
        }

        private static bool IsRRORoom(long roomId)
        {
            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null) return false;

            if (room.IsRRO) return true;

            return room.Tags != null && room.Tags.Any(t =>
                t.Tag.Equals("rro", StringComparison.OrdinalIgnoreCase) ||
                t.Tag.Equals("recroomoriginal", StringComparison.OrdinalIgnoreCase));
        }

        public static int ClearRoomLeaderboard(long roomId)
        {
            return Entries.DeleteMany(x => x.RoomId == roomId);
        }

        private static async Task BanPlayerForLeaderboardAbuse(long playerId, int type = 0)
        {
            var player = PlayerDB.Players.FindById(playerId);
            if (player == null) return;
            var msg = "Cheating";

            var mbd = new ModerationBlockDetails
            {
                IsBan = true,
                ReportCategory = ReportCategory.Moderator,
                Duration = 2147483647,
                Message = msg,
                ModerationSetUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            player.Player ??= new Player();
            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.ModerationBlockDetails = mbd;
            player.Player.PlayerExtra.Heartbeat = new Heartbeat
            {
                playerId = playerId,
                isOnline = false,
                roomInstance = null,
                errorCode = 0
            };

            PlayerDB.Players.Update(player);

            await NotiController.SendPresenceUpdate(playerId, player.Player.PlayerExtra.Heartbeat);
            await NotiController.SendAccountUpdate(playerId, PlayerDB.GetAccountMe(playerId));
            await NotificationsController.SendBanned(playerId, mbd);
        }

        public static GetRanksResponse GetRanks(GetRanksRequest request)
        {
            int rankStart = request.RankStart < 0 ? 0 : request.RankStart;
            int limit = request.RankEnd - rankStart + 1;

            if (limit <= 0)
                return new GetRanksResponse { rows = new List<LeaderboardRow>() };

            var entries = Entries
                .Find(x => x.RoomId == request.RoomId && x.StatChannel == request.StatChannel)
                .OrderBy(x => request.SortAscending ? x.Score : -x.Score)
                .ThenBy(x => x.PlayerId)
                .Skip(rankStart)
                .Take(limit)
                .ToList();

            var rows = entries.Select((e, i) => new LeaderboardRow
            {
                playerId = e.PlayerId,
                rank = rankStart + i,
                score = e.Score
            }).ToList();

            return new GetRanksResponse { rows = rows };
        }

        public static LeaderboardRow GetPlayerRank(GetRanksRequest request)
        {
            var entry = Entries.FindOne(x =>
                x.PlayerId == request.PlayerId &&
                x.RoomId == request.RoomId &&
                x.StatChannel == request.StatChannel);

            if (entry != null)
            {
                return new LeaderboardRow
                {
                    playerId = request.PlayerId,
                    rank = ComputeRank(request.RoomId, request.StatChannel, request.PlayerId, entry.Score),
                    score = entry.Score
                };
            }

            int total = Entries.Count(x => x.RoomId == request.RoomId && x.StatChannel == request.StatChannel);

            return new LeaderboardRow
            {
                playerId = request.PlayerId,
                rank = total,
                score = 0
            };
        }

        public static GetNearbyScoresResponse GetNearbyScores(GetNearbyScoresRequest request)
        {
            int windowSize = request.WindowSize <= 0 ? 10 : request.WindowSize;

            var all = Entries
                .Find(x => x.RoomId == request.RoomId && x.StatChannel == request.StatChannel)
                .OrderBy(x => request.SortAscending ? x.Score : -x.Score)
                .ThenBy(x => x.PlayerId)
                .ToList();

            if (all.Count == 0)
                return new GetNearbyScoresResponse { rows = new List<LeaderboardRow>() };

            int center = all.Count;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].PlayerId == request.PlayerId)
                {
                    center = i;
                    break;
                }
            }

            int half = windowSize / 2;
            int start = center - half;
            int end = start + windowSize;

            if (start < 0)
            {
                end -= start;
                start = 0;
            }

            if (end > all.Count)
            {
                end = all.Count;
                start = end - windowSize;
                if (start < 0)
                    start = 0;
            }

            var rows = new List<LeaderboardRow>();
            for (int i = start; i < end; i++)
            {
                rows.Add(new LeaderboardRow
                {
                    playerId = all[i].PlayerId,
                    rank = i,
                    score = all[i].Score
                });
            }

            return new GetNearbyScoresResponse { rows = rows };
        }

        public static async Task<CheckAndSetStatResponse> CheckAndSetStat(long playerId, CheckAndSetStatRequest request)
        {
            //Console.WriteLine($"[LeaderboardDB] CheckAndSetStat called for playerId: {playerId}, roomId: {request.RoomId}, statChannel: {request.StatChannel}, statValue: {request.StatValue}, currentStatValue: {request.CurrentStatValue}");
            const int RROSuspiciousMin = 1147483647;
            const int RROSuspiciousMax = 2147383647;

            if (IsRRORoom(request.RoomId) && request.StatValue >= RROSuspiciousMin && request.StatValue <= RROSuspiciousMax)
            {
                await BanPlayerForLeaderboardAbuse(playerId);

                var existing = Entries.FindOne(x =>
                    x.PlayerId == playerId &&
                    x.RoomId == request.RoomId &&
                    x.StatChannel == request.StatChannel);

                if (existing == null)
                {
                    existing = new LeaderboardEntry
                    {
                        EntryId = GetNextEntryId(),
                        PlayerId = playerId,
                        RoomId = request.RoomId,
                        StatChannel = request.StatChannel,
                        FilterType = 0,
                        Score = 0,
                        UpdatedAt = DateTime.UtcNow
                    };
                    Entries.Insert(existing);
                }
                else
                {
                    existing.Score = 0;
                    existing.UpdatedAt = DateTime.UtcNow;
                    Entries.Update(existing);
                }

                int bannedRank = ComputeRank(request.RoomId, request.StatChannel, playerId, 0);

                return new CheckAndSetStatResponse
                {
                    error = null,
                    success = true,
                    value = new LeaderboardRow
                    {
                        playerId = playerId,
                        rank = bannedRank,
                        score = 0
                    }
                };
            }
            Console.WriteLine($"[LeaderboardDB] Proceeding with normal stat update for playerId: {playerId}, roomId: {request.RoomId}, statChannel: {request.StatChannel}, statValue: {request.StatValue}, currentStatValue: {request.CurrentStatValue}");

            var entry = Entries.FindOne(x =>
                x.PlayerId == playerId &&
                x.RoomId == request.RoomId &&
                x.StatChannel == request.StatChannel);

            int msg;
            switch (request.StatChannel)
            {
                case 0:
                    msg = 0;
                    break;
                case 1:
                    msg = 3;
                    break;
                default:
                    msg = 0;
                    break;
            }

            Console.WriteLine($"[LeaderboardDB] RoomId: {request.RoomId}, typeof: {request.RoomId.GetType()}");
                if (request.RoomId == 4974)
                {
                    Console.WriteLine($"[LeaderboardDB] PlayerId: {playerId}, typeof: {playerId.GetType()}");
                    int tokensToGrant = request.StatValue - request.CurrentStatValue;
                    Console.WriteLine($"[LeaderboardDB] Granting {tokensToGrant} tokens to player {playerId} for leaderboard performance.");
                    await APIController.SendTokenEarningWebhook(playerId, tokensToGrant.ToString(), msg);
                    if (tokensToGrant >= 1200)
                    {
                        PlayerDB.UpdatePlayerHeartbeat(playerId, null, online: false);
                        APIController.SendNonHileWebhook(playerId, "Tried to claim " + tokensToGrant + " tokens");
                        return new CheckAndSetStatResponse
                        {
                            error = null,
                            success = false,
                            value = null
                        };
                    }
                    var gift = GiftsDB.CreateGift(
								toPlayerId: playerId,
								fromPlayerId: 1,
								currency: tokensToGrant,
								currencyType: 2,
								balanceType: -2,
								giftContext: enums.GiftType.GameRewards_Tokens,
								giftRarity: 50,
								message: "Great job! You earned " + tokensToGrant + " tokens for your performance in the leaderboard!",
								platform: -1,
								platformsToSpawnOn: -1
					);

                    Console.WriteLine($"[LeaderboardDB] Created gift for player {playerId}.");

					await NotiController.SendEvent(playerId, "31", GiftsDB.MapToDTO(gift));

                    Console.WriteLine($"[LeaderboardDB] Sent event notification to player {playerId} for the gift JSON: {GiftsDB.MapToDTO(gift)}");
                }

            if (entry == null)
            {
                entry = new LeaderboardEntry
                {
                    EntryId = GetNextEntryId(),
                    PlayerId = playerId,
                    RoomId = request.RoomId,
                    StatChannel = request.StatChannel,
                    FilterType = 0,
                    Score = request.StatValue,
                    UpdatedAt = DateTime.UtcNow
                };
                Entries.Insert(entry);
            }
            else
            {
                entry.Score = request.StatValue;
                entry.UpdatedAt = DateTime.UtcNow;
                Entries.Update(entry);
            }

            Console.WriteLine($"[LeaderboardDB] Updated leaderboard entry for playerId: {playerId}, roomId: {request.RoomId}, statChannel: {request.StatChannel}, newScore: {entry.Score}");

            int rank = ComputeRank(request.RoomId, request.StatChannel, playerId, entry.Score);

            return new CheckAndSetStatResponse
            {
                error = null,
                success = true,
                value = new LeaderboardRow
                {
                    playerId = playerId,
                    rank = rank,
                    score = entry.Score
                }
            };
        }
    }
}