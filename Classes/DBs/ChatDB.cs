using LiteDB;
using Vanadium.Classes;
using Vanadium.Classes.DBs.DBClasses;
using System;
using System.Collections.Generic;
using System.Linq;
using static Vanadium.Classes.DBs.DBClasses.ChatDBClasses;
using System.Text.Json;
using Vanadium.Controllers;

namespace Vanadium.Classes.DBs
{
        public class ChatDB
        {
            private static readonly Lazy<LiteDatabase> _dbLazy = new(() =>
    		new LiteDatabase(new ConnectionString
    		{
        		Filename = Path.Combine(Environment.CurrentDirectory, "Data", "DBs", "Chat.db"),
        		Connection = ConnectionType.Shared
    		}));

            public static LiteDatabase ChatDBFile => _dbLazy.Value;

            private static ILiteCollection<ChatThread> Threads => ChatDBFile.GetCollection<ChatThread>("threads");
            private static ILiteCollection<ChatMessage> Messages => ChatDBFile.GetCollection<ChatMessage>("messages");

            public static void Initialize()
            {
                Threads.EnsureIndex(x => x.ChatThreadId, unique: true);
                Threads.EnsureIndex(x => x.PlayerIds);
                Messages.EnsureIndex(x => x.ChatThreadId);
                Messages.EnsureIndex(x => x.ChatMessageId, unique: true);
            }

            public static ChatThread CreateThread(List<long> memberIds, long creatorid, string? name, out bool createdNew)
            {
                // existing
                var normalizedMemberIds = memberIds
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                var existingThread = Threads.FindAll().FirstOrDefault(t =>
                    t.PlayerIds != null &&
                    t.PlayerIds.Distinct().OrderBy(x => x).SequenceEqual(normalizedMemberIds));

                if (existingThread != null)
                {
                    createdNew = false;
                    return GetThread(existingThread.ChatThreadId)!;
                }
                // create ig

                        long newThreadId = Threads.Count() + 1;

                        ChatThread thread = new ChatThread
                        {
                            ChatThreadId = newThreadId,
                            PlayerIds = normalizedMemberIds,
                            ChatThreadName = name ?? string.Empty,
                            IsFavorited = false,
                            SnoozedUntil = null,
                            LastReadMessageId = 1,
                            Messages = new List<ChatMessage>()
                        };

                        Threads.Insert(thread);

                        var messageJson = new MessageJson
                        {
                            Type = MessageContentType.Text,
                            Version = 1,
                            Data = $"Player <@U{creatorid}> started a chat"
                        };

                        string json = System.Text.Json.JsonSerializer.Serialize(messageJson);

                        AddMessage(
                            threadId: newThreadId,
                            senderId: -5,
                            jsonContents: json
                        );

                        createdNew = true;
                        return GetThread(newThreadId)!;
            }


            public static ChatMessage AddMessage(long threadId, int senderId, string jsonContents)
            {
                var thread = GetThread(threadId);
                if (thread == null)
                    return null;

                long newMessageId = Messages.Count() + 1;

                ChatMessage msg = new ChatMessage
                {
                    ChatMessageId = newMessageId,
                    ChatThreadId = threadId,
                    SenderPlayerId = senderId,
                    TimeSent = DateTime.UtcNow,
                    Contents = jsonContents,
                    ModerationState = 0
                };

                Messages.Insert(msg);

                thread.Messages.Add(msg);
                thread.LastReadMessageId = newMessageId;
                Threads.Update(thread);

                return msg;
            }

            public static ChatThread RenameThread(long threadId, long senderId, string NewThreadName)
            {
                var thread = GetThread(threadId);
                if (thread == null)
                    return null;

                thread.ChatThreadName = NewThreadName;

                var messageJson = new MessageJson
                {
                    Type = MessageContentType.Text,
                    Version = 1,
                    Data = $"Player <@U{senderId}> renamed the chat"
                };

                string json = System.Text.Json.JsonSerializer.Serialize(messageJson);

                var newmsg = AddMessage(
                    threadId: threadId,
                    senderId: -5,
                    jsonContents: json
                );

                var payload = new
                {
                    Id = "ChatMessageReceived",
                    Msg = new
                    {
                        chatMessageId = newmsg.ChatMessageId,
                        chatThreadId = newmsg.ChatThreadId,
                        senderPlayerId = newmsg.SenderPlayerId,
                        timeSent = newmsg.TimeSent,
                        contents = newmsg.Contents,
                        moderationState = newmsg.ModerationState
                    }
                };

                NotificationsController.SendToPlayers(thread.PlayerIds, System.Text.Json.JsonSerializer.Serialize(payload));
                Threads.Update(thread);
                return GetThread(threadId);
            }

