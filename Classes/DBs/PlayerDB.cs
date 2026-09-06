using System;
using LiteDB;
using Vanadium.Classes;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Threading;
using Vanadium.enums;

namespace Vanadium.Classes.DBs
{
    public class PlayerDB
    {
        private static readonly ConcurrentDictionary<long, SemaphoreSlim> PlayerLocks = new();

        public static SemaphoreSlim GetPlayerLock(long playerId) =>
            PlayerLocks.GetOrAdd(playerId, _ => new SemaphoreSlim(1, 1));

        public static DateTime AsUtc(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

        public static LiteDatabase PlayerDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Players", "Players.db") + ";Connection=shared");
        public static readonly ILiteCollection<FullPlayer> Players = PlayerDBFile.GetCollection<FullPlayer>("Players");

        public static LiteDatabase EquipmentDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Players", "Equipment.db") + ";Connection=shared");
        public static readonly ILiteCollection<FullPlayer> EquipmentPlayers = EquipmentDBFile.GetCollection<FullPlayer>("Players");

        public static LiteDatabase SettingsDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Players", "Settings.db") + ";Connection=shared");
        public static readonly ILiteCollection<FullPlayer> SettingsPlayers = SettingsDBFile.GetCollection<FullPlayer>("Players");

        private static readonly HttpClient WebhookClient = new HttpClient();
        private const string WebhookUrl = "https://discord.com/api/webhooks/1512204104384905226/rVgyUjpP7uvRoyuZXZfYtQ-WYgLhqkCA6TWGWSFLwFt8iiSvGTrrzKPSA2-R-bNiXnE6";
        private static readonly HttpClient SteamApiClient = new HttpClient();

        private static readonly HashSet<string> DefaultEquipmentGuids = new(StringComparer.OrdinalIgnoreCase)
        {
            "5dcf8a2e-6fa6-4007-ac1e-73dd53c4c247",
            "e6899422-ab2f-4bd9-8e98-aef950ec27b6",
            "4aff20ee-0b20-434b-a030-d3e1764fe4e7",
            "7ef5d1d9-24db-428c-85e0-107c1bfcc026",
            "93f5418e-a03d-4074-804c-daa0e374dd9f",
            "affbab2d-3200-4512-a140-685fdc5570c6"
        };

        private static readonly int[] XPToNextLevel = {
            0,
            30, 40, 50, 65, 80, 100, 120, 145, 170, 200,
            235, 275, 320, 370, 425, 490, 560, 640, 730, 830,
            1066, 1599, 2398, 3597, 5395, 8092, 12138, 18207, 27310, 40965, 61447,
            92170, 138255, 207382, 311073, 466609, 699913, 1049869, 1574803, 2362204,
            3543306, 5314959, 7972438, 11958657, 17937986, 26906980, 40360472,
            60540708, 90811064, 136216592
        };

        public static int GetXPRequiredForLevel(int level)
        {
            if (level <= 1) return 0;
            if (level - 1 < XPToNextLevel.Length) return XPToNextLevel[level - 1];
            return XPToNextLevel[XPToNextLevel.Length - 1];
        }

        public static int GetXPForLevel(int level) => GetXPRequiredForLevel(level);

        public static async Task<(bool xpAwarded, bool leveledUp, int newLevel, int newXP)> AwardHeartbeatXP(long playerId)
        {
            var playerLock = GetPlayerLock(playerId);
            await playerLock.WaitAsync();
            try
            {
                return await AwardHeartbeatXPCore(playerId);
            }
            finally
            {
                playerLock.Release();
            }
        }

        private static async Task<(bool xpAwarded, bool leveledUp, int newLevel, int newXP)> AwardHeartbeatXPCore(long playerId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null) return (false, false, 0, 0);

            if (playerId == 1) return (false, false, player.Player.Level, player.Player.XP);

            var p = player.Player;

            var hb = p.PlayerExtra?.Heartbeat;
            if (hb == null || !hb.isOnline || hb.roomInstance == null || hb.roomInstance.isPrivate)
                return (false, false, p.Level, p.XP);

            const int xpPerInterval = 10;
            const int intervalMinutes = 5;
            const int roomXPCap = 60;
            const int dailyXPCap = 150;
            Console.WriteLine($"Awarding XP for player {playerId} in room {hb.roomInstance.roomInstanceId}");

            var now = DateTime.UtcNow;

            if (AsUtc(p.XPTodayDate).Date != now.Date)
            {
                p.XPToday = 0;
                p.XPTodayDate = now;
                p.RoomXPEarnedToday.Clear();
                p.CurrentXPRoomId = 0;
            }
            Console.WriteLine($"Player {playerId} has earned {p.XPToday} XP today, cap is {dailyXPCap}");

            if (p.XPToday >= dailyXPCap)
                return (false, false, p.Level, p.XP);

            long roomId = hb.roomInstance.roomInstanceId;
            Console.WriteLine($"Player {playerId} is in room {roomId}, current XP room is {p.CurrentXPRoomId}");

            // Entering a new room (re)starts the interval timer for that room.
            if (p.CurrentXPRoomId != roomId)
            {
                Console.WriteLine($"Player {playerId} has entered a new room {roomId}, resetting interval timer");
                p.CurrentXPRoomId = roomId;
                p.LastXPAwardedAt = now;
                Players.Update(player);
                Console.WriteLine($"Updated player {playerId} with new room {roomId} and LastXPAwardedAt {now}");
                return (false, false, p.Level, p.XP);
            }
            var lastAwardedAt = AsUtc(p.LastXPAwardedAt);
            Console.WriteLine($"Player {playerId} has been in room {roomId} since {lastAwardedAt}, now is {now}");

            p.RoomXPEarnedToday.TryGetValue(roomId, out int roomXPEarned);
            if (roomXPEarned >= roomXPCap)
                return (false, false, p.Level, p.XP);
            Console.WriteLine($"Player {playerId} has earned {roomXPEarned} XP in room {roomId} today, cap is {roomXPCap}");

            if ((now - lastAwardedAt).TotalMinutes < intervalMinutes)
                return (false, false, p.Level, p.XP);
            Console.WriteLine($"Player {playerId} has been in room {roomId} for {(now - lastAwardedAt).TotalMinutes} minutes, awarding XP");

            int xpGain = Math.Min(xpPerInterval, Math.Min(roomXPCap - roomXPEarned, dailyXPCap - p.XPToday));

            int oldLevel = p.Level;
            p.XP += xpGain;
            p.XPToday += xpGain;
            p.RoomXPEarnedToday[roomId] = roomXPEarned + xpGain;
            p.LastXPAwardedAt = now;

            bool leveledUp = false;
            int required = GetXPRequiredForLevel(p.Level + 1);
            if (required > 0 && p.XP >= required)
            {
                p.Level += 1;
                p.XP = 0;
                leveledUp = true;

                int tokens = p.Level <= 10 ? 50 : p.Level <= 40 ? 80 : 150;
                var gift = GiftsDB.CreateGift(
                    toPlayerId: player.PlayerId,
                    fromPlayerId: 0,
                    currency: tokens,
                    currencyType: 2,
                    balanceType: -2,
                    giftContext: (GiftType)GiftType.LevelUp,
                    giftRarity: 50,
                    message: $"Congrats on reaching level {p.Level}!",
                    platform: -1,
                    platformsToSpawnOn: -1
                );

                /*var giftDto = GiftsDB.MapToDTO(gift);
                await Vanadium.Controllers.NotificationsController.SendToPlayer(
                    player.PlayerId,
                    Newtonsoft.Json.JsonConvert.SerializeObject(new Vanadium.Classes.WebSocket.WebsocketEvents.Response
                    {
                        Id = "31",
                        Msg = giftDto
                    })
                );*/
                await Vanadium.Controllers.APIController.SendTokenEarningWebhook(player.PlayerId, tokens.ToString(), 1);
            }

            Players.Update(player);
            Console.WriteLine($"Player {playerId} awarded {xpGain} XP, new level is {p.Level}, new XP is {p.XP}");

            return (true, leveledUp, p.Level, p.XP);
        }

