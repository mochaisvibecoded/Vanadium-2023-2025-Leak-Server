using LiteDB;
using Vanadium.Classes;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.InventionDBClasses;

namespace Vanadium.Classes.DBs
{
    public class InventionDB
    {
        public static LiteDatabase InventionDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Inventions.db") + ";Connection=shared");
        public static readonly ILiteCollection<Invention> Inventions = InventionDBFile.GetCollection<Invention>("Inventions");
        public static readonly ILiteCollection<InventionVersion> Versions = InventionDBFile.GetCollection<InventionVersion>("Versions");

        public static void Setup()
        {
            Inventions.EnsureIndex(x => x.InventionId);
            Inventions.EnsureIndex(x => x.CreatorPlayerId);
            Versions.EnsureIndex(x => x.InventionId);
        }

        public static object MapVersion(InventionVersion v) => new
        {
            v.InventionId,
            v.ReplicationId,
            v.VersionNumber,
            v.BlobName,
            v.BlobHash,
            v.InstantiationCost,
            v.LightsCost,
            v.ChipsCost,
            v.CloudVariablesCost,
            v.AICost,
            v.inkCost,
            CreatedAt = v.CreatedAt.ToString("O")
        };

        public static object MapInventionFull(Invention inv)
        {
            var resolvedCurrentVersion = inv.CurrentVersion
                ?? (inv.Versions ?? new List<InventionVersion>())
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault();

            return new
            {
                inv.InventionId,
                inv.ReplicationId,
                inv.CreatorPlayerId,
                inv.Name,
                inv.Description,
                inv.LongDescription,
                inv.ImageName,
                inv.CurrentVersionNumber,
                inv.LatestVersionNumber,
                inv.UgcVersion,
                inv.ForceCannotPublish,
                CurrentVersion = resolvedCurrentVersion != null ? MapVersion(resolvedCurrentVersion) : (object?)null,
                Versions = (inv.Versions ?? new List<InventionVersion>()).Select(v => MapVersion(v)).ToList(),
                Accessibility = (int)inv.Accessibility,
                inv.IsPublished,
                inv.IsFeatured,
                inv.IsRecRoomApproved,
                inv.IsAGInvention,
                inv.IsCertifiedInvention,
                ModifiedAt = inv.ModifiedAt.ToString("O"),
                CreatedAt = inv.CreatedAt.ToString("O"),
                FirstPublishedAt = inv.FirstPublishedAt.HasValue && inv.FirstPublishedAt.Value != DateTime.MinValue
                    ? inv.FirstPublishedAt.Value.ToString("O")
                    : (string?)null,
                inv.CreationRoomId,
                inv.NumPlayersHaveUsedInRoom,
                inv.NumDownloads,
                inv.CheerCount,
                inv.CreatorPermission,
                inv.GeneralPermission,
                inv.Price,
                inv.AllowTrial,
                inv.HideFromPlayer,
                inv.DisplayMetadataJson,
                ReferencedInventions = inv.ReferencedInventions ?? new List<long>(),
                Tags = inv.Tags ?? new List<InventionTag>()
            };
        }

        public static InventionData MapInventionToData(Invention inv) => new InventionData
        {
            AllowTrial = inv.AllowTrial,
            CheerCount = inv.CheerCount,
            CreatedAt = inv.CreatedAt.ToString("O"),
            CreatorPermission = inv.CreatorPermission,
            CreatorPlayerId = inv.CreatorPlayerId,
            CurrentVersionNumber = inv.CurrentVersionNumber,
            Description = inv.Description,
            GeneralPermission = inv.GeneralPermission,
            HideFromPlayer = inv.HideFromPlayer,
            ImageName = inv.ImageName,
            InventionId = inv.InventionId,
            IsAGInvention = inv.IsAGInvention,
            IsCertifiedInvention = inv.IsCertifiedInvention,
            IsPublished = inv.IsPublished,
            IsFeatured = inv.IsFeatured,
            IsRecRoomApproved = inv.IsRecRoomApproved,
            ModifiedAt = inv.ModifiedAt.ToString("O"),
            Name = inv.Name,
            NumDownloads = inv.NumDownloads,
            NumPlayersHaveUsedInRoom = inv.NumPlayersHaveUsedInRoom,
            Price = inv.Price,
            ReplicationId = inv.ReplicationId,
            LongDescription = inv.LongDescription,
            UgcVersion = inv.UgcVersion,
            ForceCannotPublish = inv.ForceCannotPublish,
            LatestVersionNumber = inv.LatestVersionNumber,
            Versions = inv.Versions,
            CurrentVersion = inv.CurrentVersion,
            Accessibility = (int)inv.Accessibility,
            FirstPublishedAt = inv.FirstPublishedAt,
            CreationRoomId = inv.CreationRoomId,
            DisplayMetadataJson = inv.DisplayMetadataJson,
            ReferencedInventions = inv.ReferencedInventions ?? new List<long>(),
            Tags = inv.Tags ?? new List<InventionTag>()
        };

