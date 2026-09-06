using LiteDB;
using Vanadium.Classes;
using Vanadium.Classes.DBs.DBClasses;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static Vanadium.Classes.DBs.DBClasses.PhotonAccessTokenDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using static Vanadium.Classes.DBs.DBClasses.RoomDBClasses;

namespace Vanadium.Classes.DBs
{
    public class RoomDB
    {
        public static LiteDatabase RoomDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Rooms.db") + ";Connection=shared");
        public static readonly ILiteCollection<Room> Rooms = RoomDBFile.GetCollection<Room>("Rooms");
        public static readonly ILiteCollection<currentSave> SubRoomSaves = RoomDBFile.GetCollection<currentSave>("SubRoomSaves");
        public static readonly ILiteCollection<DbCounters> Counters = RoomDBFile.GetCollection<DbCounters>("Counters");

        public static void Setup()
        {
            Rooms.EnsureIndex(x => x.RoomId);
            Rooms.EnsureIndex(x => x.CreatorAccountId);
            Rooms.EnsureIndex(x => x.Name);
            Rooms.EnsureIndex("TagIndex", "$.Tags[*].Tag");
            Counters.EnsureIndex(x => x.Key);
            SubRoomSaves.EnsureIndex(x => x.SubRoomDataSaveId);
            SubRoomSaves.EnsureIndex(x => x.RoomId);
            SubRoomSaves.EnsureIndex(x => x.SubRoomId);
        }

        public static async Task ImportRooms(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[DB] No import file found at {path}, skipping room import.");
                return;
            }
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var importedRooms = System.Text.Json.JsonSerializer.Deserialize<List<Room>>(json);
                if (importedRooms != null)
                {
                    foreach (var room in importedRooms)
                        await AddRoom(room, log: true, shouldAssignNewIds: true);
                    
                    Console.WriteLine($"[DB] Imported {importedRooms.Count} rooms from {path}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] Failed to import rooms from {path}: {ex.Message}");
            }
        }

        public static Room? SetRoomSubroomRoomFileAgain(
        long roomId,
        long subRoomId,
        string roomFile,
        string roomDesc,
        long playerId,
        int platform = 0,
        int deviceClass = 0)
        {
            var room = Rooms.FindById(roomId);
            if (room == null)
                return null;

            var subRoom = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
            if (subRoom == null)
                return room;

            subRoom.DataBlob = roomFile;
            subRoom.SavedByAccountId = playerId;

            var existingUnityAssetId = !string.IsNullOrEmpty(subRoom.CurrentSave?.UnityAssetId)
                ? subRoom.CurrentSave.UnityAssetId
                : null;

            var save = new currentSave
            {
                SubRoomDataSaveId = GetNextSubRoomSaveId(),
                RoomId = roomId,
                SubRoomId = subRoomId,
                DataBlob = roomFile,
                Description = roomDesc ?? "",
                SavedByAccountId = playerId,
                SavedOnPlatform = platform,
                SavedOnDeviceClass = deviceClass,
                UnityAssetId = existingUnityAssetId,
                CreatedAt = DateTime.UtcNow
            };

            SubRoomSaves.Insert(save);
            Rooms.Update(room);
            return room;
        }

        public static void ExportRooms()
        {
            var allRooms = Rooms.FindAll().ToList();
            var saves = SubRoomSaves.FindAll().ToList();

            foreach (var room in allRooms)
            {
                foreach (var sub in room.SubRooms ?? new List<SubRooms>())
                {
                }
            }

            string outputPath = Path.Combine(Program.dataDir, "exported_rooms.json");
            File.WriteAllText(outputPath, System.Text.Json.JsonSerializer.Serialize(allRooms));
            Console.WriteLine($"[DB] Exported {allRooms.Count} rooms → {outputPath}");
        }
        
        public static List<Room> GetRoomsByIds(List<long> ids)
		{
    		if (ids == null || ids.Count == 0) return new List<Room>();
    		return Rooms.Find(room => ids.Contains(room.RoomId)).ToList();
		}

        public static async Task ClearRooms(bool log = false)
        {
            await Task.Run(() =>
            {
                try
                {
                    var dormRooms = Rooms.Find(r => r.IsDorm).ToList();
                    Rooms.DeleteAll();
                    foreach (var dorm in dormRooms)
                        Rooms.Insert(dorm);
                    if (log)
                        Console.WriteLine($"[DB] Cleared rooms, preserved {dormRooms.Count} dorm rooms.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB Error] Failed to clear rooms: {ex.Message}");
                }
            });
        }

        public static async Task AddRoom(Room newRoom, bool log = false, bool shouldAssignNewIds = true)
        {
            if (newRoom == null) return;

            await Task.Run(() =>
            {
                try
                {
                    if (shouldAssignNewIds || newRoom.RoomId <= 0)
                        newRoom.RoomId = GetNextRoomId();

                    newRoom.RankedEntityId = newRoom.RoomId.ToString();

                    if (newRoom.Stats == null)
                        newRoom.Stats = new Stats();

                    if (newRoom.LoadScreens == null)
                        newRoom.LoadScreens = new List<LoadScreens>();

                    if (newRoom.SubRooms != null && newRoom.SubRooms.Any())
                    {
                        foreach (var sub in newRoom.SubRooms)
                        {
                            if (shouldAssignNewIds || sub.SubRoomId <= 0)
                                sub.SubRoomId = GetNextSubRoomId();
                            sub.RoomId = newRoom.RoomId;
                        }
                    }

                    Rooms.Insert(newRoom);

                    if (log)
                        Console.WriteLine($"[DB] Added room: {newRoom.Name} (ID: {newRoom.RoomId})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB Error] Failed to add room {newRoom.Name}: {ex.Message}");
                }
            });
        }

