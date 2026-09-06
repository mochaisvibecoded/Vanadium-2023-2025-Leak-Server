using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Controllers;

namespace Vanadium.Classes.Rooms
{
	public class RoomUploadResponse
	{
        internal bool success;
        internal string value;

        //public RoomController.RoomDataResponse Room { get; set; }
		public RoomDBClasses.SubRooms SubRoomDataSave { get; set; }
	}
}
