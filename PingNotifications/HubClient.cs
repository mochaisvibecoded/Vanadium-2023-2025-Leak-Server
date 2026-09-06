namespace UraniumServer.RecNet.Notifications
{
	public class HubClient
	{
		public long AccountId { get; set; }
		public string ConnectionId { get; set; }
		public List<int> Subscriptions { get; set; }
	}
}