        public static Invention? GetInvention(long inventionId)
        {
            var invention = Inventions.FindById(inventionId);
            if (invention == null) return null;

            if (invention.CurrentVersion == null)
            {
                var fallback = Versions
                    .Find(v => v.InventionId == inventionId)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault();

                if (fallback != null)
                {
                    invention.CurrentVersion = fallback;
                    invention.CurrentVersionNumber = fallback.VersionNumber;
                    invention.LatestVersionNumber = fallback.VersionNumber;
                    Inventions.Update(invention);
                }
            }

            return invention;
        }

        public static List<Invention> GetInventionsBatch(List<long> inventionIds)
        {
            var inventions = Inventions.Find(x => inventionIds.Contains(x.InventionId)).ToList();

            foreach (var invention in inventions.Where(inv => inv.CurrentVersion == null))
            {
                var fallback = Versions
                    .Find(v => v.InventionId == invention.InventionId)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault();

                if (fallback != null)
                {
                    invention.CurrentVersion = fallback;
                    invention.CurrentVersionNumber = fallback.VersionNumber;
                    invention.LatestVersionNumber = fallback.VersionNumber;
                    Inventions.Update(invention);
                }
            }

            return inventions;
        }

        public static List<InventionData> GetInventionsByPlayer(long playerId)
            => Inventions.Find(x => x.DownloadIds.Contains(playerId)).ToList()
                .Select(inv => MapInventionToData(inv)).ToList();

        public static List<object> GetInventionsByCreator(long creatorId)
            => Inventions.Find(x => x.CreatorPlayerId == creatorId).ToList()
                .Select(inv => MapInventionFull(inv)).ToList();

        public static List<object> GetInventionsByRoom(long roomId)
            => Inventions.Find(x => x.CreationRoomId == roomId).ToList()
                .Select(inv => MapInventionFull(inv)).ToList();

        public static List<object> GetVersionsForInvention(long inventionId)
            => Versions.Find(v => v.InventionId == inventionId)
                .Select(v => MapVersion(v)).ToList();

        public static InventionVersion? GetVersion(long inventionId, int versionNumber)
            => Versions.FindOne(x => x.InventionId == inventionId && x.VersionNumber == versionNumber);

        public static InventionVersion? GetLatestVersion(long inventionId)
            => Versions.Find(v => v.InventionId == inventionId)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault();

        public static long GetNextInventionId()
        {
            if (Inventions.Count() == 0)
                return 5012352472715840656;
            return Convert.ToInt64(Inventions.Max(x => x.InventionId)) + 1;
        }

        public static (Invention invention, InventionVersion version)? SaveInvention(long creatorPlayerId, SaveInventionRequest request)
        {
            try
            {
                long inventionId = GetNextInventionId();

                var version = new InventionVersion
                {
                    InventionId = inventionId,
                    VersionNumber = 1,
                    ReplicationId = Guid.NewGuid().ToString(),
                    BlobName = request.inventionDataFilename ?? string.Empty,
                    BlobHash = string.Empty,
                    ChipsCost = request.chipsCost,
                    CloudVariablesCost = request.cloudVariablesCost,
                    InstantiationCost = request.instantiationCost,
                    LightsCost = request.lightsCost,
                    AICost = request.aiCost,
                    inkCost = request.inkCost,
                    CreatedAt = DateTime.UtcNow
                };

                var invention = new Invention
                {
                    InventionId = inventionId,
                    ReplicationId = Guid.NewGuid().ToString(),
                    CreatorPlayerId = creatorPlayerId,
                    CreationRoomId = request.creationRoomId,
                    Name = request.name ?? string.Empty,
                    Description = request.description ?? string.Empty,
                    ImageName = request.imageName ?? string.Empty,
                    CurrentVersionNumber = 1,
                    LatestVersionNumber = 1,
                    CurrentVersion = version,
                    Versions = new List<InventionVersion> { version },
                    Accessibility = RoomDBClasses.RoomAccessibility.Private,
                    AllowTrial = true,
                    HideFromPlayer = false,
                    CreatorPermission = (int)InventionPermissions.Unlimited,
                    GeneralPermission = (int)InventionPermissions.Unassigned,
                    inkCost = 1,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow,
                    DownloadIds = new List<long> { creatorPlayerId },
                    ReferencedInventions = request.referencedInventions ?? new List<long>()
                };

                Inventions.Insert(invention);
                Versions.Insert(version);

                return (invention, version);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InventionDB] Error saving invention: {ex.Message}");
                return null;
            }
        }

