using Vanadium.Classes.DBs;
using Vanadium.Controllers;
using Newtonsoft.Json;

namespace Vanadium.Hubs
{
    public static class ApiHub
    {
        public static async Task GetFriendsList(long playerId)
        {
            try
            {
                var friends = FriendsDB.GetRelationships(playerId);
                var message = JsonConvert.SerializeObject(new
                {
                    target = "FriendsListResult",
                    data = friends
                });

                await NotificationsController.SendToPlayer(playerId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ApiHub] Error getting friends list: {ex.Message}");
            }
        }

        public static async Task GetRelationships(long playerId)
        {
            try
            {
                var player = PlayerDB.Players.FindById(playerId);
                var relationships = player?.Player?.Relationships ?? new();

                var message = JsonConvert.SerializeObject(new
                {
                    target = "RelationshipsResult",
                    data = relationships
                });

                await NotificationsController.SendToPlayer(playerId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ApiHub] Error getting relationships: {ex.Message}");
            }
        }

        public static async Task SubscribeFriendUpdates(long playerId)
        {
            try
            {
                var response = JsonConvert.SerializeObject(new
                {
                    type = 3,
                    result = (object?)null
                });

                await NotificationsController.SendToPlayer(playerId, response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ApiHub] Error subscribing to friend updates: {ex.Message}");
            }
        }

        public static async Task SubscribeRelationshipUpdates(long playerId)
        {
            try
            {
                var response = JsonConvert.SerializeObject(new
                {
                    type = 3,
                    result = (object?)null
                });

                await NotificationsController.SendToPlayer(playerId, response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ApiHub] Error subscribing to relationship updates: {ex.Message}");
            }
        }

        public static async Task NotifyFriendRequest(long playerId, long fromPlayerId, string fromPlayerName)
        {
            try
            {
                var message = JsonConvert.SerializeObject(new
                {
                    type = "friendRequest",
                    from = fromPlayerId,
                    fromName = fromPlayerName,
                    timestamp = DateTime.UtcNow
                });

                await NotificationsController.SendToPlayer(playerId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ApiHub] Error notifying friend request: {ex.Message}");
            }
        }

        public static async Task NotifyRelationshipChanged(long playerId, long targetPlayerId, string relationshipType, string action)
        {
            try
            {
                var message = JsonConvert.SerializeObject(new
                {
                    type = "relationshipChanged",
                    targetId = targetPlayerId,
                    relationshipType = relationshipType,
                    action = action,
                    timestamp = DateTime.UtcNow
                });

                await NotificationsController.SendToPlayer(playerId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ApiHub] Error notifying relationship change: {ex.Message}");
            }
        }
    }
}