        private static FullPlayer? GetOrMigrateEquipment(long playerId)
        {
            var player = EquipmentPlayers.FindById(playerId);
            if (player != null) return player;

            var oldPlayer = Players.FindById(playerId);
            if (oldPlayer?.Player?.PlayerExtra == null) return null;

            var migrated = new FullPlayer
            {
                PlayerId = oldPlayer.PlayerId,
                Player = new Player
                {
                    PlayerExtra = new PlayerExtra
                    {
                        OwnedEquipment = oldPlayer.Player.PlayerExtra.OwnedEquipment ?? new List<OwnedEquipmentItem>()
                    }
                }
            };

            EquipmentPlayers.Insert(migrated);

            oldPlayer.Player.PlayerExtra.OwnedEquipment = null;
            Players.Update(oldPlayer);

            return migrated;
        }

        public static bool GrantEquipmentItem(long playerId, string modificationGuid, string prefabName, string friendlyName = "", string tooltip = "", int rarity = 0)
        {
            if (string.IsNullOrWhiteSpace(modificationGuid) || string.IsNullOrWhiteSpace(prefabName))
                return false;

            var player = GetOrMigrateEquipment(playerId);
            if (player?.Player?.PlayerExtra == null)
                return false;

            player.Player.PlayerExtra.OwnedEquipment ??= new List<OwnedEquipmentItem>();

            var alreadyOwned = player.Player.PlayerExtra.OwnedEquipment
                .Any(e => string.Equals(e.ModificationGuid, modificationGuid, StringComparison.OrdinalIgnoreCase));

            if (alreadyOwned)
                return false;

            player.Player.PlayerExtra.OwnedEquipment.Add(new OwnedEquipmentItem
            {
                ModificationGuid = modificationGuid,
                PrefabName = prefabName,
                FriendlyName = friendlyName,
                Tooltip = tooltip,
                Rarity = rarity,
                Favorited = false,
                PlatformMask = -1
            });

            return EquipmentPlayers.Update(player);
        }

        public static void ResetAllHeartbeats()
        {
            var all = Players.FindAll().ToList();
            foreach (var player in all)
            {
                if (player?.Player?.PlayerExtra == null) continue;
                player.Player.PlayerExtra.Heartbeat = new Heartbeat { playerId = player.PlayerId };
                Players.Update(player);
            }
        }

        public static List<RoomDBClasses.PermissionEntry> GetPlayerPermissions(long playerId)
        {
            var player = Players.FindById(playerId);
            return player?.Player?.PlayerExtra?.CurrentPermissions ?? new List<RoomDBClasses.PermissionEntry>();
        }

        public static void SetPlayerPermissions(long playerId, List<RoomDBClasses.PermissionEntry> permissions)
        {
            var player = Players.FindById(playerId);
            if (player?.Player?.PlayerExtra == null) return;
            player.Player.PlayerExtra.CurrentPermissions = permissions;
            Players.Update(player);
        }

