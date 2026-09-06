using LiteDB;
using Vanadium.Classes;
using Vanadium.Classes.DBs.DBClasses;
using System;
using System.Collections.Generic;
using System.Linq;
using static Vanadium.Classes.DBs.DBClasses.ClubsDBClasses;

namespace Vanadium.Classes.DBs
{
    public class ClubsDB
    {
        public static LiteDatabase ClubsDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Clubs.db") + ";Connection=shared");

        public static readonly ILiteCollection<Club> Clubs =
            ClubsDBFile.GetCollection<Club>("Clubs");

        private static readonly ILiteCollection<ClubDbCounters> Counters =
            ClubsDBFile.GetCollection<ClubDbCounters>("Counters");

        public static readonly ILiteCollection<ClubAnnouncement> Announcements =
            ClubsDBFile.GetCollection<ClubAnnouncement>("ClubAnnouncements");


        public static void Setup()
        {
            Clubs.EnsureIndex(x => x.ClubId);
            Clubs.EnsureIndex(x => x.CreatorAccountId);
            Clubs.EnsureIndex(x => x.Name);
            Clubs.EnsureIndex(x => x.IsDeleted);
            Announcements.EnsureIndex(x => x.ClubId);
            Announcements.EnsureIndex(x => x.AnnouncementId);
        }


        public static long GetNextClubId()
        {
            var counter = Counters.FindById("club");
            if (counter == null)
            {
                counter = new ClubDbCounters { Key = "club", Value = 1 };
                Counters.Insert(counter);
                return 1;
            }
            counter.Value++;
            Counters.Update(counter);
            return counter.Value;
        }

        private static long GetNextPermissionsId()
        {
            var counter = Counters.FindById("clubperms");
            if (counter == null)
            {
                counter = new ClubDbCounters { Key = "clubperms", Value = 1 };
                Counters.Insert(counter);
                return 1;
            }
            counter.Value++;
            Counters.Update(counter);
            return counter.Value;
        }

        private static string GeneratePlayerClubHandle()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var rng = new Random();
            return new string(Enumerable.Range(0, 16).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
        }

        private static List<ClubPermissions> CreateDefaultPermissions(long clubId)
        {
            return new List<ClubPermissions>
            {
                new ClubPermissions
                {
                    ClubPermissionsId = GetNextPermissionsId(),
                    ClubId = clubId,
                    Type = 30,
                    ApproveMember = true,
                    BanUnban = true,
                    CreateEvent = true,
                    EditDetails = true,
                    EditPermissionSettings = true,
                    PostAnnouncement = true
                },
                new ClubPermissions
                {
                    ClubPermissionsId = GetNextPermissionsId(),
                    ClubId = clubId,
                    Type = 20,
                    ApproveMember = true,
                    BanUnban = true,
                    CreateEvent = false,
                    EditDetails = false,
                    EditPermissionSettings = false,
                    PostAnnouncement = false
                },
                new ClubPermissions
                {
                    ClubPermissionsId = GetNextPermissionsId(),
                    ClubId = clubId,
                    Type = 10,
                    ApproveMember = false,
                    BanUnban = false,
                    CreateEvent = false,
                    EditDetails = false,
                    EditPermissionSettings = false,
                    PostAnnouncement = false
                }
            };
        }


        public static Club CreateClub(
            long creatorId,
            string name,
            string description,
            string category)
        {
            long clubId = GetNextClubId();

            var club = new Club
            {
                ClubId = clubId,
                Name = name,
                Description = description,
                Category = category,
                CreatorAccountId = creatorId,
                MemberCount = 1,
                Visibility = (int)ClubVisibility.Public,
                Joinability = (int)ClubJoinability.Open,
                AllowJuniors = true,
                MainImageName = "DefaultRoomImage.png",
                CreatedAt = DateTime.UtcNow,
                Members = new List<ClubMember>
                {
                    new ClubMember
                    {
                        MemberId = GetNextMemberId(),
                        AccountId = creatorId,
                        MemberType = (int)ClubMemberType.Owner,
                        JoinedAt = DateTime.UtcNow
                    }
                }
            };

            club.Permissions = CreateDefaultPermissions(clubId);

            Clubs.Insert(club);
            return club;
        }