        public static (Invention invention, InventionVersion version)? AddVersion(Invention invention, AddVersionRequest request)
        {
            int nextVersion = invention.CurrentVersionNumber + 1;

            var version = new InventionVersion
            {
                InventionId = invention.InventionId,
                VersionNumber = nextVersion,
                BlobName = request.inventionDataFilename ?? string.Empty,
                ChipsCost = request.chipsCost,
                CloudVariablesCost = request.cloudVariablesCost,
                InstantiationCost = request.instantiationCost,
                LightsCost = request.lightsCost,
                AICost = request.aiCost,
                inkCost = request.inkCost,
                ReplicationId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow
            };

            invention.CurrentVersionNumber = nextVersion;
            invention.LatestVersionNumber = nextVersion;
            invention.CurrentVersion = version;
            invention.Versions.Add(version);
            invention.ModifiedAt = DateTime.UtcNow;

            Versions.Insert(version);
            Inventions.Update(invention);

            return (invention, version);
        }

        public static Invention? UpdateInvention(long inventionId, long playerId,
            string? name, string? description, string? imageName,
            int? price, bool? isPublished, bool? allowTrial,
            bool? hideFromPlayer, int? generalPermission, int? creatorPermission)
        {
            var invention = GetInvention(inventionId);
            if (invention == null || invention.CreatorPlayerId != playerId) return null;

            if (name != null) invention.Name = name;
            if (description != null) invention.Description = description;
            if (imageName != null) invention.ImageName = imageName;
            if (price.HasValue) invention.Price = price.Value;
            if (isPublished.HasValue) invention.IsPublished = isPublished.Value;
            if (allowTrial.HasValue) invention.AllowTrial = allowTrial.Value;
            if (hideFromPlayer.HasValue) invention.HideFromPlayer = hideFromPlayer.Value;
            if (generalPermission.HasValue) invention.GeneralPermission = generalPermission.Value;
            if (creatorPermission.HasValue) invention.CreatorPermission = creatorPermission.Value;

            invention.ModifiedAt = DateTime.UtcNow;
            Inventions.Update(invention);

            return invention;
        }

        public static Invention? PublishInvention(long inventionId, long playerId, int permissionLevel, int accessibility, int price)
        {
            var invention = GetInvention(inventionId);
            if (invention == null || invention.CreatorPlayerId != playerId) return null;

            invention.CreatorPermission = 255;
            invention.GeneralPermission = permissionLevel;
            invention.Accessibility = (RoomDBClasses.RoomAccessibility)accessibility;
            invention.Price = price;
            invention.IsPublished = true;

            invention.ModifiedAt = DateTime.UtcNow;
            Inventions.Update(invention);

            return invention;
        }

        public static Invention? UnpublishInvention(long inventionId, long playerId)
        {
            var invention = GetInvention(inventionId);
            if (invention == null || invention.CreatorPlayerId != playerId) return null;

            invention.IsPublished = false;

            invention.ModifiedAt = DateTime.UtcNow;
            Inventions.Update(invention);

            return invention;
        }

        public static List<Invention> GetTopTodayInventions(bool featuredOnly, bool includeUnpublished = false)
        {
            var query = Inventions.FindAll().AsEnumerable();
            if (featuredOnly)
            {
                query = query.Where(r => r.Tags.Any(t => string.Equals(t.Tag, "featured", StringComparison.OrdinalIgnoreCase) || r.IsFeatured));
            }
            if (!includeUnpublished)
                query = query.Where(r => r.IsPublished);

            return query.OrderByDescending(r => r.CheerCount).ToList();
        }

        public static (List<Invention> Results, int Total) Search(string query, int skip = 0, int take = 30)
        {
            if (string.IsNullOrWhiteSpace(query))
                return (new List<Invention>(), 0);

            var segments = query.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            var candidatePool = Inventions.FindAll()
                .Where(i => i.IsPublished)
                .Where(i => !i.HideFromPlayer)
                .Where(i => i.Accessibility == RoomDBClasses.RoomAccessibility.Public)
                .ToList();

            var allPlayers = PlayerDB.Players.FindAll().ToList();

            var matched = new Dictionary<long, Invention>();

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

                foreach (var invention in candidatePool)
                {
                    if (resolvedAuthorId.HasValue && invention.CreatorPlayerId != resolvedAuthorId.Value)
                        continue;

                    if (tagParts.Count > 0)
                    {
                        var inventionTags = (invention.Tags ?? new List<InventionTag>())
                            .Select(t => t.Tag.ToLowerInvariant().Trim())
                            .ToList();

                        bool allTagsMatch = tagParts.All(tp =>
                        {
                            if (inventionTags.Contains(tp))
                                return true;

                            foreach (var it in inventionTags)
                            {
                                if (it.Contains(tp) || tp.Contains(it))
                                    return true;

                                int dist = EditDistance(it, tp);
                                int maxLen = Math.Max(it.Length, tp.Length);
                                if (maxLen > 0 && (double)dist / maxLen <= 0.34)
                                    return true;
                            }

                            return false;
                        });

                        if (!allTagsMatch)
                            continue;
                    }

                    if (nameParts.Count > 0)
                    {
                        string inventionNameLower = (invention.Name ?? "").ToLowerInvariant();

                        bool nameMatch = nameParts.All(np =>
                        {
                            if (inventionNameLower.Contains(np))
                                return true;

                            int dist = EditDistance(inventionNameLower, np);
                            int maxLen = Math.Max(inventionNameLower.Length, np.Length);
                            if (maxLen > 0 && (double)dist / maxLen <= 0.34)
                                return true;

                            return false;
                        });

                        if (!nameMatch)
                            continue;
                    }

                    matched[invention.InventionId] = invention;
                }
            }

