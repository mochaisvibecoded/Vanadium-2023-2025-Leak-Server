using Vanadium.Classes.WebSocket;
using Newtonsoft.Json;
using Vanadium.Controllers;

namespace Vanadium.Hubs
{
    public static class NotificationHub
    {
        public static async Task SubscribeToPlayers()
        {
            await Task.CompletedTask;
        }

        public static async Task SubscribeToRooms()
        {
            await Task.CompletedTask;
        }

        public static async Task SubscribeToEvents()
        {
            await Task.CompletedTask;
        }

        public static async Task SendNotificationToPlayer(long playerId, object notification)
        {
            try
            {
                var message = JsonConvert.SerializeObject(notification);
                await NotificationsController.SendToPlayer(playerId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationHub] Error sending notification: {ex.Message}");
            }
        }

        public static async Task SendNotificationToAll(object notification)
        {
            try
            {
                var message = JsonConvert.SerializeObject(notification);
                await NotificationsController.SendToAll(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationHub] Error broadcasting notification: {ex.Message}");
            }
        }
    }
}