        private static long GetNextMemberId()
        {
            var counter = Counters.FindById("clubmember");
            if (counter == null)
            {
                counter = new ClubDbCounters { Key = "clubmember", Value = 1 };
                Counters.Insert(counter);
                return 1;
            }
            counter.Value++;
            Counters.Update(counter);
            return counter.Value;
        }

        public static Club CreatePlayerClub(long creatorId)
        {
            long clubId = GetNextClubId();
            string handle = GeneratePlayerClubHandle();

            var club = new Club
            {
                ClubId = clubId,
                Name = handle,
                Description = "",
                Category = "Social",
                CreatorAccountId = creatorId,
                MemberCount = 1,
                Visibility = (int)ClubVisibility.Unlisted,
                Joinability = (int)ClubJoinability.Open,
                AllowJuniors = true,
                MainImageName = "DefaultRoomImage.png",
                CreatedAt = DateTime.UtcNow,
                Members = new List<ClubMember>
                {
                    new ClubMember
                    {
                        MemberId = GetNextMemberId(),
                        AccountId = creatorId,
                        MemberType = (int)ClubMemberType.Owner,
                        JoinedAt = DateTime.UtcNow
                    }
                }
            };

            club.Permissions = CreateDefaultPermissions(clubId);

            Clubs.Insert(club);
            return club;
        }

        public static Club? GetClub(long clubId)
        {
            var club = Clubs.FindById(clubId);
            return (club == null || club.IsDeleted) ? null : club;
        }

        public static bool DeleteClub(long clubId)
        {
            var club = Clubs.FindById(clubId);
            if (club == null || club.IsDeleted) return false;
            club.IsDeleted = true;
            Clubs.Update(club);
            return true;
        }

        public static List<Club> GetClubsCreatedBy(long playerId)
            => Clubs.Find(c => c.CreatorAccountId == playerId && !c.IsDeleted).ToList();

        public static List<Club> GetClubsJoinedBy(long playerId)
        {
            return Clubs.FindAll()
                .Where(c => !c.IsDeleted &&
                            c.Members.Any(m => m.AccountId == playerId))
                .ToList();
        }

        public static int GetSubscriberCount(long playerClubId)
        {
            var club = GetClub(playerClubId);
            if (club == null) return 0;
            return Math.Max(0, club.MemberCount - 1);
        }

