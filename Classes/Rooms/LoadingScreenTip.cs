namespace Vanadium.Classes.Rooms
{
	public class LoadingScreenTip
	{
		public int PlatformMask { get; set; } = -1;
		public string Title { get; set; }
		public string Message { get; set; }
		public List<string> RoomNames { get; set; }
		public string ImageName { get; set; }
	}
}
