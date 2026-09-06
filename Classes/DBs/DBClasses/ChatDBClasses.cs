using LiteDB;
using Newtonsoft.Json;

namespace Vanadium.Classes.DBs.DBClasses
{
	public class ChatDBClasses
    {
        public class ChatThread
        {
            public List<ChatMessage> Messages { get; set; }
            public long ChatThreadId { get; set; }
            public List<long> PlayerIds { get; set; }
            public long LastReadMessageId { get; set; }
            [JsonIgnore]
            [System.Text.Json.Serialization.JsonIgnore]
            public Dictionary<long, long> PlayerReadStates { get; set; } = new();
            public string ChatThreadName { get; set; }
            public DateTime? SnoozedUntil { get; set; }
            public bool IsFavorited { get; set; }
        }

        public class ChatMessage
        {
            public long ChatMessageId { get; set; }
            public long ChatThreadId { get; set; }
            public int SenderPlayerId { get; set; }
            public DateTime TimeSent {  get; set; }
            public string Contents { get; set; }
            public int ModerationState { get; set; }
        }

        public class MessageJson
        {
            public MessageContentType Type { get; set; }
            public int Version { get; set; }
            public string Data { get; set; }
        }

        public struct SendMessageResponse
        {
            public ChatResults ChatResult { get; set; }
            public ChatThread ChatThread { get; set; }
        }

        public enum ChatResults
        {
            Success,
            InvalidArguments,
            ThreadNotFound,
            MembershipNotFound,
            PlayerAlreadyOnThread,
            CannotMessagePlayer,
            InvalidCharacters,
            RecentlyLeftThread,
            ThreadTooLarge
        }

        public enum ModerationState : byte
        {
            Active,
            Junior_Pending = 11,
            Moderation_Pending = 100,
            Moderation_Closed,
            Moderation_Banned,
            MarkedForDelete = 255
        }

        public enum QueryMode
        {
            Latest,
            NewerThan,
            OlderThan
        }

        public enum MessageContentType
        {
            Text,
            PartyInvite,
            Photo
        }
    }
}