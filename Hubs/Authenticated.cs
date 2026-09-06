using Microsoft.AspNetCore.SignalR;

namespace Vanadium.Hubs
{
    public abstract class AuthenticatedHub : Hub
    {
        protected long PlayerId
        {
            get
            {
                var raw = Context.UserIdentifier;
                if (raw == null || !long.TryParse(raw, out var id))
                    throw new HubException("Unauthorized");
                return id;
            }
        }

        public override async Task OnConnectedAsync()
        {
            if (Context.UserIdentifier == null)
            {
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, $"player:{PlayerId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"player:{PlayerId}");
            await base.OnDisconnectedAsync(exception);
        }
    }
}