        public static ClubSearchResult Search(
            string? query,
            string? category,
            int sort,
            int count,
            int skip = 0)
        {
            IEnumerable<Club> all = Clubs.FindAll()
                .Where(c => !c.IsDeleted && c.Visibility == (int)ClubVisibility.Public);

            if (!string.IsNullOrWhiteSpace(category))
                all = all.Where(c =>
                    string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(query))
            {
                string q = query.ToLowerInvariant();

                all = all
                    .Select(c =>
                    {
                        string nameLower = (c.Name ?? "").ToLowerInvariant();
                        string descLower = (c.Description ?? "").ToLowerInvariant();

                        int score = 0;

                        if (nameLower == q || descLower == q)
                            score += 200;

                        if (nameLower.StartsWith(q))
                            score += 100;

                        if (nameLower.Contains(q))
                            score += 50;

                        if (descLower.Contains(q))
                            score += 20;

                        if (c.CustomTags.Any(t => t.ToLowerInvariant().Contains(q)))
                            score += 30;

                        score -= Math.Min(nameLower.Length, 50);

                        return new { Club = c, Score = score };
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .Select(x => x.Club);
            }

            IEnumerable<Club> sorted = sort switch
            {
                1 => all.OrderByDescending(c => c.CreatedAt),
                2 => all.OrderBy(c => c.CreatedAt),
                _ => all.OrderByDescending(c => c.MemberCount)
            };

            var list = sorted.ToList();
            int total = list.Count;

            var page = list.Skip(skip).Take(count).ToList();

            return new ClubSearchResult
            {
                Clubs = page.Select(ToSummary).ToList(),
                ContinuationToken = null,
                TotalClubs = total
            };
        }

        public static ClubMember? GetMember(long clubId, long playerId)
        {
            var club = GetClub(clubId);
            return club?.Members.FirstOrDefault(m => m.AccountId == playerId);
        }

        public static bool IsMember(long clubId, long playerId)
            => GetMember(clubId, playerId) != null;

        public static List<ClubMemberDTO> GetClubMembers(long clubId)
        {
            var club = GetClub(clubId);
            if (club == null)
                return new List<ClubMemberDTO>();

            return club.Members
                .Select(m => new ClubMemberDTO
                {
                    MemberId = m.MemberId,
                    AccountId = m.AccountId,
                    ClubId = club.ClubId,
                    MembershipType = m.MemberType,
                    CreatedAt = m.JoinedAt
                })
                .OrderByDescending(m => m.CreatedAt)
                .ToList();
        }

        public static bool IsOwnerOrCoOwner(long clubId, long playerId)
        {
            var m = GetMember(clubId, playerId);
            return m != null && m.MemberType >= (int)ClubMemberType.CoOwner;
        }

        public static bool CanEditDetails(long clubId, long playerId)
        {
            var club = GetClub(clubId);
            if (club == null) return false;
            if (club.CreatorAccountId == playerId) return true;

            var member = club.Members.FirstOrDefault(m => m.AccountId == playerId);
            if (member == null) return false;

            var perms = club.Permissions.FirstOrDefault(p => p.Type == member.MemberType);
            return perms?.EditDetails == true || member.MemberType >= (int)ClubMemberType.CoOwner;
        }

        public static Club? JoinClub(long clubId, long playerId)
        {
            var club = GetClub(clubId);
            if (club == null) return null;
            if (club.BannedMemberIds.Contains(playerId)) return null;
            if (IsMember(clubId, playerId)) return club;

            bool needsApproval = club.Joinability == (int)ClubJoinability.ApprovalRequired;

            club.Members.Add(new ClubMember
            {
                MemberId = GetNextMemberId(),
                AccountId = playerId,
                MemberType = (int)ClubMemberType.Member,
                JoinedAt = DateTime.UtcNow,
                IsPendingApproval = needsApproval
            });

            if (!needsApproval)
                club.MemberCount++;

            Clubs.Update(club);
            return club;
        }

        public static Club LeaveClub(long clubId, long playerId)
        {
            var club = GetClub(clubId);
            if (club == null) return null;

            var member = club.Members.FirstOrDefault(m => m.AccountId == playerId);
            if (member == null) return null;

            if (member.MemberType == (int)ClubMemberType.Owner) return null;

            club.Members.Remove(member);
            if (!member.IsPendingApproval)
                club.MemberCount = Math.Max(0, club.MemberCount - 1);

            Clubs.Update(club);
            return club;
        }

        public static Club KickMember(long clubId, long targetId, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null) return null;

            var requester = club.Members.FirstOrDefault(m => m.AccountId == requesterId);
            var target = club.Members.FirstOrDefault(m => m.AccountId == targetId);

            if (requester == null || target == null) return null;
            if (requester.MemberType <= target.MemberType && club.CreatorAccountId != requesterId)
                return null;

            club.Members.Remove(target);
            if (!target.IsPendingApproval)
                club.MemberCount = Math.Max(0, club.MemberCount - 1);

            Clubs.Update(club);
            return club;
        }

        public static Club ApproveMember(long clubId, long targetId, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null) return null;

            var requester = club.Members.FirstOrDefault(m => m.AccountId == requesterId);
            if (requester == null) return null;

            var perms = club.Permissions.FirstOrDefault(p => p.Type == requester.MemberType);
            bool canApprove = club.CreatorAccountId == requesterId ||
                              requester.MemberType >= (int)ClubMemberType.CoOwner ||
                              (perms?.ApproveMember == true);

            if (!canApprove) return null;

            var target = club.Members.FirstOrDefault(m => m.AccountId == targetId && m.IsPendingApproval);
            if (target == null) return null;

            target.IsPendingApproval = false;
            club.MemberCount++;
            Clubs.Update(club);
            return club;
        }

        public static bool SetMemberRole(long clubId, long targetId, int newRole, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null) return false;