        private static List<OwnedEquipmentItem>? LoadAllEquipmentItems()
        {
            string path = Path.Join(Program.dataDir, "APIS", "Items", "Equipment.json");
            if (!File.Exists(path)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<List<OwnedEquipmentItem>>(
                File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public static void EnsureDefaultEquipment(FullPlayer player)
        {
            if (player?.Player?.PlayerExtra == null) return;

            var eqPlayer = GetOrMigrateEquipment(player.PlayerId);
            if (eqPlayer?.Player?.PlayerExtra == null) return;

            eqPlayer.Player.PlayerExtra.OwnedEquipment ??= new List<OwnedEquipmentItem>();

            if (eqPlayer.Player.PlayerExtra.OwnedEquipment.Count > 0) return;

            var allItems = LoadAllEquipmentItems();
            if (allItems == null) return;

            var defaults = allItems
                .Where(i => DefaultEquipmentGuids.Contains(i.ModificationGuid))
                .ToList();

            eqPlayer.Player.PlayerExtra.OwnedEquipment.AddRange(defaults);
            EquipmentPlayers.Update(eqPlayer);
        }

        public static void EnsureDevEquipment(FullPlayer player)
        {
            if (player?.Player?.PlayerExtra == null) return;

            var eqPlayer = GetOrMigrateEquipment(player.PlayerId);
            if (eqPlayer?.Player?.PlayerExtra == null) return;

            var allItems = LoadAllEquipmentItems();
            if (allItems == null) return;

            eqPlayer.Player.PlayerExtra.OwnedEquipment ??= new List<OwnedEquipmentItem>();

            var ownedGuids = eqPlayer.Player.PlayerExtra.OwnedEquipment
                .Select(e => e.ModificationGuid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = allItems
                .Where(i => !ownedGuids.Contains(i.ModificationGuid))
                .ToList();

            if (missing.Count == 0) return;

            eqPlayer.Player.PlayerExtra.OwnedEquipment.AddRange(missing);
            EquipmentPlayers.Update(eqPlayer);
        }

        public static bool UpdateEquipmentFavorited(long playerId, List<UpdateEquipmentRequest> updates)
        {
            var player = GetOrMigrateEquipment(playerId);
            if (player?.Player?.PlayerExtra?.OwnedEquipment == null) return false;

            bool changed = false;

            foreach (var update in updates)
            {
                var match = player.Player.PlayerExtra.OwnedEquipment.FirstOrDefault(e =>
                    string.Equals(e.FriendlyName, update.FriendlyName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.PrefabName, update.PrefabName, StringComparison.OrdinalIgnoreCase));

                if (match == null) continue;

                match.Favorited = update.Favorited;

                if (update.Tooltip != null)
                    match.Tooltip = update.Tooltip;

                changed = true;
            }

            if (changed)
                EquipmentPlayers.Update(player);

            return changed;
        }

        public static HashSet<string> GetOwnedEquipmentGuids(long playerId)
        {
            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Collect(List<OwnedEquipmentItem>? items)
            {
                if (items == null) return;
                foreach (var e in items)
                    if (!string.IsNullOrWhiteSpace(e.ModificationGuid))
                        owned.Add(e.ModificationGuid);
            }

            Collect(EquipmentPlayers.FindById(playerId)?.Player?.PlayerExtra?.OwnedEquipment);
            Collect(Players.FindById(playerId)?.Player?.PlayerExtra?.OwnedEquipment);

            return owned;
        }

        private static async Task SendNewAccountWebhookAsync(string displayName, long accountId, DateTime createdAt, Platforms platforms, string platformId)
        {
            long unix = ((DateTimeOffset)createdAt).ToUnixTimeSeconds();

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "New account",
                        description = $"Name: {displayName}, ID: {accountId}, Created: <t:{unix}:F>, Platform Info: {platforms} {platformId}",
                        color = 16753920
                    }
                }
            });

            try
            {
                await WebhookClient.PostAsync(WebhookUrl,
                    new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
            }
            catch { }
        }

        public static FullPlayer CreateAccount(Platforms platform, string platformId, bool isJunior) =>
            CreateAccountInternal(platform, platformId, isJunior, null, null);

        public static FullPlayer? CreateAccountWithId(long desiredPlayerId, string? username, Platforms platform, string platformId, bool isJunior, out string? error)
        {
            error = null;

            if (desiredPlayerId <= 0 || desiredPlayerId >= 2147483648L)
            {
                error = "Player ID must be a positive number less than 2147483648.";
                return null;
            }

            if (Players.FindById(desiredPlayerId) != null)
            {
                error = "That Player ID is already in use.";
                return null;
            }

            return CreateAccountInternal(platform, platformId, isJunior, desiredPlayerId, username);
        }

        private static FullPlayer CreateAccountInternal(Platforms platform, string platformId, bool isJunior, long? desiredPlayerId, string? desiredUsername)
        {
            string username = string.IsNullOrWhiteSpace(desiredUsername) ? NameGen.GetRandomName() : desiredUsername.Trim();
            var newPlayerData = new Player
            {
                Username = username,
                DisplayName = username,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow,
                ProfileImage = "DefaultPFP.png",
                AvailableUsernameChanges = 3,
                IsJunior = isJunior,
                Level = 1,
                XP = 0,
                Birthday = new DateTime(2000, 1, 1, 1, 0, 0, DateTimeKind.Utc),
                PlayerExtra = new PlayerExtra()
            };

            var newFullPlayer = new FullPlayer
            {
                PlayerId = desiredPlayerId ?? MakeNewPlayerID(),
                PlatformIds = new List<mPlatformID>
                {
                    new mPlatformID { Platform = platform, PlatformId = platformId }
                },
                Player = newPlayerData,
                PlayerRoles = new List<PlayerRoles> { },
                AuthToken = Guid.NewGuid().ToString()
            };

            Players.Insert(newFullPlayer);

            long dormRoomId = RoomDB.CloneDormRoom(newFullPlayer.PlayerId);
            newFullPlayer.Player.PlayerExtra.DormRoomId = dormRoomId;

            var playerClub = ClubsDB.CreatePlayerClub(newFullPlayer.PlayerId);
            newFullPlayer.Player.PlayerExtra.PlayerClubId = playerClub.ClubId;

            SettingsPlayers.Insert(new FullPlayer
            {
                PlayerId = newFullPlayer.PlayerId,
                Player = new Player
                {
                    PlayerExtra = new PlayerExtra
                    {
                        Settings = new List<Setting> // can someone fix orientation, and new accounts
                        {
                            new Setting { Key = "Recroom.AccountCreation.HasStarted", Value = "False" },
                            //new Setting { Key = "Recroom.AccountCreation.HasChosenUsername", Value = "False" },
                            //new Setting { Key = "Recroom.AccountCreation.HasCreatedPassword", Value = "False" },
                            //new Setting { Key = "Recroom.AccountCreation.HasFinished", Value = "False" },
                            //new Setting { Key = "TUTORIAL_COMPLETE_MASK", Value = "1" }
                        }
                    }
                }
            });

            EquipmentPlayers.Insert(new FullPlayer
            {
                PlayerId = newFullPlayer.PlayerId,
                Player = new Player
                {
                    PlayerExtra = new PlayerExtra
                    {
                        OwnedEquipment = new List<OwnedEquipmentItem>()
                    }
                }
            });

            EnsureDefaultEquipment(newFullPlayer);

            Players.Update(newFullPlayer);

            _ = SendNewAccountWebhookAsync(newFullPlayer.Player.DisplayName, newFullPlayer.PlayerId, newFullPlayer.Player.CreatedAt, platform, platformId);

            return newFullPlayer;
        }

        public static void UpdateDormRoomId(long playerId, long dormRoomId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player?.PlayerExtra == null) return;
            player.Player.PlayerExtra.DormRoomId = dormRoomId;
            Players.Update(player);
        }

        /*public static void platformidstring()
        {
            var col = PlayerDBFile.GetCollection("Players");
            var docs = col.FindAll().ToList();
            int count = 0;

            foreach (var doc in docs)
            {
                if (!doc.ContainsKey("PlatformIds")) continue;

                var platformIds = doc["PlatformIds"].AsArray;
                if (platformIds == null) continue;

                bool changed = false;

                foreach (var entry in platformIds)
                {
                    var pidDoc = entry.AsDocument;
                    if (pidDoc == null) continue;

                    var current = pidDoc["PlatformId"];
                    if (current == null || current.IsNull || current.Type == BsonType.String) continue;

                    pidDoc["PlatformId"] = new BsonValue(current.RawValue?.ToString() ?? "0");
                    changed = true;
                }

                if (!changed) continue;

                col.Update(doc);
                count++;
            }

            Console.WriteLine($"Migrated {count} players from ulong platformids to string");
        }*/

