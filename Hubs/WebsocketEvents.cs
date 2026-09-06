using Vanadium.Classes.DBs;
using Vanadium.Hubs;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Classes.WebSocket
{
    public class WebsocketEvents
    {
        public class Response
        {
            public string? Id { get; set; }
            public object? Msg { get; set; }
        }

        public static Response CreatePlayerUpdatedAccountResponse(long playerId)
        {
            var account = PlayerDB.GetAccountMe(playerId);
            return new Response
            {
                Id = "PlayerUpdatedAccount",
                Msg = account
            };
        }

        public static Response CreateClubMembershipUpdateResponse(long clubMemberId, long accountId, long clubId, int membershipType, DateTime createdAt, int invitedMembershipType = 0)
        {
            return new Response
            {
                Id = NotiEventTypes.ClubMembershipUpdate,
                Msg = new
                {
                    ClubMemberId = clubMemberId,
                    AccountId = accountId,
                    ClubId = clubId,
                    MembershipType = membershipType,
                    CreatedAt = createdAt,
                    InvitedMembershipType = invitedMembershipType
                }
            };
        }

        public static Response CreateCreatorClubSubscriptionUpdateResponse(long creatorAccountId, long clubId, int membershipType)
        {
            return new Response
            {
                Id = NotiEventTypes.CreatorClubSubscriptionUpdate,
                Msg = new
                {
                    creatorAccountId = creatorAccountId,
                    clubId = clubId,
                    membershipType = membershipType
                }
            };
        }

        public static Response CreateSubResponse(ulong playerId, int membershipType)
        {
            return new Response
            {
                Id = ((int)ResponseResults.SubscriptionUpdateProfile).ToString(),
                Msg = new Sub
                {
                    creatorAccountId = playerId,
                    clubId = 1,
                    membershipType = membershipType
                }
            };
        }

        public static Response CreateLevelUpdateResponse(long playerId, int level, int xp)
        {
            return new Response
            {
                Id = "PlayerProgressionLevelUpdate",
                Msg = new
                {
                    PlayerId = playerId,
                    Level = level,
                    XP = xp
                }
            };
        }

        public static Response CreateReputationUpdateResponse(long playerId)
        {
            var player = PlayerDB.GetCurrentPlayer(playerId);
            var rep = player?.Player?.Reputation ?? new Reputation();
            bool isDev = player?.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false;

            return new Response
            {
                Id = "ReputationUpdate",
                Msg = new
                {
                    AccountId = playerId,
                    rep.IsCheerful,
                    rep.Noteriety,
                    rep.SelectedCheer,
                    rep.CheerCredit,
                    rep.CheerGeneral,
                    rep.CheerHelpful,
                    rep.CheerCreative,
                    rep.CheerGreatHost,
                    rep.CheerSportsman,
                    rep.SubscriberCount,
                    SubscribedCount = rep.SubscribedCount,
                    RecRoomDeveloper = isDev
                }
            };
        }

        public static Response CreateMessageResponse(MessageData messageData, long forPlayerId)
        {
            return new Response
            {
                Id = "2",
                Msg = new
                {
                    messageData.Id,
                    messageData.Type,
                    messageData.Data,
                    messageData.RoomId,
                    messageData.PlayerEventId,
                    ForPlayerId = forPlayerId
                }
            };
        }

        public static Response CreateLogoutResponse()
        {
            return new Response
            {
                Id = ((int)ResponseResults.Logout).ToString(),
                Msg = new { }
            };
        }

        public static Response CreateBanResponse(ModerationBlockDetails block)
        {
            return new Response
            {
                Id = ((int)ResponseResults.ModerationKick).ToString(),
                Msg = block
            };
        }

        public static Response CreateRelationshipChangedResponse(long targetPlayerId, int relationshipType)
        {
            return new Response
            {
                Id = ((int)ResponseResults.RelationshipChanged).ToString(),
                Msg = new
                {
                    PlayerId = targetPlayerId,
                    RelationshipType = relationshipType
                }
            };
        }

        public static Response CreateWSEventString(string id, object obj)
        {
            return new Response
            {
                Id = id,
                Msg = obj
            };
        }

        /*public static Response CreateWSEventId(int id, object obj)
        {
            return new
            {
                Id = id,
                Msg = obj
            };
        }*/

        public enum NotificationType
        {
            ALL = -1,
            NONE = 0,
            Games = 1,
            FriendRequests = 2,
            FriendStatusOnline = 4,
            Cheers = 8,
            Rooms = 16,
            Subscriptions = 32,
            PlayerEvents = 64,
            Clubs = 128,
            CreatorFeedback = 256,
            RoomNotifications = 512,
            Deprecated = 1073741824,
            NoOptOut = -2147483648
        }

        public enum ResponseResults // got both from february 2024 decomp :fire:
        {
            RelationshipChanged = 1,
            MessageReceived,
            MessageDeleted,
            PresenceHeartbeatResponse,
            RefreshLogin,
            Logout,
            SubscriptionUpdateProfile = 11,
            SubscriptionUpdatePresence,
            SubscriptionUpdateGameSession,
            SubscriptionUpdateRoom = 15,
            SubscriptionUpdateRoomPlaylist,
            ModerationQuitGame = 20,
            ModerationUpdateRequired,
            ModerationKick,
            ModerationKickAttemptFailed,
            ModerationRoomBan,
            ServerMaintenance = 25,
            GiftPackageReceived = 30,
            GiftPackageReceivedImmediate,
            GiftPackageRewardSelectionReceived,
            ProfileJuniorStatusUpdate = 40,
            RelationshipsInvalid = 50,
            StorefrontBalanceAdd = 60,
            StorefrontBalanceUpdate,
            StorefrontBalancePurchase,
            ConsumableMappingAdded = 70,
            ConsumableMappingRemoved,
            PlayerEventCreated = 80,
            PlayerEventUpdated,
            PlayerEventDeleted,
            PlayerEventResponseChanged,
            PlayerEventResponseDeleted,
            PlayerEventStateChanged,
            ChatMessageReceived = 90,
            CommunityBoardUpdate = 95,
            CommunityBoardAnnouncementUpdate,
            InventionModerationStateChanged = 100,
            FreeGiftButtonItemsAdded = 110,
            LocalRoomKeyCreated = 120,
            LocalRoomKeyDeleted
        }

        public enum WebsocketMessageType
        {
            DEPRECATED_GameInvite,
            GameInviteDeclined,
            GameJoinFailed,
            DEPRECATED_PartyActivitySwitch,
            FriendInvite,
            VoteToKick,
            GameInviteV2,
            PartyActivitySwitchV2,
            PartyInvite,
            RequestGameInvite = 10,
            RequestGameInviteDeclined,
            FriendStatusOnline = 20,
            TextMessage = 30,
            FriendRequestAccepted = 40,
            FriendCodeUsed,
            PlayerCheer = 50,
            PlayerCheerAnonymous,
            RoomCoOwnerAdded = 60,
            RoomCoOwnerRemoved,
            RoomCoOwnerInvited,
            RoomTranslationCompleted,
            RoomModerationCompleted,
            CreatorPublishedNewRoom = 70,
            PlayerAttendingEvent = 80,
            PlayerEventInvitation,
            DEPRECATED_GroupInvitation = 90,
            DEPRECATED_PlayerJoinedGroup,
            CoachMessage = 100,
            NewRoomComments = 110,
            FriendIntroduction = 130,
            ClubMemberInvited = 200,
            ClubModeratorInvited,
            ClubCoownerInvited,
            RoomEarningsDistributionCoOwnerAdded = 210,
            RoomEarningsDistributionCoOwnerRemoved,
            RoomEarningsDistributionPercentageUpdated,
            ModerationActionApplied = 300,
            VirtualClubAnnouncementRoomPublished = 100000,
            VirtualClubAnnouncementInventionPublished,
            VirtualClubAnnouncementGeneric,
            VirtualClubAnnouncementPlayerEventPublished,
            VirtualClubAnnouncementClub,
            VirtualClubAnnouncementPlayer,
            VirtualClubAnnouncementCode,
            VirtualClubAnnouncementPhoto,
            VirtualRoomNotification,
            VirtualBroadcastingRequest,
            VirtualLeagueAssignment,
            VirtualPartyUpRequest
        }
    }
}