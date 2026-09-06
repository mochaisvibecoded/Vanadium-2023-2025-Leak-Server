using Vanadium.Controllers;
using Newtonsoft.Json;

namespace Vanadium.Hubs
{
    public static class DiscordBotHub
    {
        public static async Task BroadcastBotEvent(string eventType, object payload)
        {
            try
            {
                var message = JsonConvert.SerializeObject(new
                {
                    type = "botEvent",
                    eventType = eventType,
                    data = payload,
                    timestamp = DateTime.UtcNow
                });

                await NotificationsController.SendToAll(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DiscordBotHub] Error broadcasting bot event: {ex.Message}");
            }
        }

        public static async Task BroadcastBotMessage(string message)
        {
            try
            {
                var json = JsonConvert.SerializeObject(new
                {
                    type = "botMessage",
                    message = message,
                    timestamp = DateTime.UtcNow
                });

                await NotificationsController.SendToAll(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DiscordBotHub] Error broadcasting bot message: {ex.Message}");
            }
        }
    }
}