        public static Room? MoveSubRoomToRoom(
            long fromRoomId,
            long subRoomId,
            long targetRoomId,
            long playerId)
        {
            var sourceRoom = Rooms.FindById(fromRoomId);
            var targetRoom = Rooms.FindById(targetRoomId);

            if (sourceRoom == null || targetRoom == null)
                return null;

            var subRoom = sourceRoom.SubRooms?
                .FirstOrDefault(s => s.SubRoomId == subRoomId);

            if (subRoom == null)
                return null;

            bool canEditSource = UserCanEditRoom(fromRoomId, playerId);
            bool canEditTarget = UserCanEditRoom(targetRoomId, playerId);

            if (!canEditSource || !canEditTarget)
                return null;

            targetRoom.SubRooms ??= new List<SubRooms>();

            long oldSubRoomId = subRoom.SubRoomId;
            long newSubRoomId = GetNextSubRoomId();

            var json = System.Text.Json.JsonSerializer.Serialize(subRoom);
            var movedSubRoom = System.Text.Json.JsonSerializer.Deserialize<SubRooms>(json);

            if (movedSubRoom == null)
                return null;

            movedSubRoom.SubRoomId = newSubRoomId;
            movedSubRoom.RoomId = targetRoomId;

            targetRoom.SubRooms.Add(movedSubRoom);

            var saves = SubRoomSaves.Find(s =>
                s.RoomId == fromRoomId &&
                s.SubRoomId == oldSubRoomId).ToList();

            foreach (var save in saves)
            {
                save.RoomId = targetRoomId;
                save.SubRoomId = newSubRoomId;

                SubRoomSaves.Update(save);
            }

            sourceRoom.SubRooms.Remove(subRoom);

            Rooms.Update(sourceRoom);
            Rooms.Update(targetRoom);

            return targetRoom;
        }

        public static long AddRoomSync(Room newRoom, bool shouldAssignNewIds = true)
        {
            if (newRoom == null) return 0;

            if (shouldAssignNewIds || newRoom.RoomId <= 0)
                newRoom.RoomId = GetNextRoomId();

            newRoom.RankedEntityId = newRoom.RoomId.ToString();

            if (newRoom.Stats == null) newRoom.Stats = new Stats();
            if (newRoom.LoadScreens == null) newRoom.LoadScreens = new List<LoadScreens>();

            if (newRoom.SubRooms != null && newRoom.SubRooms.Any())
            {
                foreach (var sub in newRoom.SubRooms)
                {
                    if (shouldAssignNewIds || sub.SubRoomId <= 0)
                        sub.SubRoomId = GetNextSubRoomId();
                    sub.RoomId = newRoom.RoomId;
                }
            }

            Rooms.Insert(newRoom);
            Console.WriteLine($"[DB] Added room: {newRoom.Name} (ID: {newRoom.RoomId})");
            return newRoom.RoomId;
        }

        public static long GetNextRoomId()
        {
            long id;
            do
            {
                id = Random.Shared.NextInt64(100_000_000_000L, long.MaxValue);
            } while (Rooms.FindById(id) != null);
            return id;
        }

        public static long GetNextSubRoomId()
        {
            long id;
            do
            {
                id = Random.Shared.NextInt64(100_000_000_000L, long.MaxValue);
            } while (Rooms.FindAll().Any(r => r.SubRooms != null && r.SubRooms.Any(s => s.SubRoomId == id)));
            return id;
        }

        public static long GetNextSubRoomSaveId()
        {
            const long saveIdCeiling = int.MaxValue / 16;
            long id;
            do
            {
                id = Random.Shared.NextInt64(1, saveIdCeiling);
            } while (SubRoomSaves.FindOne(s => s.SubRoomDataSaveId == id) != null);
            return id;
        }

