//using Discord.Net;
//using Microsoft.AspNetCore.SignalR;
//using VanaNet.Auth;
//using System.Net;
//using System.Security.Principal;
//using UraniumServer.RecNet.Notifications;

//namespace UraniumServer.API.Notifications
//{
//	public class NotifyHub : Hub
//	{
//		public NotifyHub(IHubContext<NotifyHub> hubContext) { 
//			Notify.Context = hubContext;
//		}

//		[HubMethodName("SubscribeToPlayers")]
//		public async Task Subscribe(SubscriptionUpdate update)
//		{
//			var account = AuthStuff.GetPlayerId(Context.GetHttpContext().Request);
//			if (account == null)
//				return;

//			List<int> Subscriptions = update.PlayerIds;
//			var client = Notify.Clients.FirstOrDefault(n => n.ConnectionId == Context.ConnectionId);
//			if (client != null)
//			{
//				client.Subscriptions = Subscriptions;
//				await Notify.UpdateSubscriptions((long)account);
//			}
//		}

//		public override Task OnConnectedAsync()
//		{
//			var account = AuthStuff.GetPlayerId(Context.GetHttpContext().Request);
//			if (account == null)
//				return base.OnConnectedAsync();

//			Console.WriteLine("Player connected!");
//			Notify.Clients.Add(new HubClient
//			{
//				AccountId = (long)account,
//				ConnectionId = Context.ConnectionId,
//				Subscriptions = new List<int>()
//			});
//			return base.OnConnectedAsync();
//		}

//		public override async Task OnDisconnectedAsync(Exception exception)
//		{
//			var userId = AuthStuff.GetPlayerId(Context.GetHttpContext().Request);
//			if (userId != null)
//			{
//				/* set isOnline to false from presence here skyfire -- pingvin (14/05/2026) */
//			}
//			Notify.Clients.Remove(Notify.Clients.FirstOrDefault(n => n.ConnectionId == Context.ConnectionId));
//			await base.OnDisconnectedAsync(exception);
//		}
//	}
//}