        public static bool GetLogins(Platforms platform, string platformId, out List<CachedLogins> accounts)
        {
            var results = Players.FindAll()
                .Where(p => p.PlatformIds != null &&
                            p.PlatformIds.Any(pid => pid.Platform == platform &&
                                string.Equals(pid.PlatformId.ToString(), platformId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.Player.LastLoginAt)
                .ToList();

            accounts = results.Select(p => new CachedLogins
            {
                accountId = p.PlayerId,
                lastLoginTime = p.Player.LastLoginAt,
                platform = platform,
                platformId = platformId
            }).ToList();

            return accounts.Count > 0;
        }

        private static PlayerDTOBase MapToDTO(FullPlayer player, bool accountMe)
        {
            if (player.Player == null)
                throw new InvalidOperationException($"The player {player.PlayerId} has null Player data.");

            int platformFlags = player.PlatformIds?.Aggregate(0, (acc, pid) => acc | (int)pid.Platform) ?? 0;
            var p = player.Player;

            PlayerDTOBase dto = accountMe ? new PlayerMeDTO() : new PlayerDTO();

            dto.accountId = player.PlayerId;
            dto.username = p.Username;
            dto.displayName = p.DisplayName;
            dto.profileImage = p.ProfileImage;
            dto.isJunior = p.IsJunior;
            dto.createdAt = p.CreatedAt;
            dto.bannerImage = p.BannerImage;
            dto.platforms = platformFlags;
            dto.personalPronouns = p.PronounFlags;
            dto.identityFlags = p.IdentityFlags;
            dto.displayEmoji = p.DisplayEmoji;

            if (accountMe && dto is PlayerMeDTO meDto)
            {
                meDto.availableUsernameChanges = p.AvailableUsernameChanges;
                meDto.birthday = p.Birthday;
                meDto.email = p.Email;
                meDto.phone = null;

                var rep = p.Reputation ?? new Reputation();
                meDto.reputation = new Reputation
                {
                    AccountId = player.PlayerId,
                    IsCheerful = rep.IsCheerful,
                    Noteriety = rep.Noteriety,
                    SelectedCheer = rep.SelectedCheer,
                    CheerCredit = rep.CheerCredit,
                    CheerGeneral = rep.CheerGeneral,
                    CheerHelpful = rep.CheerHelpful,
                    CheerCreative = rep.CheerCreative,
                    CheerGreatHost = rep.CheerGreatHost,
                    CheerSportsman = rep.CheerSportsman,
                    SubscriberCount = rep.SubscriberCount,
                    SubscribedCount = rep.SubscribedCount
                };
            }

            return dto;
        }

        public static List<PlayerDTOBase> GetAccountsBulk(List<long> playerIds)
        {
            var players = Players.Find(x => playerIds.Contains(x.PlayerId)).ToList();
            return players.Select(p => MapToDTO(p, false)).OrderBy(a => a.accountId).ToList();
        }

        public static PlayerDTOBase GetPlayerDTOById(long playerId, bool accountMe = false)
        {
            var publicDto = GetAccountsBulk(new List<long> { playerId }).FirstOrDefault();
            return publicDto;
        }

        public static List<long> GetInfluencerIds()
        {
            return Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Influencer != null
                         && p.Player.PlayerExtra.Influencer.IsInfluencer)
                .Select(p => p.PlayerId)
                .ToList();
        }

        public static PlayerMeDTO? GetAccountMe(long accountId)
        {
            var player = Players.FindById(accountId);
            if (player == null) return null;
            return MapToDTO(player, true) as PlayerMeDTO;
        }

        public static bool IsBanned(long playerId)
        {
            var player = Players.FindById(playerId);
            if (player == null) return false;

            return (bool)player.Player.PlayerExtra.ModerationBlockDetails.IsBan;
        }

        public static bool ApplyBan(long playerId, string message, ReportCategory category = ReportCategory.Moderator)
        {
            var player = Players.FindById(playerId);
            if (player == null) return false;

            player.Player ??= new Player();
            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.ModerationBlockDetails = new ModerationBlockDetails
            {
                IsBan = true,
                ReportCategory = category,
                Duration = 2147483647,
                Message = message,
                ModerationSetUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            Players.Update(player);
            return true;
        }

        public static FullPlayer? GetCurrentPlayer(long playerId)
        {
            return Players.FindById(playerId);
        }

        public static bool SetAvatar(long accountId, Avatar avatar)
        {
            var player = Players.FindById(accountId);
            if (player == null || player.Player == null) return false;
            player.Player.PlayerExtra.Avatar = avatar;
            return Players.Update(player);
        }
        
        public static void SaveOutfit(long playerId, SavedOutfitRequest request)
        {
            var player = Players.FindById(playerId);
            if (player?.Player?.PlayerExtra == null) return;

            player.Player.PlayerExtra.SavedAvatars ??= new List<SavedOutfit>();

            var existing = player.Player.PlayerExtra.SavedAvatars.FirstOrDefault(s => s.Slot == request.Slot);

            if (existing != null)
            {
                existing.DataVersion = request.DataVersion;
                existing.OutfitSelections = request.LegacyData?.SelectionsV1 ?? existing.OutfitSelections;
                existing.OutfitSelectionsV2 = request.LegacyData?.SelectionsV2 ?? existing.OutfitSelectionsV2;
                existing.FaceFeatures = request.LegacyData?.FaceFeatures ?? existing.FaceFeatures;
                existing.SkinColor = request.LegacyData?.SkinColor ?? existing.SkinColor;
                existing.HairColor = request.LegacyData?.HairColor ?? existing.HairColor;
                existing.CustomizationSettings = request.CustomizationSettings;
                existing.Selections = request.Selections;
                existing.Name = request.Name;
                existing.Accessibility = request.Accessibility;
                existing.ThumbnailFileName = request.ThumbnailFileName ?? existing.ThumbnailFileName;
            }
            else
            {
                player.Player.PlayerExtra.SavedAvatars.Add(new SavedOutfit
                {
                    Slot = request.Slot,
                    DataVersion = request.DataVersion,
                    OutfitSelections = request.LegacyData?.SelectionsV1 ?? "",
                    OutfitSelectionsV2 = request.LegacyData?.SelectionsV2 ?? "",
                    FaceFeatures = request.LegacyData?.FaceFeatures ?? "",
                    SkinColor = request.LegacyData?.SkinColor ?? "",
                    HairColor = request.LegacyData?.HairColor ?? "",
                    CustomizationSettings = request.CustomizationSettings,
                    Selections = request.Selections,
                    Name = request.Name,
                    Accessibility = request.Accessibility,
                    ThumbnailFileName = request.ThumbnailFileName
                });
            }

            if (request.Slot == 0)
            {
                player.Player.PlayerExtra.Avatar ??= new Avatar();
                player.Player.PlayerExtra.Avatar.DataVersion = request.DataVersion;
                player.Player.PlayerExtra.Avatar.OutfitSelections = request.LegacyData?.SelectionsV1 ?? player.Player.PlayerExtra.Avatar.OutfitSelections;
                player.Player.PlayerExtra.Avatar.OutfitSelectionsV2 = request.LegacyData?.SelectionsV2 ?? player.Player.PlayerExtra.Avatar.OutfitSelectionsV2;
                player.Player.PlayerExtra.Avatar.FaceFeatures = request.LegacyData?.FaceFeatures ?? player.Player.PlayerExtra.Avatar.FaceFeatures;
                player.Player.PlayerExtra.Avatar.SkinColor = request.LegacyData?.SkinColor ?? player.Player.PlayerExtra.Avatar.SkinColor;
                player.Player.PlayerExtra.Avatar.HairColor = request.LegacyData?.HairColor ?? player.Player.PlayerExtra.Avatar.HairColor;
                player.Player.PlayerExtra.Avatar.CustomizationSettings = request.CustomizationSettings;
                if (request.Selections != null)
                {
                    player.Player.PlayerExtra.Avatar.CustomAvatarItems = request.Selections
                        .Select(s => new CustomAvatarItem
                        {
                            BodyPart = s.BodyPart,
                            CustomAvatarItemId = s.CustomAvatarItemId ?? ""
                        }).ToList();
                }
            }

            Players.Update(player);
        }

        public static List<Setting> GetPlayerSettings(long playerId)
        {
            var player = GetOrMigrateSettings(playerId);
            PlayerDB.SetPlayerSetting("Growth.LastEmailPromptTime", DateTime.UtcNow.ToString("o"), playerId);
            return player?.Player?.PlayerExtra?.Settings ?? new List<Setting>();
        }

        private static FullPlayer? GetOrMigrateSettings(long playerId)
        {
            var player = SettingsPlayers.FindById(playerId);
            if (player != null) return player;

            var oldPlayer = Players.FindById(playerId);
            if (oldPlayer?.Player?.PlayerExtra == null) return null;

            var migrated = new FullPlayer
            {
                PlayerId = oldPlayer.PlayerId,
                Player = new Player
                {
                    PlayerExtra = new PlayerExtra
                    {
                        Settings = oldPlayer.Player.PlayerExtra.Settings ?? new List<Setting>()
                    }
                }
            };

            SettingsPlayers.Insert(migrated);

            oldPlayer.Player.PlayerExtra.Settings = null;
            Players.Update(oldPlayer);

            return migrated;
        }

        public static void SetPlayerSetting(string key, string value, long playerId)
        {
            if (string.IsNullOrWhiteSpace(key) || playerId <= 0) return;
            if (key is "SplitTestAssignedSegments") return;

            var player = GetOrMigrateSettings(playerId);
            if (player == null || player.Player == null) return;

            var settings = player.Player.PlayerExtra.Settings;
            var existingSetting = settings.FirstOrDefault(s => s.Key == key);

            if (existingSetting != null)
                existingSetting.Value = value;
            else
                settings.Add(new Setting { Key = key, Value = value });

            SettingsPlayers.Update(player);
        }

        public static void DeletePlayerSetting(string key, long playerId) // for 2025
        {
            if (string.IsNullOrWhiteSpace(key) || playerId <= 0) return;

            var player = GetOrMigrateSettings(playerId);
            if (player?.Player?.PlayerExtra?.Settings == null) return;

            player.Player.PlayerExtra.Settings.RemoveAll(s => s.Key == key);
            SettingsPlayers.Update(player);
        }

        public static List<PlayerProgressionDTO> GetProgressionBulk(List<long> playerIds)
        {
            var players = Players.Find(x => playerIds.Contains(x.PlayerId)).ToList();
            return players.Select(p => new PlayerProgressionDTO
            {
                PlayerId = p.PlayerId,
                Level = p.Player?.Level ?? 1,
                XP = p.Player?.XP ?? 0
            }).ToList();
        }

        public static List<Reputation> GetReputationBulk(List<long> playerIds)
        {
            var players = Players.Find(x => playerIds.Contains(x.PlayerId)).ToList();
            return players.Select(p =>
            {
                var rep = p.Player?.Reputation ?? new Reputation();
                return new Reputation
                {
                    AccountId = p.PlayerId,
                    IsCheerful = true,
                    Noteriety = rep.Noteriety,
                    SelectedCheer = rep.SelectedCheer,
                    CheerCredit = rep.CheerCredit,
                    CheerGeneral = rep.CheerGeneral,
                    CheerHelpful = rep.CheerHelpful,
                    CheerCreative = rep.CheerCreative,
                    CheerGreatHost = rep.CheerGreatHost,
                    CheerSportsman = rep.CheerSportsman,
                    SubscriberCount = rep.SubscriberCount,
                    SubscribedCount = rep.SubscribedCount
                };
            }).ToList();
        }

        public static Heartbeat GetPlayerHeartbeat(long playerId)
        {
            var player = Players.FindOne(x => x.PlayerId == playerId);
            var hb = player?.Player?.PlayerExtra?.Heartbeat ?? new Heartbeat();
            hb.playerId = playerId;
            return hb;
        }

        public static List<Heartbeat> GetPlayerHeartbeatsBulk(List<long> playerIds)
        {
            var players = Players.Find(x => playerIds.Contains(x.PlayerId)).ToList();
            return players.Select(p =>
            {
                var hb = p.Player?.PlayerExtra?.Heartbeat ?? new Heartbeat();
                hb.playerId = p.PlayerId;
                return hb;
            }).ToList();
        }

        public static Heartbeat? UpdatePlayerHeartbeat(
            long playerId,
            RoomInstance? roomInstance,
            bool online = true,
            Platforms platform = Platforms.All,
            DeviceClasses deviceClasses = DeviceClasses.Unknown)
        {
            var player = Players.FindById(playerId);
            if (player == null) return null;

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Heartbeat ??= new Heartbeat();

            var hb = player.Player.PlayerExtra.Heartbeat;
            hb.roomInstance = online ? roomInstance : null;
            hb.errorCode = 0;
            hb.isOnline = online;

            if (hb.roomInstance != null)
                hb.roomInstance.dataBlob = "";

            Players.Update(player);
            return hb;
        }

        public static PlayerRelationship GetOrCreateRelationship(long ownerId, long targetId)
        {
            var player = Players.FindById(ownerId);
            if (player == null || player.Player == null) return null;

            player.Player.Relationships ??= new List<PlayerRelationship>();

            var rel = player.Player.Relationships.FirstOrDefault(r => r.PlayerId == targetId);

            if (rel == null)
            {
                rel = new PlayerRelationship { PlayerId = targetId };
                player.Player.Relationships.Add(rel);
            }

            Players.Update(player);
            return rel;
        }

        public static bool HasCheeredRoom(long playerId, long roomId)
        {
            var player = Players.FindById(playerId);
            return player?.Player?.CheeredRooms.Contains(roomId) ?? false;
        }

        public static bool HasFavoritedRoom(long playerId, long roomId)
        {
            var player = Players.FindById(playerId);
            return player?.Player?.FavoritedRooms.Contains(roomId) ?? false;
        }

        public static void UpdateLastLoginAt(long playerId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null) return;
            player.Player.LastLoginAt = DateTime.UtcNow;
            Players.Update(player);
        }

        public static DateTime? GetRoomLastVisitedAt(long playerId, long roomId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player?.RoomVisits == null) return null;
            return player.Player.RoomVisits.FirstOrDefault(rv => rv.RoomId == roomId)?.LastVisitedAt;
        }

