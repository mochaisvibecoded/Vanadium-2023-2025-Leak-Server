using LiteDB;

namespace Vanadium.Classes.DBs
{
    public class ActionLinkDB
    {
        public class ActionLink
        {
            public int Id { get; set; }
            public string Code { get; set; } = "";
            public long CreatorPlayerId { get; set; }
            public string Data { get; set; } = "";
            public int CodeType { get; set; }
            public int ExtraDataId { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class ActionLinkResponse
		{
    		public bool isValid { get; set; }
    		public int codeType { get; set; }
    		public string extraJson { get; set; } = "";
   		 	public long creatorPlayerId { get; set; }
    		public string data { get; set; } = "";
		}

        public static LiteDatabase ActionLinkDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "ActionLinks.db") + ";Connection=shared");
        public static readonly ILiteCollection<ActionLink> ActionLinks = ActionLinkDBFile.GetCollection<ActionLink>("ActionLinks");

        static ActionLinkDB()
        {
            ActionLinks.EnsureIndex(x => x.Code);
            ActionLinks.EnsureIndex(x => x.CreatorPlayerId);
            ActionLinks.EnsureIndex(x => x.ExpiresAt);
        }

        private static string GenerateCode()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz";
            return new string(Enumerable.Range(0, 9).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
        }

        public static ActionLink? CreateActionLink(long creatorPlayerId, string data, int codeType, int extraDataId, int validHours)
        {
            if (PlayerDB.Players.FindById(creatorPlayerId) == null)
                return null;

            if (codeType == 5 && !RoomDB.DoesRoomExist(extraDataId))
                return null;

            string code;
            do { code = GenerateCode(); } while (ActionLinks.FindOne(x => x.Code == code) != null);

            var link = new ActionLink
            {
                Code = code,
                CreatorPlayerId = creatorPlayerId,
                Data = data,
                CodeType = codeType,
                ExtraDataId = extraDataId,
                ExpiresAt = DateTime.UtcNow.AddHours(validHours),
                CreatedAt = DateTime.UtcNow
            };

            ActionLinks.Insert(link);
            return link;
        }

        public static ActionLinkResponse GetActionLinkResponse(string code)
		{
		    var invalid = new ActionLinkResponse { isValid = false, codeType = 0, extraJson = "", creatorPlayerId = 0, data = "" };
		    var link = ActionLinks.FindOne(x => x.Code == code);
		    if (link == null || DateTime.UtcNow > link.ExpiresAt) return invalid;
		    if (PlayerDB.Players.FindById(link.CreatorPlayerId) == null) return invalid;
		    if (link.CodeType == 5 && !RoomDB.DoesRoomExist(link.ExtraDataId)) return invalid;
		    return new ActionLinkResponse
    		{
        		isValid = true,
        		codeType = link.CodeType,
        		extraJson = link.Data,
        		creatorPlayerId = link.CreatorPlayerId,
        		data = link.Data
    		};
		}
    }
}