            var all = matched.Values
                .OrderByDescending(i => i.CheerCount)
                .ThenByDescending(i => i.NumDownloads)
                .ToList();

            return (all.Skip(skip).Take(take).ToList(), all.Count);
        }

        public static (bool success, bool isCheering, int cheerCount)? CheerInvention(long inventionId, long playerId, bool cheer)
        {
            var invention = GetInvention(inventionId);
            if (invention == null) return null;

            var player = PlayerDB.Players.FindById(playerId);
            if (player?.Player?.PlayerExtra == null) return null;

            player.Player.PlayerExtra.PersonalDetails ??= new PlayerDBClasses.PersonalDetails();
            player.Player.PlayerExtra.PersonalDetails.inventionsCheered ??= new List<long>();

            bool alreadyCheering = player.Player.PlayerExtra.PersonalDetails.inventionsCheered.Contains(inventionId);

            if (cheer && !alreadyCheering)
            {
                invention.CheerCount++;
                player.Player.PlayerExtra.PersonalDetails.inventionsCheered.Add(inventionId);
                player.Player.PlayerExtra.PersonalDetails.IsCheering = true;
            }
            else if (!cheer && alreadyCheering)
            {
                invention.CheerCount = Math.Max(0, invention.CheerCount - 1);
                player.Player.PlayerExtra.PersonalDetails.inventionsCheered.Remove(inventionId);
                player.Player.PlayerExtra.PersonalDetails.IsCheering = false;
            }

            Inventions.Update(invention);
            PlayerDB.Players.Update(player);

            return (true, player.Player.PlayerExtra.PersonalDetails.inventionsCheered.Contains(inventionId), invention.CheerCount);
        }

        public static bool DeleteInvention(long inventionId, long playerId)
        {
            var invention = GetInvention(inventionId);
            if (invention == null || invention.CreatorPlayerId != playerId) return false;

            try
            {
                var cdnPath = Path.Join(Program.dataDir, "cdn", "inv");

                if (Directory.Exists(cdnPath))
                {
                    foreach (var v in Versions.Find(v => v.InventionId == inventionId).ToList())
                        if (!string.IsNullOrWhiteSpace(v.ReplicationId))
                            foreach (var file in Directory.GetFiles(cdnPath, v.ReplicationId + "*"))
                                try { File.Delete(file); } catch { }

                    if (!string.IsNullOrWhiteSpace(invention.ReplicationId))
                        foreach (var file in Directory.GetFiles(cdnPath, invention.ReplicationId + "*"))
                            try { File.Delete(file); } catch { }
                }

                Versions.DeleteMany(v => v.InventionId == inventionId);
                Inventions.Delete(inventionId);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InventionDB] Error deleting invention: {ex.Message}");
                return false;
            }
        }

        public static SetTagsResponse SetTags(long inventionId, SetTagsRequest request)
        {
            try
            {
                var invention = GetInvention(inventionId);
                if (invention == null)
                    return new SetTagsResponse { Result = -1 };

                invention.Tags.Clear();

                var allTags = new List<string>();

                foreach (var autoTag in request.AutoTags ?? new List<string>())
                {
                    invention.Tags.Add(new InventionTag { Tag = autoTag, Type = 0 });
                    allTags.Add(autoTag);
                }

                foreach (var customTag in request.CustomTags ?? new List<string>())
                {
                    invention.Tags.Add(new InventionTag { Tag = customTag, Type = 1 });
                    allTags.Add(customTag);
                }

                Inventions.Update(invention);

                return new SetTagsResponse { Result = 0, Tags = allTags };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InventionDB] Error setting tags: {ex.Message}");
                return new SetTagsResponse { Result = -1 };
            }
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
    }
}