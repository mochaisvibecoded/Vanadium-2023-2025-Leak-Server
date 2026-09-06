//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using System.Text.Json;
//using UraniumServer.DB;
//using UraniumServer.Extensions;
//using UraniumServer.RecNet.Accounts;
//using UraniumServer.RecNet.Auth;
//using UraniumServer.RecNet.Enums.Messages;
//using UraniumServer.RecNet.Messages;
//using UraniumServer.RecNet.Notifications;
//using UraniumServer.RecNet.Rooms;
//using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

//namespace UraniumServer.API.Notifications
//{
//	public static class Notify
//	{
//		public static IDbContextFactory<RecDBContext> DbFactory;
//		public static IHubContext<NotifyHub> Context;
//		public static List<HubClient> Clients = new List<HubClient>();

//		public static async Task SendMessage(long TargetId, Message msg)
//		{
//			await using var ctx = await DbFactory.CreateDbContextAsync();

//			msg.SentTime = DateTime.UtcNow;
//			msg.ToPlayerId = TargetId;

//			await ctx.Messages.AddAsync(msg);
//			await ctx.SaveChangesAsync();

//			var client = Notify.Clients.FirstOrDefault(n => n.AccountId == TargetId);
//			if (client == null) { Console.WriteLine("invalid client :("); return; }

//			string connectionId = client.ConnectionId;
//			var notif = new Notification { Id = NotificationType.MessageReceived, Msg = msg };
//			await Notify.Context.Clients.Client(connectionId).SendAsync("Notification", notif.ToJson());
//		}

//		public static async Task SendNotify(long AccountId, Notification Message)
//		{
//			var client = Notify.Clients.FirstOrDefault(n => n.AccountId == AccountId);
//			if (client == null) { Console.WriteLine("invalid client :("); return; }

//			string connectionId = client.ConnectionId;
//			await Notify.Context.Clients.Client(connectionId).SendAsync("Notification", Message.ToJson());
//		}

//		public static async Task UpdateEveryone(long Player)
//		{
//			await using var ctx = await DbFactory.CreateDbContextAsync();

//			var client = Notify.Clients.FirstOrDefault(n => n.AccountId == Player);
//			if (client == null) { Console.WriteLine("invalid client :("); return; }

//			foreach (var S in Clients.ToList())
//			{
//				await Notify.SendStatusTo(Player, S.AccountId);
//			}
//		}
//		public static async Task UpdateSubscriptions(long Player)
//		{
//			await using var ctx = await DbFactory.CreateDbContextAsync();

//			var client = Notify.Clients.FirstOrDefault(n => n.AccountId == Player);
//			if (client == null) { Console.WriteLine("invalid client :("); return; }

//			List<int> Subscriptions = client.Subscriptions;
//			foreach (int S in Subscriptions)
//			{
//				await Notify.SendStatusTo(Player, S);
//			}
//		}
//		public static async Task UpdateSubscriptions(long Player, object Id, object Message)
//		{
//			await using var ctx = await DbFactory.CreateDbContextAsync();

//			var client = Notify.Clients.FirstOrDefault(n => n.AccountId == Player);
//			if (client == null) { Console.WriteLine("invalid client :("); Notify.Clients.RemoveAll(x => x == null); return; }

//			List<int> Subscriptions = client.Subscriptions;
//			foreach (int S in Subscriptions)
//			{
//				await Notify.SendStatusTo(Player, S, Id, Message);
//			}
//		}

//		private static async Task SendStatusTo(long From, long To)
//		{
//			await using var ctx = await DbFactory.CreateDbContextAsync();

//			Profile account = await ctx.Profiles.FirstOrDefaultAsync(x => x.accountId == From);
//			var realPresence = await ctx.Presences.FirstOrDefaultAsync(x => x.playerId == From);
//			RoomInstance? roomInstance = await ctx.RoomInstances.FirstOrDefaultAsync(x => x.roomInstanceId == realPresence.roomInstance);
//			Presence playerPresence = new Presence(realPresence, roomInstance);

//			Notification notification = new Notification();
//			notification.Id = "PresenceUpdate";
//			notification.Msg = playerPresence;
//			//await Notify.SendNotify(From, notification);
//			await Notify.SendNotify(To, notification);

//			Notification notification3 = new Notification();
//			notification3.Id = "AccountUpdate";
//			notification3.Msg = account;
//			//await Notify.SendNotify(From, notification3);
//			await Notify.SendNotify(To, notification3);
//		}
//		private static async Task SendStatusTo(long From, long To, object Id, object Message)
//		{
//			await using var ctx = await DbFactory.CreateDbContextAsync();

//			Profile account = await ctx.Profiles.FirstOrDefaultAsync(x => x.accountId == From);
//			var realPresence = await ctx.Presences.FirstOrDefaultAsync(x => x.playerId == From);
//			RoomInstance? roomInstance = await ctx.RoomInstances.FirstOrDefaultAsync(x => x.roomInstanceId == realPresence.roomInstance);
//			Presence playerPresence = new Presence(realPresence, roomInstance);

//			await Notify.SendNotify(From, new Notification { Id = Id, Msg = Message });
//			await Notify.SendNotify(To, new Notification { Id = Id, Msg = Message });
//		}
//	}
//}