        public static Room? GetRoom(long roomId)
        {
            var col = RoomDBFile.GetCollection("Rooms");
            var doc = col.FindById(new BsonValue(roomId));
            if (doc == null) return null;
            try
            {
                return BsonMapper.Global.Deserialize<Room>(doc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Skipped corrupt room doc {roomId}: {ex.Message}");
                return null;
            }
        }

        public static Room GetRoomByName(string name)
            => Rooms.FindOne(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public static List<Room> GetRoomsByNames(List<string> names)
        {
            if (names == null || names.Count == 0) return new List<Room>();
            return Rooms.Find(room => names.Contains(room.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        }

		public static List<Room> GetRoomsByCreator(long playerId)
		{
		  var col = RoomDBFile.GetCollection("Rooms");
		  var results = new List<Room>();
		  foreach (var doc in col.FindAll())
		  {
		    try
		    {
		      var room = BsonMapper.Global.Deserialize<Room>(doc);
		      if (room == null)
		        continue;
		      if (room.Accessibility != RoomAccessibility.Public)
		        continue;

		      bool isCreator = room.CreatorAccountId == playerId;
		      bool hasOwnerRole = room.Roles?.Any(r =>
		        r.AccountId == playerId &&
		        (r.Role == Role.Creator ||
		         r.Role == Role.Host ||
		         r.Role == Role.CoOwner ||
		         r.Role == Role.TemporaryCoOwner)
		      ) ?? false;

		      if (!isCreator && !hasOwnerRole)
		        continue;

		      results.Add(room);
		    }
		    catch (Exception ex)
		    {
		      Console.WriteLine($"[oh shittinhs] Skipped corrupt room doc {doc["_id"]}: {ex.Message}");
		    }
		  }
		  return results;
		}

        public static long GetAllRoomsCount() => Rooms.Count();

        public static bool DoesRoomExist(long roomId) => Rooms.FindById(new BsonValue(roomId)) != null;

        public static (List<Room> Results, int Total) GetHotRooms(string? tag, int skip, int take, bool risingSort = false)
        {
            var col = RoomDBFile.GetCollection("Rooms");
            string t = tag?.ToLowerInvariant() ?? "";
            bool hasTag = !string.IsNullOrWhiteSpace(t);
            bool isRRO = (t == "rro" || t == "recroomoriginal");
            var results = new List<Room>();

            foreach (var doc in col.FindAll())
            {
                try
                {
                    var room = BsonMapper.Global.Deserialize<Room>(doc);
                    if (room == null) continue;
                    if (room.IsDorm) continue;
                    if (room.Accessibility != RoomAccessibility.Public) continue;

                    if (!hasTag)
                    {
                        bool isRoomRRO = room.Tags != null &&
                            room.Tags.Any(x =>
                                x.Tag.Equals("rro", StringComparison.OrdinalIgnoreCase) ||
                                x.Tag.Equals("recroomoriginal", StringComparison.OrdinalIgnoreCase));

                        if (isRoomRRO) continue;
                    }
                    else
                    {
                        if (isRRO)
                        {
                            bool hasRRO = room.Tags != null &&
                                room.Tags.Any(x =>
                                    x.Tag.Equals("rro", StringComparison.OrdinalIgnoreCase) ||
                                    x.Tag.Equals("recroomoriginal", StringComparison.OrdinalIgnoreCase));

                            if (!hasRRO) continue;
                        }
                    }

                    results.Add(room);
                }
                catch
                {
                }
            }

            IEnumerable<Room> ordered;
            if (risingSort)
            {
                ordered = results
                    .Where(r => r.Stats?.CheerCount >= 5)
                    .OrderByDescending(r => r.CreatedAt);
            }
            else if (t == "new")
            {
                ordered = results.OrderByDescending(r => r.CreatedAt);
            }
            else if (!hasTag)
            {
                ordered = results.OrderByDescending(r => r.Stats?.CheerCount ?? 0);
            }
            else
            {
                ordered = results.OrderByDescending(r => r.Stats?.VisitCount ?? 0);
            }

            var finalResults = ordered.Skip(skip).Take(take).ToList();
            return (finalResults, results.Count);
        }

        public static List<Room> GetBaseRooms()
        {
            var col = RoomDBFile.GetCollection("Rooms");
            var results = new List<Room>();

            foreach (var doc in col.FindAll())
            {
                try
                {
                    var room = BsonMapper.Global.Deserialize<Room>(doc);
                    
                    if (room == null) 
                        continue;

                    if (!room.IsDorm && room.Tags != null && room.Tags.Any(t => t.Tag == "base"))
                        results.Add(room);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB] Skipped corrupt room doc {doc["_id"]}: {ex.Message}");
                }
            }

            return results;
        }
        
        public static (List<Room> Results, int Total) Search(string query, bool isBase = false, int skip = 0, int take = 30)
		{
			if (string.IsNullOrWhiteSpace(query))
				return (new List<Room>(), 0);

			var segments = query.Split('|', StringSplitOptions.RemoveEmptyEntries)
				.Select(s => s.Trim())
				.Where(s => !string.IsNullOrWhiteSpace(s))
				.ToList();

			var candidatePool = Rooms.FindAll()
				.Where(r => !r.IsDorm)
				.Where(r => !r.ExcludeFromSearch)
				.Where(r => isBase || r.Accessibility == RoomAccessibility.Public)
				.ToList();

			var allPlayers = PlayerDB.Players.FindAll().ToList();

			var matched = new Dictionary<long, Room>();

			foreach (var segment in segments)
			{
				var terms = segment.Split(new char[] { ' ', '+' }, StringSplitOptions.RemoveEmptyEntries);

				var nameParts = new List<string>();
				var tagParts = new List<string>();
				var authorParts = new List<string>();

				foreach (var term in terms)
				{
					if (term.StartsWith("#"))
						tagParts.Add(term.Substring(1).ToLowerInvariant().Trim());
					else if (term.StartsWith("@"))
						authorParts.Add(term.Substring(1).ToLowerInvariant().Trim());
					else
						nameParts.Add(term.ToLowerInvariant().Trim());
				}

				long? resolvedAuthorId = null;

				if (authorParts.Count > 0)
				{
					var authorQuery = string.Join(" ", authorParts);

					var exactMatch = allPlayers.FirstOrDefault(p =>
						string.Equals(p.Player?.Username, authorQuery, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(p.Player?.DisplayName, authorQuery, StringComparison.OrdinalIgnoreCase));

					if (exactMatch != null)
					{
						resolvedAuthorId = exactMatch.PlayerId;
					}
					else
					{
						var fuzzyMatch = allPlayers
							.Where(p => p.Player != null)
							.Select(p => new
							{
								Player = p,
								Score = Math.Max(
									ScoreShit((p.Player!.Username ?? "").ToLowerInvariant(), authorQuery),
									ScoreShit((p.Player!.DisplayName ?? "").ToLowerInvariant(), authorQuery))
							})
							.Where(x => x.Score > 0)
							.OrderByDescending(x => x.Score)
							.FirstOrDefault();

						if (fuzzyMatch != null)
							resolvedAuthorId = fuzzyMatch.Player.PlayerId;
						else
							continue;
					}
				}

				foreach (var room in candidatePool)
				{
					if (resolvedAuthorId.HasValue && room.CreatorAccountId != resolvedAuthorId.Value)
						continue;

					if (tagParts.Count > 0)
					{
						var roomTags = (room.Tags ?? new List<Tags>())
							.Select(t => t.Tag.ToLowerInvariant().Trim())
							.ToList();

						bool allTagsMatch = tagParts.All(tp =>
						{
							if (roomTags.Contains(tp))
								return true;

							foreach (var rt in roomTags)
							{
								if (rt.Contains(tp) || tp.Contains(rt))
									return true;

								int dist = EditDistance(rt, tp);
								int maxLen = Math.Max(rt.Length, tp.Length);
								if (maxLen > 0 && (double)dist / maxLen <= 0.34)
									return true;
							}

							return false;
						});

						if (!allTagsMatch) continue;
					}

					if (nameParts.Count > 0)
					{
						string roomNameLower = (room.Name ?? "").ToLowerInvariant();

						bool nameMatch = nameParts.All(np =>
						{
							if (roomNameLower.Contains(np))
								return true;

							int dist = EditDistance(roomNameLower, np);
							int maxLen = Math.Max(roomNameLower.Length, np.Length);
							if (maxLen > 0 && (double)dist / maxLen <= 0.34)
								return true;

							return false;
						});

						if (!nameMatch) continue;
					}

					matched[room.RoomId] = room;
				}
			}

			var all = matched.Values
				.OrderByDescending(r => r.Stats?.CheerCount ?? 0)
				.ThenByDescending(r => r.Stats?.VisitCount ?? 0)
				.ToList();

			return (all.Skip(skip).Take(take).ToList(), all.Count);
		}

		private static int ScoreShit(string source, string target)
		{
			if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0;
			if (source == target) return 1000;
			if (source.Contains(target) || target.Contains(source)) return 500;

			int dist = EditDistance(source, target);
			int maxLen = Math.Max(source.Length, target.Length);
			if (maxLen == 0) return 0;

			double similarity = 1.0 - (double)dist / maxLen;
			if (similarity >= 0.66) return (int)(similarity * 100);

			return 0;
		}

		private static int EditDistance(string a, string b)
		{
			if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
			if (string.IsNullOrEmpty(b)) return a.Length;

			int m = a.Length, n = b.Length;
			var dp = new int[m + 1, n + 1];

			for (int i = 0; i <= m; i++) dp[i, 0] = i;
			for (int j = 0; j <= n; j++) dp[0, j] = j;

			for (int i = 1; i <= m; i++)
			{
				for (int j = 1; j <= n; j++)
				{
					dp[i, j] = a[i - 1] == b[j - 1]
						? dp[i - 1, j - 1]
						: 1 + Math.Min(dp[i - 1, j - 1], Math.Min(dp[i - 1, j], dp[i, j - 1]));
				}
			}

			return dp[m, n];
		}

        public static Room? SetRoomName(long roomId, string roomName)
        {
            var existing = Rooms.FindOne(x => x.Name == roomName && x.RoomId != roomId);
            if (existing != null) return null;

            var room = Rooms.FindById(roomId);
            if (room == null) return null;
            room.Name = roomName;
            Rooms.Update(room);
            return room;
        }

        public static Room? SetRoomDescription(long roomId, string description)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;
            room.Description = description;
            Rooms.Update(room);
            return room;
        }

        public static Room? SetRoomImageName(long roomId, string imageName)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;
            room.ImageName = imageName;
            Rooms.Update(room);
            return room;
        }

        public static Room? SetRoomAccessibility(long roomId, RoomAccessibility accessibility)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;
            room.Accessibility = accessibility;
            Rooms.Update(room);
            return room;
        }

        public static Room? SetRoomCloning(long roomId, bool cloningEnabled)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;
            room.CloningAllowed = cloningEnabled;
            Rooms.Update(room);
            return room;
        }