            var requester = club.Members.FirstOrDefault(m => m.AccountId == requesterId);
            if (requester == null && club.CreatorAccountId != requesterId) return false;

            var target = club.Members.FirstOrDefault(m => m.AccountId == targetId);
            if (target == null) return false;

            target.MemberType = newRole;
            Clubs.Update(club);
            return true;
        }

        public static bool BanMember(long clubId, long targetId, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null) return false;

            if (!IsOwnerOrCoOwner(clubId, requesterId) && club.CreatorAccountId != requesterId)
                return false;

            var target = club.Members.FirstOrDefault(m => m.AccountId == targetId);
            if (target != null)
            {
                club.Members.Remove(target);
                if (!target.IsPendingApproval)
                    club.MemberCount = Math.Max(0, club.MemberCount - 1);
            }

            if (!club.BannedMemberIds.Contains(targetId))
                club.BannedMemberIds.Add(targetId);

            Clubs.Update(club);
            return true;
        }

        public static bool UnbanMember(long clubId, long targetId, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null) return false;

            if (!IsOwnerOrCoOwner(clubId, requesterId) && club.CreatorAccountId != requesterId)
                return false;

            club.BannedMemberIds.Remove(targetId);
            Clubs.Update(club);
            return true;
        }

        public static Club? SetMainImage(long clubId, string imageName, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null || !CanEditDetails(clubId, requesterId)) return null;
            club.MainImageName = imageName;
            Clubs.Update(club);
            return club;
        }

        public static Club? DeleteMainImage(long clubId, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null || !CanEditDetails(clubId, requesterId)) return null;
            if (club.AdditionalImages.Count == 0) return null;
            club.MainImageName = club.AdditionalImages[0].ImageName;
            club.AdditionalImages.RemoveAt(0);
            Clubs.Update(club);
            return club;
        }

        public static Club? SetAdditionalImage(long clubId, int index, string imageName, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null || !CanEditDetails(clubId, requesterId)) return null;

            var existing = club.AdditionalImages.FirstOrDefault(i => i.Index == index);
            if (existing != null)
                existing.ImageName = imageName;
            else
                club.AdditionalImages.Add(new ClubAdditionalImage { Index = index, ImageName = imageName });

            Clubs.Update(club);
            return club;
        }

        public static Club? DeleteAdditionalImage(long clubId, int index, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null || !CanEditDetails(clubId, requesterId)) return null;

            var img = club.AdditionalImages.FirstOrDefault(i => i.Index == index);
            if (img != null)
                club.AdditionalImages.Remove(img);

            Clubs.Update(club);
            return club;
        }

        public static Club? SetClubhouse(long clubId, long roomId, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null || !IsOwnerOrCoOwner(clubId, requesterId) && club.CreatorAccountId != requesterId)
                return null;
            club.ClubhouseRoomId = roomId;
            Clubs.Update(club);
            return club;
        }

        public static Club? RemoveClubhouse(long clubId, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null || !IsOwnerOrCoOwner(clubId, requesterId) && club.CreatorAccountId != requesterId)
                return null;
            club.ClubhouseRoomId = null;
            Clubs.Update(club);
            return club;
        }

        public static Club? SetPermissions(
            long clubId,
            int membershipType,
            bool approveMember,
            bool banUnban,
            bool createEvent,
            bool editDetails,
            bool editPermissionSettings,
            bool postAnnouncement)
        {
            var club = GetClub(clubId);
            if (club == null) return null;

            int normalizedType = membershipType switch
            {
                0 => (int)ClubMemberType.Member,
                1 => (int)ClubMemberType.Moderator,
                2 => (int)ClubMemberType.CoOwner,
                (int)ClubMemberType.Member or (int)ClubMemberType.Moderator or (int)ClubMemberType.CoOwner => membershipType,
                _ => -1
            };

            if (normalizedType == -1) return null;

            var existing = club.Permissions.FirstOrDefault(p => p.Type == normalizedType);
            if (existing == null)
            {
                existing = new ClubPermissions
                {
                    ClubPermissionsId = GetNextPermissionsId(),
                    ClubId = clubId,
                    Type = normalizedType
                };
                club.Permissions.Add(existing);
            }

            existing.ApproveMember = approveMember;
            existing.BanUnban = banUnban;
            existing.CreateEvent = createEvent;
            existing.EditDetails = editDetails;
            existing.EditPermissionSettings = editPermissionSettings;
            existing.PostAnnouncement = postAnnouncement;

            Clubs.Update(club);
            return club;
        }

        public static Club? Modify(
            long clubId,
            string name,
            string description,
            string category,
            long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null || !CanEditDetails(clubId, requesterId)) return null;
            club.Name = name;
            club.Description = description;
            club.Category = category;
            Clubs.Update(club);
            return club;
        }

        public static Club? SetMinLevel(long clubId, int minLevel, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null || !CanEditDetails(clubId, requesterId)) return null;
            club.MinLevel = minLevel;
            Clubs.Update(club);
            return club;
        }

        public static Club? ModifyDetails(
            long clubId,
            int? visibility,
            int? joinability,
            bool? allowJuniors,
            List<string>? customTags,
            long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null || !CanEditDetails(clubId, requesterId)) return null;

            if (visibility.HasValue) club.Visibility = visibility.Value;
            if (joinability.HasValue) club.Joinability = joinability.Value;
            if (allowJuniors.HasValue) club.AllowJuniors = allowJuniors.Value;
            if (customTags != null)
            {
                club.CustomTags = customTags
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Distinct()
                    .ToList();
            }

            Clubs.Update(club);
            return club;
        }

        public static ClubSummaryDTO ToSummary(Club club) => new ClubSummaryDTO
        {
            ClubId = club.ClubId,
            Name = club.Name,
            Description = club.Description,
            Category = club.Category,
            Joinability = club.Joinability,
            Visibility = club.Visibility,
            AllowJuniors = club.AllowJuniors,
            MinLevel = club.MinLevel,
            ClubType = club.ClubType,
            IsRRO = club.IsRRO,
            State = club.State,
            CreatorAccountId = club.CreatorAccountId,
            ClubhouseRoomId = club.ClubhouseRoomId,
            MainImageName = club.MainImageName,
            MemberCount = club.MemberCount
        };

        public static ClubDetailsDTO ToDetails(Club club, long requesterId) => new ClubDetailsDTO
        {
            ClubId = club.ClubId,
            Club = ToSummary(club),
            AdditionalImages = club.AdditionalImages ?? new(),
            CustomTags = club.CustomTags ?? new(),
            CoownerPermissions = club.Permissions.FirstOrDefault(p => p.Type == 30),
            ModeratorPermissions = club.Permissions.FirstOrDefault(p => p.Type == 20),
            MemberPermissions = club.Permissions.FirstOrDefault(p => p.Type == 10),
            MyMembershipType = club.Members
                .FirstOrDefault(m => m.AccountId == requesterId)?.MemberType ?? 0
        };

        private static long GetNextAnnouncementId()
        {
            var counter = Counters.FindById("announcement");
            if (counter == null)
            {
                counter = new ClubDbCounters { Key = "announcement", Value = 1 };
                Counters.Insert(counter);
                return 1;
            }
            counter.Value++;
            Counters.Update(counter);
            return counter.Value;
        }

        public static bool CanPostAnnouncement(long clubId, long playerId)
        {
            var club = GetClub(clubId);
            if (club == null) return false;
            if (club.CreatorAccountId == playerId) return true;

            var member = club.Members.FirstOrDefault(m => m.AccountId == playerId);
            if (member == null) return false;

            var perms = club.Permissions.FirstOrDefault(p => p.Type == member.MemberType);
            return member.MemberType >= (int)ClubMemberType.CoOwner || perms?.PostAnnouncement == true;
        }

        public static ClubAnnouncement? CreateAnnouncement(long clubId, long creatorAccountId, string title, string body, string? imageName, string? meta)
        {
            if (!CanPostAnnouncement(clubId, creatorAccountId)) return null;

            var announcement = new ClubAnnouncement
            {
                AnnouncementId = GetNextAnnouncementId(),
                ClubId = clubId,
                CreatorAccountId = creatorAccountId,
                Title = title.Trim(),
                Body = body.Trim(),
                ImageName = string.IsNullOrWhiteSpace(imageName) ? null : imageName.Trim(),
                Meta = string.IsNullOrWhiteSpace(meta) ? null : meta,
                CreatedAt = DateTime.UtcNow
            };

            Announcements.Insert(announcement);
            return announcement;
        }

        public static ClubAnnouncement? EditAnnouncement(long clubId, long announcementId, long requesterId, string title, string body, string? imageName, string? meta)
        {
            var announcement = Announcements.FindOne(a => a.AnnouncementId == announcementId && a.ClubId == clubId);
            if (announcement == null) return null;

            if (announcement.CreatorAccountId != requesterId && !CanPostAnnouncement(clubId, requesterId))
                return null;

            announcement.Title = title.Trim();
            announcement.Body = body.Trim();
            announcement.ImageName = string.IsNullOrWhiteSpace(imageName) ? null : imageName.Trim();
            announcement.Meta = string.IsNullOrWhiteSpace(meta) ? null : meta;

            Announcements.Update(announcement);
            return announcement;
        }

        public static bool DeleteAnnouncement(long clubId, long announcementId, long requesterId)
        {
            var announcement = Announcements.FindOne(a => a.AnnouncementId == announcementId && a.ClubId == clubId);
            if (announcement == null) return false;

            if (announcement.CreatorAccountId != requesterId && !CanPostAnnouncement(clubId, requesterId))
                return false;

            Announcements.Delete(announcementId);
            return true;
        }

        public static bool MarkAnnouncementRead(long clubId, long announcementId, long accountId)
        {
            var announcement = Announcements.FindOne(a => a.AnnouncementId == announcementId && a.ClubId == clubId);
            if (announcement == null) return false;

            var allForClub = Announcements.Find(a => a.ClubId == clubId).ToList();

            foreach (var a in allForClub)
            {
                if (!a.LastReadAnnouncementIdByAccount.ContainsKey(accountId) ||
                    a.LastReadAnnouncementIdByAccount[accountId] < announcementId)
                {
                    a.LastReadAnnouncementIdByAccount[accountId] = announcementId;
                    Announcements.Update(a);
                }
            }

            return true;
        }

        public static ClubAnnouncementListDTO? GetClubAnnouncements(long clubId, long requesterId)
        {
            var club = GetClub(clubId);
            if (club == null) return null;

            var allAnnouncements = Announcements
                .Find(a => a.ClubId == clubId)
                .OrderByDescending(a => a.CreatedAt)
                .ToList();

            long? lastAnnouncementId = allAnnouncements.Count > 0
                ? allAnnouncements.Max(a => a.AnnouncementId)
                : (long?)null;

            long lastReadAnnouncementId = 0;
            foreach (var a in allAnnouncements)
            {
                if (a.LastReadAnnouncementIdByAccount.TryGetValue(requesterId, out long readId))
                    lastReadAnnouncementId = Math.Max(lastReadAnnouncementId, readId);
            }

            return new ClubAnnouncementListDTO
            {
                ClubId = clubId,
                Announcements = allAnnouncements.Select(ToAnnouncementDTO).ToList(),
                LastAnnouncementId = lastAnnouncementId,
                LastReadAnnouncementId = lastReadAnnouncementId
            };
        }

        public static List<ClubAnnouncementListDTO> GetAnnouncementsForMyClubs(long accountId)
        {
            return GetClubsJoinedBy(accountId)
                .Select(c => GetClubAnnouncements(c.ClubId, accountId))
                .Where(dto => dto != null && dto.Announcements.Count > 0)
                .ToList()!;
        }

        public static ClubAnnouncementDTO ToAnnouncementDTO(ClubAnnouncement a) => new ClubAnnouncementDTO
        {
            AnnouncementId = a.AnnouncementId,
            ClubId = a.ClubId,
            CreatorAccountId = a.CreatorAccountId,
            Title = a.Title,
            Body = a.Body,
            ImageName = a.ImageName,
            Meta = a.Meta,
            CreatedAt = a.CreatedAt
        };
    }
}