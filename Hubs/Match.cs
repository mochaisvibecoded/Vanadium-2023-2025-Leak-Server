using Vanadium.Classes.WebSocket;
using Vanadium.Controllers;
using Newtonsoft.Json;

namespace Vanadium.Hubs
{
    public static class MatchHub
    {
        public static async Task BroadcastMatchUpdate(long matchId, object update)
        {
            try
            {
                var message = JsonConvert.SerializeObject(new WebsocketEvents.Response
                {
                    Id = "MatchUpdate",
                    Msg = update
                });

                await NotificationsController.SendToAll(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchHub] Error broadcasting match update: {ex.Message}");
            }
        }

        public static async Task BroadcastPlayerHeartbeat(long playerId, object heartbeatData)
        {
            try
            {
                var message = JsonConvert.SerializeObject(new WebsocketEvents.Response
                {
                    Id = "PresenceUpdate",
                    Msg = heartbeatData
                });

                await NotificationsController.SendToPlayer(playerId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchHub] Error broadcasting heartbeat: {ex.Message}");
            }
        }

        public static async Task NotifyPlayerDisconnected(long matchId, long playerId)
        {
            try
            {
                var message = JsonConvert.SerializeObject(new
                {
                    type = "playerDisconnected",
                    playerId = playerId,
                    matchId = matchId
                });

                await NotificationsController.SendToAll(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchHub] Error notifying player disconnect: {ex.Message}");
            }
        }
    }
}