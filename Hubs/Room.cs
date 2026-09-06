using Vanadium.Classes.WebSocket;
using Vanadium.Controllers;
using Newtonsoft.Json;
using static Vanadium.Classes.DBs.DBClasses.RoomDBClasses;

namespace Vanadium.Hubs
{
    public static class RoomHub
    {
        public static async Task BroadcastRoomUpdate(long roomId, object update)
        {
            try
            {
                var message = JsonConvert.SerializeObject(new WebsocketEvents.Response
                {
                    Id = "RoomUpdate",
                    Msg = update
                });

                await NotificationsController.SendToAll(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RoomHub] Error broadcasting room update: {ex.Message}");
            }
        }

        public static async Task NotifyRoomOwners(Room room, object message)
        {
            try
            {
                var json = JsonConvert.SerializeObject(message);
                
                await NotificationsController.SendToPlayer(room.CreatorAccountId, json);

                if (room.Roles != null)
                {
                    foreach (var role in room.Roles.Where(r => r.Role == Role.CoOwner || r.Role == Role.Creator))
                    {
                        await NotificationsController.SendToPlayer(role.AccountId, json);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RoomHub] Error notifying room owners: {ex.Message}");
            }
        }

        public static async Task NotifyRoomSubscribers(long roomId, object message)
        {
            try
            {
                var json = JsonConvert.SerializeObject(message);
                await NotificationsController.SendToAll(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RoomHub] Error notifying room subscribers: {ex.Message}");
            }
        }
    }
}