            public static ChatThread LeaveThread(long threadId, long senderId)
            {
                var thread = GetThread(threadId);
                if (thread == null)
                    return null;

                thread.PlayerIds.Remove(senderId);
                var messageJson = new MessageJson
                {
                    Type = MessageContentType.Text,
                    Version = 1,
                    Data = $"Player <@U{senderId}> left"
                };

                string json = System.Text.Json.JsonSerializer.Serialize(messageJson);

                var newmsg = AddMessage(
                    threadId: threadId,
                    senderId: -5,
                    jsonContents: json
                );

                var payload = new
                {
                    Id = "ChatMessageReceived",
                    Msg = new
                    {
                        chatMessageId = newmsg.ChatMessageId,
                        chatThreadId = newmsg.ChatThreadId,
                        senderPlayerId = newmsg.SenderPlayerId,
                        timeSent = newmsg.TimeSent,
                        contents = newmsg.Contents,
                        moderationState = newmsg.ModerationState
                    }
                };

                NotificationsController.SendToPlayers(thread.PlayerIds, System.Text.Json.JsonSerializer.Serialize(payload));

                Threads.Update(thread);
                return GetThread(threadId);
            }

            public static ChatThread? GetThread(long threadId)
            {
                var thread = Threads.FindOne(x => x.ChatThreadId == threadId);
                if (thread == null) return null;

                thread.Messages = Messages.Find(x => x.ChatThreadId == threadId).OrderBy(x => x.ChatMessageId).ToList();
                return thread;
            }

            public static List<ChatThread> GetThreadsForPlayer(long playerId, int maxCount)
            {
                var list = Threads.Find(x => x.PlayerIds.Contains(playerId)).OrderByDescending(x => x.LastReadMessageId).Take(maxCount).ToList();

                foreach (var t in list)
                {
                    t.Messages = Messages.Find(x => x.ChatThreadId == t.ChatThreadId).OrderByDescending(x => x.ChatMessageId).Take(1).ToList();
                }

                return list;
            }

        public static object? SetThreadFavorited(long threadId, long playerId, bool favorite)
        {
            var thread = Threads.FindOne(x => x.ChatThreadId == threadId);
            if (thread == null) return null;
            if (!thread.PlayerIds.Contains(playerId)) return null;
            thread.IsFavorited = favorite;
            Threads.Update(thread);
            thread = GetThread(threadId);
            if (thread == null) return null;

            return new
            {
                latestMessage = thread.Messages.Count > 0 ? new
                {
                    chatMessageId = thread.Messages.Last().ChatMessageId,
                    chatThreadId = thread.Messages.Last().ChatThreadId,
                    senderPlayerId = thread.Messages.Last().SenderPlayerId,
                    timeSent = thread.Messages.Last().TimeSent,
                    contents = thread.Messages.Last().Contents,
                    moderationState = thread.Messages.Last().ModerationState
                } : (object?)null,
                chatThreadId = thread.ChatThreadId,
                playerIds = thread.PlayerIds,
                lastReadMessageId = thread.LastReadMessageId,
                chatThreadName = thread.ChatThreadName,
                chatThreadType = 0,
                snoozedUntil = thread.SnoozedUntil,
                isFavorited = thread.IsFavorited
            };
        }

        public static bool SetLastReadMessage(long threadId, long playerId, long messageId)
            {
                var thread = Threads.FindOne(x => x.ChatThreadId == threadId);
                if (thread == null) return false;

                if (!thread.PlayerIds.Contains(playerId)) return false;

                thread.LastReadMessageId = messageId;
                return Threads.Update(thread);
            }

            public static ChatThread AddMemberToThread(long threadId, long senderId, long memberId)
            {
                var thread = GetThread(threadId);
                if (thread == null)
                    return null;

                thread.PlayerIds.Add(memberId);
                var messageJson = new MessageJson
                {
                    Type = MessageContentType.Text,
                    Version = 1,
                    Data = $"Player <@U{memberId}> was added by <@U{senderId}>"
                };

                string json = System.Text.Json.JsonSerializer.Serialize(messageJson);

                var newmsg = AddMessage(
                    threadId: threadId,
                    senderId: -5,
                    jsonContents: json
                );

                var payload = new
                {
                    Id = "ChatMessageReceived",
                    Msg = new
                    {
                        chatMessageId = newmsg.ChatMessageId,
                        chatThreadId = newmsg.ChatThreadId,
                        senderPlayerId = newmsg.SenderPlayerId,
                        timeSent = newmsg.TimeSent,
                        contents = newmsg.Contents,
                        moderationState = newmsg.ModerationState
                    }
                };

                NotificationsController.SendToPlayers(thread.PlayerIds, System.Text.Json.JsonSerializer.Serialize(payload));

                Threads.Update(thread);
                return GetThread(threadId);
            }
    }
}