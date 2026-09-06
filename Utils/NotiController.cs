using Vanadium.Controllers;

namespace Vanadium.Utils.NotiController // i made this for things that still use NotiController in other controllers.
{
    public static class NotiController
    {
        public static Task SendEvent(long playerId, string eventType, object payload)
            => NotificationsController.SendEvent(playerId, eventType, payload);
        public static Task SendAccountUpdate(long playerId, object? accountData)
            => NotificationsController.SendAccountUpdate(playerId, accountData);
        public static Task BroadcastAccountUpdate(long playerId, object? publicDto)
            => NotificationsController.BroadcastAccountUpdate(playerId, publicDto);
        public static Task SendRoomUpdate(long playerId, object room)
            => NotificationsController.SendRoomUpdate(playerId, room);
        public static Task SendPresenceUpdate(long playerId, object heartbeat)
            => NotificationsController.SendPresenceUpdate(playerId, heartbeat);
        public static Task SendReputationUpdate(long playerId, object reputation)
            => NotificationsController.SendReputationUpdate(playerId, reputation);
        public static Task SendToAll(string message)
            => NotificationsController.SendToAll(message);
        public static Task SendToPlayer(long playerId, string message)
    		=> NotificationsController.SendEvent(playerId, "Notification", message);
        public static Task SendCoahMessage(string message)
    		=> NotificationsController.SendCoahMessage(message);
        public static Task SendMaintenance(int minutes)
    		=> NotificationsController.SendMaintenance(minutes);

        public static class EventTypes
        {
            public const string AccountUpdate = NotificationsController.EventTypes.AccountUpdate;
            public const string SelfAccountUpdate = NotificationsController.EventTypes.SelfAccountUpdate;
            public const string PresenceUpdate = NotificationsController.EventTypes.PresenceUpdate;
            public const string RoomUpdate = NotificationsController.EventTypes.RoomUpdate;
            public const string RoomInstanceUpdate = NotificationsController.EventTypes.RoomInstanceUpdate;
            public const string ReputationUpdate = NotificationsController.EventTypes.ReputationUpdate;
            public const string PlayerProgressionLevelUpdate = NotificationsController.EventTypes.PlayerProgressionLevelUpdate;
            public const string PhotonAccessToken = NotificationsController.EventTypes.PhotonAccessToken;
            public const string InfluencerSupportedUpdate = NotificationsController.EventTypes.InfluencerSupportedUpdate;
        }
    }
}