        public static Room? SetRoomRestrictions(long roomId, bool supportsScreens, bool supportsWalkVR, bool supportsTeleportVR, bool supportsJuniors)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;
            room.SupportsScreens = supportsScreens;
            room.SupportsWalkVR = supportsWalkVR;
            room.SupportsTeleportVR = supportsTeleportVR;
            room.SupportsJuniors = supportsJuniors;
            Rooms.Update(room);
            return room;
        }

        public static Room? SetRoomLoadscreen(long roomId, string? imageName, string? title, string? subtitle)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;
            room.LoadScreens = new List<LoadScreens>
            {
                new LoadScreens
                {
                    ImageName = imageName ?? "",
                    Title = title ?? "",
                    Subtitle = subtitle ?? ""
                }
            };
            Rooms.Update(room);
            return room;
        }

        public static Room? SetRoomWarningMask(long roomId, WarningMaskType warningMask)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;
            room.WarningMask = warningMask;
            Rooms.Update(room);
            return room;
        }
        
        public static Room? SetRoomCustomWarning(long roomId, string? customWarning)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;
            room.CustomWarning = customWarning;
            Rooms.Update(room);
            return room;
        }

        public static Room? SetRoomTags(List<string> tags, long roomId, int type = 0)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;

            var newTags = tags
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();

            if (room.Tags == null) room.Tags = new List<Tags>();


            foreach (var tag in newTags)
			{
    			var existing = room.Tags.FirstOrDefault(t => t.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
    			if (existing != null)
        			existing.Type = (TagType)type;
    			else
        			room.Tags.Add(new Tags { Tag = tag, Type = (TagType)type });
			}

            Rooms.Update(room);
            return room;
        }
        
        public static Room? SetRoomTagsMixed(List<RoomDBClasses.Tags> incomingTags, long roomId)
		{
			var room = Rooms.FindById(roomId);
			if (room == null) return null;

			room.Tags ??= new List<Tags>();

			bool autoTagOnly = incomingTags.Count == 1 && incomingTags[0].Type == TagType.Auto;

			foreach (var incoming in incomingTags)
			{
				var tag = incoming.Tag.Trim().ToLowerInvariant();
				if (string.IsNullOrWhiteSpace(tag)) continue;

				var existing = room.Tags.FirstOrDefault(t => t.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));

				if (incoming.Type == TagType.Auto && !autoTagOnly)
				{
					if (existing == null)
						room.Tags.Add(new Tags { Tag = tag, Type = incoming.Type });
					continue;
				}

				if (existing != null)
					room.Tags.Remove(existing);
				else
					room.Tags.Add(new Tags { Tag = tag, Type = incoming.Type });
			}

			Rooms.Update(room);
			return room;
		}

        public static Room? SetRoomSubroomRoomFile(
            long roomId, long subRoomId,
            string dataBlob, string description,
            long playerId, int platform, int deviceClass)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;

            var subRoom = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
            if (subRoom == null) return room;

            subRoom.DataBlob = dataBlob;
            subRoom.SavedByAccountId = playerId;

            var save = new currentSave
            {
                SubRoomDataSaveId = GetNextSubRoomSaveId(),
                RoomId = roomId,
                SubRoomId = subRoomId,
                DataBlob = dataBlob,
                Description = description ?? "",
                SavedByAccountId = playerId,
                SavedOnPlatform = platform,
                SavedOnDeviceClass = deviceClass,
                CreatedAt = DateTime.UtcNow
            };

            SubRoomSaves.Insert(save);
            Rooms.Update(room);
            return room;
        }

        public static (List<currentSave> Results, int TotalResults) GetSubRoomSaves(
            long roomId, long subRoomId, int skip, int take, string? search = null)
        {
            take = Math.Min(take, 9999);

            var query = SubRoomSaves.FindAll()
                .Where(s => s.RoomId == roomId && s.SubRoomId == subRoomId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.ToLowerInvariant();
                query = query.Where(s =>
                    (s.DataBlob ?? "").ToLower().Contains(q) ||
                    (s.Description ?? "").ToLower().Contains(q) ||
                    (s.Tags != null && s.Tags.Any(t => t.ToLower().Contains(q)))
                );
            }

            var ordered = query.OrderByDescending(s => s.CreatedAt).ToList();

            return (ordered.Skip(skip).Take(take).ToList(), ordered.Count);
        }

        public static currentSave? GetSubRoomSaveById(long roomId, long subRoomId, long saveId)
            => SubRoomSaves.FindOne(s => s.RoomId == roomId && s.SubRoomId == subRoomId && s.SubRoomDataSaveId == saveId);

        public static Room? CreateSubroomForRoom(long roomId, string subroomName)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;

            if (room.SubRooms == null) room.SubRooms = new List<SubRooms>();

            var lastSub = room.SubRooms.OrderByDescending(s => s.SubRoomId).FirstOrDefault();

            var newSub = new SubRooms
            {
                RoomId = roomId,
                Name = subroomName,
                SubRoomId = GetNextSubRoomId(),
                Accessibility = RoomAccessibility.Public,
                UnitySceneId = "a75f7547-79eb-47c6-8986-6767abcb4f92",
                MaxPlayers = 20
            };

            room.SubRooms.Add(newSub);
            Rooms.Update(room);
            return room;
        }

        public static bool DeleteSubroom(long roomId, long subRoomId)
		{
				var room = Rooms.FindById(roomId);
				if (room == null) return false;

				var sub = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
				if (sub == null) return false;

				room.SubRooms.Remove(sub);

				if (room.SubRooms.Count < 1)
				{
						SubRoomSaves.DeleteMany(s => s.RoomId == roomId);
						Rooms.Delete(roomId);
						return true;
				}

				Rooms.Update(room);
				return true;
		}

        public static bool ModifySubroom(long roomId, long subRoomId, string name, RoomAccessibility accessibility, int maxPlayers)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return false;

            var sub = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
            if (sub == null) return false;

            sub.Name = name;
            sub.Accessibility = accessibility;
            sub.MaxPlayers = maxPlayers;
            Rooms.Update(room);
            return true;
        }

        public static void IncrementRoomVisitCount(long roomId)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return;
            if (room.Stats == null) room.Stats = new Stats();
            room.Stats.VisitCount++;
            Rooms.Update(room);
        }

        public static void IncrementRoomCheerCount(long roomId)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return;
            room.Stats.CheerCount++;
            Rooms.Update(room);
        }

        public static void DecrementRoomCheerCount(long roomId)
        {
            var room = Rooms.FindById(roomId);
            if (room == null || room.Stats.CheerCount <= 0) return;
            room.Stats.CheerCount--;
            Rooms.Update(room);
        }

        public static void IncrementRoomFavoriteCount(long roomId)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return;
            room.Stats.FavoriteCount++;
            Rooms.Update(room);
        }

        public static void DecrementRoomFavoriteCount(long roomId)
        {
            var room = Rooms.FindById(roomId);
            if (room == null || room.Stats.FavoriteCount <= 0) return;
            room.Stats.FavoriteCount--;
            Rooms.Update(room);
        }

        public static bool UserCanEditRoom(long roomId, long accountId, bool allowCoOwner = true)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return false;
            if (room.CreatorAccountId == accountId) return true;

            if (allowCoOwner) {
                return room.Roles?.Any(r =>
                    r.AccountId == accountId &&
                    (r.Role == Role.CoOwner || r.Role == Role.Creator || r.Role == Role.TemporaryCoOwner)
                ) ?? false;
            } else {
                return room.Roles?.Any(r =>
                    r.AccountId == accountId &&
                    r.Role == Role.Creator
                ) ?? false;
            }
        }

        public static bool AddRoleToRoom(long roomId, long accountId, Role role, Role invitedRole = Role.None)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return false;

            room.Roles ??= new List<Roles>();
            room.Roles.Add(new Roles { AccountId = accountId, Role = role, InvitedRole = invitedRole });
            Rooms.Update(room);
            return true;
        }

        public static bool RemoveRoleFromRoom(long roomId, long accountId)
        {
            var room = Rooms.FindById(roomId);
            if (room == null || room.Roles == null) return false;
            room.Roles.RemoveAll(r => r.AccountId == accountId);
            Rooms.Update(room);
            return true;
        }

        public static long? CloneRoom(long roomId, string roomName, long playerId)
        {
            var original = GetRoom(roomId);
            if (original == null)
                return null;

            if (original.Name == "DormRoom")
                return null;

            bool isOwner = original.CreatorAccountId == playerId;

            bool isCoOwner = original.Roles?.Any(r =>
                r.AccountId == playerId &&
                (r.Role == Role.CoOwner || r.Role == Role.Creator)
            ) ?? false;

            bool canClone = original.CloningAllowed;

            bool isBaseRoom = original.Tags != null &&
                original.Tags.Any(t => string.Equals(t.Tag, "base", StringComparison.OrdinalIgnoreCase));

            bool isRRO = original.Tags != null &&
                original.Tags.Any(t =>
                    string.Equals(t.Tag, "rro", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Tag, "recroomoriginal", StringComparison.OrdinalIgnoreCase));

            if (isRRO && !isBaseRoom && !canClone)
                return null;

            if (!isBaseRoom && !isOwner && !isCoOwner && !canClone)
                return null;

            var originalSubroomOrder = (original.SubRooms ?? new List<SubRooms>())
                .Select(s => s.SubRoomId)
                .ToList();

            var latestSavePerSubroom = new Dictionary<long, currentSave>();
            foreach (var origSubId in originalSubroomOrder)
            {
                var latestSave = SubRoomSaves
                    .Find(s => s.RoomId == roomId && s.SubRoomId == origSubId)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefault();
                if (latestSave != null)
                    latestSavePerSubroom[origSubId] = latestSave;
            }

            var json = System.Text.Json.JsonSerializer.Serialize(original);
            var clone = System.Text.Json.JsonSerializer.Deserialize<Room>(json);

            if (clone == null)
                return null;

            clone.RoomId = 0;
            clone.Name = roomName;
            clone.CreatedAt = DateTime.UtcNow;
            clone.ImageName = "DefaultRoomImage.jpg";
            clone.Accessibility = RoomAccessibility.Private;
            clone.CloningAllowed = false;
            clone.IsRRO = false;
            clone.IsDeveloperOwned = false;
            clone.IsDorm = false;
            clone.PromoImages = new List<string>();
            clone.Stats = new Stats();
            clone.CreatorAccountId = playerId;

            clone.Tags = (clone.Tags ?? new List<Tags>())
                .Where(t =>
                    !string.Equals(t.Tag, "base", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(t.Tag, "rro", StringComparison.OrdinalIgnoreCase))
                .ToList();

            clone.Roles = new List<Roles>
            {
                new Roles
                {
                    AccountId = playerId,
                    Role = Role.Creator,
                    InvitedRole = Role.Creator
                }
            };

            if (clone.SubRooms != null)
            {
                foreach (var sub in clone.SubRooms)
                {
                    sub.SubRoomId = GetNextSubRoomId();
                    sub.RoomId = clone.RoomId;
                    sub.SavedByAccountId = playerId;
                    if (sub.CreatorAccountId != null)
                        sub.CreatorAccountId = playerId;
                }
            }

            long newRoomId = AddRoomSync(clone, shouldAssignNewIds: false);

            var insertedRoom = GetRoom(newRoomId);

            if (insertedRoom?.SubRooms != null)
            {
                foreach (var sub in insertedRoom.SubRooms)
                    sub.RoomId = newRoomId;

                Rooms.Update(insertedRoom);
            }

            var clonedSubrooms = insertedRoom?.SubRooms ?? new List<SubRooms>();

            for (int i = 0; i < originalSubroomOrder.Count && i < clonedSubrooms.Count; i++)
            {
                long originalSubroomId = originalSubroomOrder[i];
                long clonedSubroomId = clonedSubrooms[i].SubRoomId;

                if (latestSavePerSubroom.TryGetValue(originalSubroomId, out var latestOriginalSave))
                {
                    var clonedSave = new currentSave
                    {
                        SubRoomDataSaveId = GetNextSubRoomSaveId(),
                        RoomId = newRoomId,
                        SubRoomId = clonedSubroomId,
                        DataBlob = latestOriginalSave.DataBlob,
                        DataBlobHash = latestOriginalSave.DataBlobHash,
                        ReferencedUnityAssetIds = latestOriginalSave.ReferencedUnityAssetIds != null
                            ? new List<string>(latestOriginalSave.ReferencedUnityAssetIds)
                            : new List<string>(),
                        UnitySubAssets = latestOriginalSave.UnitySubAssets != null
                            ? new List<string>(latestOriginalSave.UnitySubAssets)
                            : new List<string>(),
                        ReferencedUnityAssets = latestOriginalSave.ReferencedUnityAssets != null
                            ? new List<string>(latestOriginalSave.ReferencedUnityAssets)
                            : new List<string>(),
                        UnityAssetId = latestOriginalSave.UnityAssetId,
                        PersistenceVersion = latestOriginalSave.PersistenceVersion,
                        OMVersion = latestOriginalSave.OMVersion,
                        UgcSubVersion = latestOriginalSave.UgcSubVersion,
                        SavedByAccountId = playerId,
                        SavedOnPlatform = latestOriginalSave.SavedOnPlatform,
                        SavedOnDeviceClass = latestOriginalSave.SavedOnDeviceClass,
                        Description = $"Cloned from room ^{original.Name}",
                        Tags = latestOriginalSave.Tags != null
                            ? new List<string>(latestOriginalSave.Tags)
                            : new List<string>(),
                        ModerationState = latestOriginalSave.ModerationState,
                        CreatedAt = DateTime.UtcNow
                    };

                    SubRoomSaves.Insert(clonedSave);

                    var room = Rooms.FindById(newRoomId);
                    var sub = room?.SubRooms?.FirstOrDefault(s => s.SubRoomId == clonedSubroomId);

                    if (sub != null)
                    {
                        sub.DataBlob = clonedSave.DataBlob;
                        sub.SavedByAccountId = playerId;
                        sub.CurrentSave = clonedSave;
                        Rooms.Update(room);
                    }
                }
            }

            for (int i = 0; i < originalSubroomOrder.Count && i < clonedSubrooms.Count; i++)
            {
                long originalSubroomId = originalSubroomOrder[i];
                long clonedSubroomId = clonedSubrooms[i].SubRoomId;

                var originalPermissions = PhotonAccessTokenDB.GetPermissions(roomId, originalSubroomId);
                if (originalPermissions != null && originalPermissions.Permissions.Count > 0)
                {
                    var copiedEntries = originalPermissions.Permissions.Select(p => new PhotonAccessTokenDBClasses.StoredPermissionEntry
                    {
                        Permission = p.Permission,
                        Role = p.Role,
                        Type = p.Type,
                        Override = p.Override,
                        Value = p.Value
                    }).ToList();

                    PhotonAccessTokenDB.SetPermissions(newRoomId, clonedSubroomId, copiedEntries);
                }
            }

            return newRoomId;
        }

        public static SubRooms? CloneSubroom(long roomId, long subRoomId, long playerId)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;

            var original = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
            if (original == null) return null;

            string GenerateUniqueName()
            {
                string candidate;
                do
                {
                    int suffix = Random.Shared.Next(10000, 999999);
                    candidate = $"{original.Name}_{suffix}";
                } while (room.SubRooms!.Any(s => s.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)));
                return candidate;
            }

            var newSub = new SubRooms
            {
                SubRoomId = GetNextSubRoomId(),
                RoomId = roomId,
                Name = GenerateUniqueName(),
                Accessibility = original.Accessibility,
                IsSandbox = original.IsSandbox,
                MaxPlayers = original.MaxPlayers,
                UnitySceneId = original.UnitySceneId,
                DataBlob = original.DataBlob,
                SavedByAccountId = playerId,
                ShouldAutoStageSaves = original.ShouldAutoStageSaves,
                StagedSubRoomDataSaveId = original.StagedSubRoomDataSaveId,
                LastModeratedSaveModerationState = original.LastModeratedSaveModerationState,
                CreatorAccountId = playerId
            };

            var latestSave = SubRoomSaves
                .Find(s => s.RoomId == roomId && s.SubRoomId == subRoomId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();

            if (latestSave != null)
            {
                var clonedSave = new currentSave
                {
                    SubRoomDataSaveId = GetNextSubRoomSaveId(),
                    RoomId = roomId,
                    SubRoomId = newSub.SubRoomId,
                    DataBlob = latestSave.DataBlob,
                    DataBlobHash = latestSave.DataBlobHash,
                    ReferencedUnityAssetIds = latestSave.ReferencedUnityAssetIds != null ? new List<string>(latestSave.ReferencedUnityAssetIds) : new List<string>(),
                    UnitySubAssets = latestSave.UnitySubAssets != null ? new List<string>(latestSave.UnitySubAssets) : new List<string>(),
                    ReferencedUnityAssets = latestSave.ReferencedUnityAssets != null ? new List<string>(latestSave.ReferencedUnityAssets) : new List<string>(),
                    UnityAssetId = latestSave.UnityAssetId,
                    PersistenceVersion = latestSave.PersistenceVersion,
                    OMVersion = latestSave.OMVersion,
                    UgcSubVersion = latestSave.UgcSubVersion,
                    SavedByAccountId = playerId,
                    SavedOnPlatform = latestSave.SavedOnPlatform,
                    SavedOnDeviceClass = latestSave.SavedOnDeviceClass,
                    // Description = "$"Cloned from subroom ^{original.Name}"",
                    Description = "",
                    Tags = latestSave.Tags != null ? new List<string>(latestSave.Tags) : new List<string>(),
                    ModerationState = latestSave.ModerationState,
                    CreatedAt = DateTime.UtcNow
                };

                SubRoomSaves.Insert(clonedSave);
                newSub.CurrentSave = clonedSave;
                newSub.DataBlob = clonedSave.DataBlob;
            }

            room.SubRooms!.Add(newSub);
            Rooms.Update(room);
            return newSub;
        }

        public static long CloneDormRoom(long playerId)
        {
            var baseRoom = GetRoom(1);
            if (baseRoom == null) throw new Exception("Base dorm room not found.");
            if (baseRoom.SubRooms == null || !baseRoom.SubRooms.Any()) throw new Exception("Base dorm room has no subrooms.");

            var json = System.Text.Json.JsonSerializer.Serialize(baseRoom);
            var clone = System.Text.Json.JsonSerializer.Deserialize<Room>(json);
            if (clone == null) throw new Exception("Failed to clone dorm room.");

            clone.RoomId = 0;
            clone.CreatorAccountId = playerId;
            clone.CloningAllowed = false;
            clone.Name = "DormRoom";
            clone.ImageName = "DefaultRoomImage.jpg";
            clone.CreatedAt = DateTime.UtcNow;
            clone.Accessibility = RoomAccessibility.Private;
            clone.IsRRO = false;
            clone.IsDorm = true;
            clone.Stats = new Stats();
            clone.Roles = new List<Roles>
                {
                    new Roles { AccountId = playerId, Role = Role.Creator, InvitedRole = Role.Creator }
                };

            if (clone.SubRooms != null)
            {
                foreach (var sub in clone.SubRooms)
                {
                    sub.SubRoomId = GetNextSubRoomId();
                    sub.RoomId = 0;
                    sub.SavedByAccountId = playerId;
                    sub.CreatorAccountId = playerId;
                }
            }

            return AddRoomSync(clone);
        }

        public static void DeleteRoom(long roomId)
        {
            SubRoomSaves.DeleteMany(s => s.RoomId == roomId);
            Rooms.Delete(new BsonValue(roomId));
        }

        public static void DeleteAllRoomsFromPlayerId(long creatorId)
        {
            var rooms = Rooms.Find(r => r.CreatorAccountId == creatorId).ToList();
            foreach (var room in rooms)
            {
                SubRoomSaves.DeleteMany(s => s.RoomId == room.RoomId);
                Rooms.Delete(room.RoomId);
            }
            Console.WriteLine($"[DB] Deleted {rooms.Count} rooms for creator {creatorId}.");
        }

        public static Room? GetPlayerDormRoom(long playerId)
        {
            return Rooms.FindOne(r => r.IsDorm && r.CreatorAccountId == playerId);
        }

        public static object? GetRoomSaveById(
        ulong roomId,
        ulong subroomId,
        ulong subRoomDataSaveId,
        int target,
        int version)
        {
            var save = SubRoomSaves.FindOne(s =>
                s.RoomId == (long)roomId &&
                s.SubRoomId == (long)subroomId &&
                s.SubRoomDataSaveId == (long)subRoomDataSaveId);

            if (save == null)
                return null;

            return new
            {
                save.SubRoomDataSaveId,
                save.RoomId,
                save.SubRoomId,
                save.DataBlob,
                save.Description,
                save.CreatedAt,
                Target = target,
                Version = version
            };
        }

        public static RoomDBClasses.SubroomSaveResults? GetRoomSaves(ulong roomId, ulong subroomId, int skip = 0, int take = 20)
        {
            var saves = SubRoomSaves.Find(x => x.RoomId == (long)roomId && x.SubRoomId == (long)subroomId)
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            var paged = saves
                .Skip(skip)
                .Take(take)
                .ToList();

            return new SubroomSaveResults
            {
                Results = paged,
                TotalCount = saves.Count
            };
        }

        public static void FixDuplicateDorms()
        {
            var dormRooms = Rooms.Find(r => r.IsDorm).ToList();
            var grouped = dormRooms.GroupBy(r => r.CreatorAccountId).Where(g => g.Count() > 1);

            foreach (var group in grouped)
            {
                var dormToKeep = group.OrderBy(r => r.CreatedAt).First();
                foreach (var room in group.Where(r => r.RoomId != dormToKeep.RoomId))
                {
                    SubRoomSaves.DeleteMany(s => s.RoomId == room.RoomId);
                    Rooms.Delete(room.RoomId);
                    Console.WriteLine($"[DB] Deleted duplicate dorm {room.RoomId} for creator {room.CreatorAccountId}");
                }
                Console.WriteLine($"[DB] Kept dorm {dormToKeep.RoomId} for creator {group.Key}");
            }
        }

        public static bool DoesPlayerDormExist(long playerId)
        {
            var dorm = Rooms.FindOne(r => r.CreatorAccountId == playerId && r.IsDorm);
            return dorm != null;
        }
    }
}