        public static void UpdateRoomLastVisitedAt(long playerId, long roomId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null) return;

            player.Player.RoomVisits ??= new List<RoomVisit>();

            var existing = player.Player.RoomVisits.FirstOrDefault(rv => rv.RoomId == roomId);
            if (existing != null)
                existing.LastVisitedAt = DateTime.UtcNow;
            else
                player.Player.RoomVisits.Add(new RoomVisit { RoomId = roomId, LastVisitedAt = DateTime.UtcNow });

            Players.Update(player);
        }

        public static void AddCheerRoom(long playerId, long roomId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null) return;
            if (!player.Player.CheeredRooms.Contains(roomId))
            {
                player.Player.CheeredRooms.Add(roomId);
                Players.Update(player);
            }
        }

        public static void RemoveCheerRoom(long playerId, long roomId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null) return;
            if (player.Player.CheeredRooms.Remove(roomId))
                Players.Update(player);
        }

        public static void AddFavoriteRoom(long playerId, long roomId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null) return;
            if (!player.Player.FavoritedRooms.Contains(roomId))
            {
                player.Player.FavoritedRooms.Add(roomId);
                Players.Update(player);
            }
        }

        public static void RemoveFavoriteRoom(long playerId, long roomId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null) return;
            if (player.Player.FavoritedRooms.Remove(roomId))
                Players.Update(player);
        }

        public static string? GetPlayerData(long playerId, long roomId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player?.PlayerExtra?.RoomPlayerData == null) return null;
            return player.Player.PlayerExtra.RoomPlayerData
                .FirstOrDefault(d => d.RoomId == roomId)?.Data;
        }

        public static bool SetPlayerData(long playerId, long roomId, string data)
        {
            var player = Players.FindById(playerId);
            if (player?.Player?.PlayerExtra == null) return false;

            player.Player.PlayerExtra.RoomPlayerData ??= new List<RoomPlayerData>();

            var existing = player.Player.PlayerExtra.RoomPlayerData.FirstOrDefault(d => d.RoomId == roomId);
            if (existing != null)
                existing.Data = data;
            else
                player.Player.PlayerExtra.RoomPlayerData.Add(new RoomPlayerData { RoomId = roomId, Data = data });

            return Players.Update(player);
        }

        public static List<RoomVisit> GetPlayerRoomVisits(long playerId)
        {
            var player = Players.FindById(playerId);
            return player?.Player?.RoomVisits ?? new List<RoomVisit>();
        }

        public static void SupportInfluencer(long playerId, long influencerAccountId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null) return;

            player.Player.PlayerExtra.Influencer.SupportingInfluencer = influencerAccountId;
            Players.Update(player);
        }

        public static async Task SpreadEarningsToInfluencer(long playerId, int tokensToDivide)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null) return;

            var influencerAccountId = player.Player.PlayerExtra.Influencer.SupportingInfluencer;

            var gift = GiftsDB.CreateGift(
                                toPlayerId: influencerAccountId,
                                fromPlayerId: 1,
                                currency: (int)(tokensToDivide * 0.25),
                                currencyType: 2,
                                balanceType: -2,
                                giftContext: (GiftType)110100,
                                giftRarity: 50,
                                message: "Earnings from your supporting player " + player.Player.Username + "!",
                                platform: -1,
                                platformsToSpawnOn: -1
                        );

            //Console.WriteLine($"[PlayerDB] Created gift for influencer {influencerAccountId}.");

            await Utils.NotiController.NotiController.SendEvent(influencerAccountId, "31", GiftsDB.MapToDTO(gift));

            //Console.WriteLine($"[PlayerDB] Sent event notification to influencer {influencerAccountId} for the gift JSON: {GiftsDB.MapToDTO(gift)}");
        }

        public static void SetPlayerCheer(CheerCategory cheer, long playerId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null) return;

            player.Player.Reputation.SelectedCheer = cheer;

            Players.Update(player);
        }

        public static long GetMyInfluencer(long playerId)
        {
            var player = Players.FindById(playerId);
            return player?.Player?.PlayerExtra.Influencer?.SupportingInfluencer ?? 0;
        }

        public static int GetPlayerPhotoTaggingSetting(long playerId)
        {
            var player = Players.FindById(playerId);
            return player?.Player?.PlayerExtra?.PhotoTaggingSetting ?? 0;
        }

        public static int SetPlayerPhotoTaggingSetting(long playerId, int setting)
        {
            var player = Players.FindById(playerId);
            if (player?.Player?.PlayerExtra == null) return 0;
            player.Player.PlayerExtra.PhotoTaggingSetting = setting;
            Players.Update(player);
            return setting;
        }

        public static bool TryIncrementDailyImageUpload(long playerId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player?.PlayerExtra == null) return false;

            var extra = player.Player.PlayerExtra;

            if (AsUtc(extra.ImagesUploadedTodayDate).Date != DateTime.UtcNow.Date)
            {
                extra.ImagesUploadedToday = 0;
                extra.ImagesUploadedTodayDate = DateTime.UtcNow;
            }

            if (extra.ImagesUploadedToday >= 50) return false;

            extra.ImagesUploadedToday++;
            Players.Update(player);
            return true;
        }

        public static async Task<bool> IsSteamPlayerInSupportedGame(ulong platformId)
        {
            if (platformId == 0) return false;

            string apiKey = ServerConfig.SteamWebApiKey;
            if (string.IsNullOrWhiteSpace(apiKey)) return false;

            string steamId = platformId.ToString();
            string requestUrl =
                $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={Uri.EscapeDataString(apiKey)}&steamids={Uri.EscapeDataString(steamId)}";

            try
            {
                var response = await SteamApiClient.GetAsync(requestUrl);
                if (!response.IsSuccessStatusCode) return false;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("response", out var responseNode)) return false;
                if (!responseNode.TryGetProperty("players", out var playersNode)) return false;
                if (playersNode.ValueKind != System.Text.Json.JsonValueKind.Array || playersNode.GetArrayLength() == 0) return false;

                var player = playersNode[0];
                if (!player.TryGetProperty("gameid", out var gameIdNode)) return false;

                string? gameIdText = gameIdNode.GetString();
                return gameIdText == "480" || gameIdText == "471710";
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> ValidateSteamUserTicketAsync(string ticketPayload, ulong? platformId)
        {
            if (platformId == null || platformId == 0 || string.IsNullOrWhiteSpace(ticketPayload))
                return false;

            string apiKey = ServerConfig.SteamWebApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                return false;

            string? ticketHex = ExtractSteamTicketHex(ticketPayload);
            if (string.IsNullOrWhiteSpace(ticketHex))
                return false;

            var firstAttempt = await AuthenticateSteamUserTicketAsync(apiKey, ticketHex, "471710", platformId);
            if (firstAttempt.isValid)
                return true;

            var secondAttempt = await AuthenticateSteamUserTicketAsync(apiKey, ticketHex, "480", platformId);
            if (secondAttempt.isValid)
                return true;

            if (!firstAttempt.ticketForOtherApp)
                return false;

            return false;
        }

        private static async Task<(bool isValid, bool ticketForOtherApp)> AuthenticateSteamUserTicketAsync(
            string apiKey,
            string ticketHex,
            string appId,
            ulong? expectedSteamId)
        {
            string requestUrl =
                $"https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v1/?key={Uri.EscapeDataString(apiKey)}&appid={Uri.EscapeDataString(appId)}&ticket={Uri.EscapeDataString(ticketHex)}";

            Console.WriteLine($"[AUTH] Verifying ticket URL: {requestUrl}");

            try
            {
                var response = await SteamApiClient.GetAsync(requestUrl);
                if (!response.IsSuccessStatusCode)
                    return (false, false);

                string json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("response", out var responseNode))
                    return (false, false);

                if (responseNode.TryGetProperty("error", out var errorNode)
                    && errorNode.TryGetProperty("errorcode", out var errorCodeNode)
                    && errorCodeNode.TryGetInt32(out int errorCode)
                    && errorCode == 102)
                {
                    return (false, true);
                }

                if (!responseNode.TryGetProperty("params", out var paramsNode))
                    return (false, false);

                if (!paramsNode.TryGetProperty("result", out var resultNode)
                    || !string.Equals(resultNode.GetString(), "OK", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, false);
                }

                if (!paramsNode.TryGetProperty("steamid", out var steamIdNode))
                    return (false, false);

                string? steamIdText = steamIdNode.GetString();
                if (!ulong.TryParse(steamIdText, out ulong ticketSteamId))
                    return (false, false);

                return (ticketSteamId == expectedSteamId, false);
            }
            catch
            {
                return (false, false);
            }
        }

        public static async Task SetAllBirthdays() // temp for now?
        {
            var all = Players.FindAll().ToList();
            foreach (var player in all)
            {
                if (player?.Player == null) continue;
                player.Player.Birthday = new DateTime(2000, 1, 1, 1, 0, 0, DateTimeKind.Utc);
                Players.Update(player);
            }
            await Task.CompletedTask;
        }

        private static string? ExtractSteamTicketHex(string ticketPayload)
        {
            if (string.IsNullOrWhiteSpace(ticketPayload))
                return null;

            string input = ticketPayload.Trim();

            if ((input.StartsWith("'") && input.EndsWith("'"))
                || (input.StartsWith("\"") && input.EndsWith("\"")))
            {
                input = input[1..^1];
            }

            string candidate = input;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(input);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("Ticket", out var ticketNode)
                    && ticketNode.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    candidate = ticketNode.GetString() ?? string.Empty;
                }
            }
            catch
            {
                var match = Regex.Match(input, "\"Ticket\"\\s*:\\s*\"(?<ticket>[0-9a-fA-F]+)\"", RegexOptions.IgnoreCase);
                if (match.Success)
                    candidate = match.Groups["ticket"].Value;
            }

            candidate = candidate.Trim().Trim('\'', '"');

            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            if (!candidate.All(Uri.IsHexDigit))
                return null;

            return candidate;
        }

        private static readonly HttpClient OculusApiClient = new HttpClient();
        private const string OculusNonceValidateUrl = "https://graph.oculus.com/user_nonce_validate";
        private static readonly HashSet<int> OculusIgnoreErrorCodes = new() { 1, 2 };
        private const int OculusMaxAttempts = 3;

        public static async Task<bool> ValidateOculusUserNonceAsync(string? platformAuth, string? platformId)
        {
            if (!ServerConfig.OculusAuth)
            {
                // Console.WriteLine($"[AUTH] ServerConfig oculus off accepting platformId={platformId} unverified");
                return true;
            }
            Console.WriteLine($"[AUTH] Validating oculus platformId={platformId} nonce={platformAuth}");

            string appSecret = ServerConfig.OculusAppSecret;

            if (string.IsNullOrWhiteSpace(platformId) || !platformId.All(char.IsDigit))
            {
                Console.WriteLine($"[AUTH] missing or non numeric oculus platform_id '{platformId}'");
                return false;
            }

            var auth = ExtractOculusNonce(platformAuth);
            if (auth == null)
            {
                Console.WriteLine($"[AUTH] Can't extract platformId={platformId}'s nonce!");
                return false;
            }

            var form = new Dictionary<string, string>
            {
                ["nonce"] = auth.Value.nonce,
                ["user_id"] = platformId,
                ["access_token"] = $"OC|{auth.Value.appId}|{appSecret}"
            };
            Console.WriteLine($"[AUTH] Validating oculus platformId={platformId} nonce={auth.Value.nonce} appId={auth.Value.appId}");

            var last = (isValid: false, retryable: false, reason: "not attempted");

            for (int attempt = 1; attempt <= OculusMaxAttempts; attempt++)
            {
                last = await ValidateOculusNonceOnceAsync(form);
                Console.WriteLine($"[AUTH] Oculus validation attempt {attempt} for platformId={platformId} result: isValid={last.isValid}, retryable={last.retryable}, reason={last.reason}");

                if (last.isValid)
                    return true;

                if (!last.retryable || attempt == OculusMaxAttempts)
                    break;

                await Task.Delay(attempt * attempt * 250);
            }

            Console.WriteLine($"[AUTH] Oculus error probably platformId={platformId}: {last.reason}");
            return false;
        }
        private static async Task<(bool isValid, bool retryable, string reason)> ValidateOculusNonceOnceAsync(Dictionary<string, string> form)
        {
            HttpResponseMessage response;

            try
            {
                response = await OculusApiClient.PostAsync(OculusNonceValidateUrl, new FormUrlEncodedContent(form));
            }
            catch (Exception ex)
            {
                return (false, true, $"request failed: {ex.Message}");
            }

            string json;

            try
            {
                json = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return (false, true, $"reading response failed: {ex.Message}");
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return (false, true, $"HTTP Error {(int)response.StatusCode} (2)");

                if (root.TryGetProperty("error", out var errorNode)
                    && errorNode.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    int? code = errorNode.TryGetProperty("code", out var codeNode)
                        && codeNode.TryGetInt32(out int parsedCode) ? parsedCode : null;

                    string message = errorNode.TryGetProperty("message", out var messageNode)
                        && messageNode.ValueKind == System.Text.Json.JsonValueKind.String
                        ? messageNode.GetString() ?? "no message" : "no message";

                    bool transient = (errorNode.TryGetProperty("is_transient", out var transientNode)
                            && transientNode.ValueKind == System.Text.Json.JsonValueKind.True)
                        || (code != null && OculusIgnoreErrorCodes.Contains(code.Value));

                    return (false, transient, $"graph error {(code?.ToString() ?? "?")}: {message}");
                }

                if (!root.TryGetProperty("is_valid", out var validNode)
                    || validNode.ValueKind != System.Text.Json.JsonValueKind.True)
                {
                    return (false, false, "nonce rejected");
                }
                Console.WriteLine($"[AUTH] Oculus nonce validated successfully for platformId={form["user_id"]} response: {json}");

                return (true, false, string.Empty);
            }
            catch (System.Text.Json.JsonException)
            {
                return (false, true, $"HTTP Error {(int)response.StatusCode}");
            }
        }

        private static (string nonce, string appId)? ExtractOculusNonce(string? platformAuth)
        {
            if (string.IsNullOrWhiteSpace(platformAuth))
                return null;

            string input = platformAuth.Trim();

            string? nonce = null;
            string? appId = null;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(input);
                var root = doc.RootElement;

                if (root.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    using var innerDoc = System.Text.Json.JsonDocument.Parse(root.GetString() ?? string.Empty);
                    (nonce, appId) = ReadOculusAuthFields(innerDoc.RootElement);
                }
                else
                {
                    (nonce, appId) = ReadOculusAuthFields(root);
                }
            }
            catch
            {
                var nonceMatch = Regex.Match(input, "\"Nonce\"\\s*:\\s*\"(?<nonce>[^\"]+)\"", RegexOptions.IgnoreCase);
                var appIdMatch = Regex.Match(input, "\"AppId\"\\s*:\\s*\"?(?<appId>\\d+)\"?", RegexOptions.IgnoreCase);

                if (nonceMatch.Success)
                    nonce = nonceMatch.Groups["nonce"].Value;

                if (appIdMatch.Success)
                    appId = appIdMatch.Groups["appId"].Value;
            }

            if (string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(appId))
                return null;

            if (!appId.All(char.IsDigit))
                return null;

            return (nonce, appId);
        }

        private static (string? nonce, string? appId) ReadOculusAuthFields(System.Text.Json.JsonElement element)
        {
            if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
                return (null, null);

            string? nonce = element.TryGetProperty("Nonce", out var nonceNode)
                && nonceNode.ValueKind == System.Text.Json.JsonValueKind.String
                ? nonceNode.GetString() : null;

            string? appId = null;

            if (element.TryGetProperty("AppId", out var appIdNode))
            {
                appId = appIdNode.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => appIdNode.GetString(),
                    System.Text.Json.JsonValueKind.Number => appIdNode.GetRawText(),
                    _ => null
                };
            }

            return (nonce, appId);
        }

        public static List<MessageData> GetMessages(long playerId)
        {
            var player = Players.FindOne(x => x.PlayerId == playerId);

            if (player == null)
                return new List<MessageData>();

            if (player.Player.PlayerExtra.Messages == null)
                player.Player.PlayerExtra.Messages = new List<MessageData>();

            return player.Player.PlayerExtra.Messages;
        }

        public static MessageData? AddMessage(long toPlayerId, long fromPlayerId, MessageType type, string? data, long? roomId = null, long? playerEventId = null)
        {
            var player = Players.FindOne(x => x.PlayerId == toPlayerId);

            if (player == null)
                return null;

            if (player.Player.PlayerExtra.Messages == null)
                player.Player.PlayerExtra.Messages = new List<MessageData>();

            var msg = new MessageData()
            {
                Id = GenerateRandomMessageId(),
                FromPlayerId = fromPlayerId,
                SentTime = DateTime.UtcNow,
                Type = type,
                Data = data,
                RoomId = roomId,
                PlayerEventId = playerEventId
            };

            player.Player.PlayerExtra.Messages.Add(msg);

            Players.Update(player);

            return msg;
        }

        public static void DeleteMessage(long playerId, long messageId)
        {
            var player = Players.FindOne(x => x.PlayerId == playerId);

            if (player == null)
                return;

            if (player.Player.PlayerExtra.Messages == null || player.Player.PlayerExtra.Messages.Count == 0)
                return;

            var message = player.Player.PlayerExtra.Messages.FirstOrDefault(m => m.Id == messageId);

            if (message == null)
                return;

            player.Player.PlayerExtra.Messages.Remove(message);

            Players.Update(player);
        }

        public static void DeleteMessagesBulk(long playerId, IEnumerable<long> messageIds)
        {
            var player = Players.FindOne(x => x.PlayerId == playerId);
            if (player == null)
                return;
            if (player.Player.PlayerExtra.Messages == null || player.Player.PlayerExtra.Messages.Count == 0)
                return;

            var idsToDelete = new HashSet<long>(messageIds);
            if (idsToDelete.Count == 0)
                return;

            int removed = player.Player.PlayerExtra.Messages.RemoveAll(m => idsToDelete.Contains(m.Id));
            if (removed > 0)
                Players.Update(player);
        }

        public static void StoreLoginDeviceInfo(long playerId, DeviceClasses? deviceClass, int? ver)
        {
            var player = Players.FindById(playerId);
            if (player?.Player?.PlayerExtra == null) return;

            player.Player.PlayerExtra.Heartbeat ??= new Heartbeat();
            player.Player.PlayerExtra.Heartbeat.deviceClass = deviceClass ?? DeviceClasses.Unknown;
            //Console.WriteLine($"[PlayerDB] Outcome of if statement: ${ver.HasValue} and ${ver?.ToString()}");
            player.Player.PlayerExtra.Heartbeat.appVersion = ver.HasValue ? ver.Value.ToString() : ServerConfig.GameVersion.ToString();
            //Console.WriteLine($"[PlayerDB] Stored login device info for player {playerId}: deviceClass={player.Player.PlayerExtra.Heartbeat.deviceClass}, appVersion={player.Player.PlayerExtra.Heartbeat.appVersion}");

            Players.Update(player);
        }

        private static readonly Random random = new Random();
        private static readonly object randLock = new object();

        private static long MakeNewPlayerID()
        {
            long id;
            do
            {
                lock (randLock)
                    id = random.NextInt64(1, 2147483648L);
            } while (Players.FindById(id) != null);
            return id;
        }

        public static long GenerateRandomMessageId()
        {
            lock (randLock)
            {
                byte[] buffer = new byte[8];
                random.NextBytes(buffer);
                return BitConverter.ToInt64(buffer, 0);
            }
        }



        public static class PasswordManager // [PasswordManager] if it was in rec room bruh
        {
            public static (bool valid, string? error) ValidatePassword(string password)
            {
                if (password.Length < 8)
                    return (false, "Your password cannot be under 8 characters.");

                if (!password.Any(char.IsUpper))
                    return (false, "Password must contain a capital letter.");

                if (!Regex.IsMatch(password, @"[!@#$%^&*()\-_=+\[\]{};':""|,.<>/?`~]"))
                    return (false, "Password must contain a symbol.");

                if (password.Count(char.IsDigit) < 2)
                    return (false, "Password must contain 2 numbers minimum.");

                return (true, null);
            }

            public static string HashPassword(string password)
            {
                byte[] salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
                var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256);
                byte[] hash = pbkdf2.GetBytes(32);
                byte[] combined = new byte[48];
                salt.CopyTo(combined, 0);
                hash.CopyTo(combined, 16);
                return Convert.ToBase64String(combined);
            }

            public static bool VerifyPassword(string password, string storedHash)
            {
                byte[] combined = Convert.FromBase64String(storedHash);
                byte[] salt = combined[..16];
                byte[] storedBytes = combined[16..];
                var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256);
                byte[] hash = pbkdf2.GetBytes(32);
                return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hash, storedBytes);
            }

            public static bool ChangePassword(long playerId, string newPassword)
            {
                var player = Players.FindById(playerId);
                if (player == null) return false;

                player.Password = HashPassword(newPassword);
                return Players.Update(player);
            }
        }
    }
}