using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Vanadium.Auth;
using Vanadium.Classes.DBs;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using static Vanadium.Classes.DBs.DBClasses.ChatDBClasses;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.FriendsDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using Vanadium.Controllers;

namespace Vanadium.Controllers
{
	[Route("chat")]
    public class ChatController : ControllerBase
    {
        [HttpGet("thread")]
        public IActionResult GetThreads([FromQuery] int maxCount = 50, int mode = 0)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
                
            var threads = ChatDB.GetThreadsForPlayer((long)id, 50);

            var response = threads.Select(t => new
            {
                latestMessage = t.Messages.Count > 0 ? new
                {
                    chatMessageId = t.Messages[0].ChatMessageId,
                    chatThreadId = t.ChatThreadId,
                    senderPlayerId = t.Messages[0].SenderPlayerId,
                    timeSent = t.Messages[0].TimeSent,
                    contents = t.Messages[0].Contents,
                    moderationState = t.Messages[0].ModerationState
                } : null,

                chatThreadId = t.ChatThreadId,
                playerIds = t.PlayerIds,
                lastReadMessageId = t.LastReadMessageId,
                chatThreadName = t.ChatThreadName,
                chatThreadType = 0,
                snoozedUntil = t.SnoozedUntil,
                isFavorited = t.IsFavorited
            });

            return new ContentResult()
            {
                Content = JsonConvert.SerializeObject(response),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("thread/{id}")]
        public IActionResult GetThreadById(long id, [FromQuery] int maxCount = 50, [FromQuery] int mode = 0)
        {
           var pid = AuthStuff.GetPlayerId(Request);
            if (pid == null)
                return Unauthorized("");

            var thread = ChatDB.GetThread(id);

            if (thread == null)
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 200
                };
            }
            
            if (!thread.PlayerIds.Contains((long)pid))
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 403
                };
            }
            

            var latestMessage = thread.Messages.Count > 0
                ? thread.Messages.Last()
                : null;

            var response = new
            {
                latestMessage = latestMessage != null ? new
                {
                    chatMessageId = latestMessage.ChatMessageId,
                    chatThreadId = latestMessage.ChatThreadId,
                    senderPlayerId = latestMessage.SenderPlayerId,
                    timeSent = latestMessage.TimeSent,
                    contents = latestMessage.Contents,
                    moderationState = latestMessage.ModerationState
                } : null,

                chatThreadId = thread.ChatThreadId,
                playerIds = thread.PlayerIds,
                lastReadMessageId = thread.LastReadMessageId,
                chatThreadName = thread.ChatThreadName,
                chatThreadType = 0,
                snoozedUntil = thread.SnoozedUntil,
                isFavorited = thread.IsFavorited,

                messages = thread.Messages
                    .OrderByDescending(m => m.ChatMessageId)
                    .Take(maxCount)
                    .OrderBy(m => m.ChatMessageId)
                    .Select(m => new
                    {
                        chatMessageId = m.ChatMessageId,
                        chatThreadId = m.ChatThreadId,
                        senderPlayerId = m.SenderPlayerId,
                        timeSent = m.TimeSent,
                        contents = m.Contents,
                        moderationState = m.ModerationState
                    })
            };

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(response),
                ContentType = "application/json",
                StatusCode = 200
            };
        }



        private const string NvidiaAPIKey = "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"; private const string NvidiaAPIEndpoint = "https://integrate.api.nvidia.com/v1/chat/completions";
        private const int CoachHistoryMessageLimit = 20;
        private static readonly HttpClient _httpClient = new HttpClient();
        private static string? _cachedSystemPrompt = null;

        private static bool TryExtractCoachContent(string? rawContents, out string content)
        {
            content = string.Empty;
            if (string.IsNullOrWhiteSpace(rawContents))
                return false;

            try
            {
                var parsedContents = Newtonsoft.Json.Linq.JObject.Parse(rawContents);
                var data = parsedContents["Data"]?.ToString();
                if (string.IsNullOrWhiteSpace(data))
                    return false;

                if (data.StartsWith("<=>", StringComparison.Ordinal))
                    data = data.Substring(3);

                content = data.Trim();
                return !string.IsNullOrWhiteSpace(content);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                content = rawContents.Trim();
                return !string.IsNullOrWhiteSpace(content);
            }
        }

        internal static async Task RequestNvidiaAPIResponseAndSend(string message, long threadId, string? currentUsername)
        {
            var systemPromptPath = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "systemprompt.txt");
            _cachedSystemPrompt ??= System.IO.File.ReadAllText(systemPromptPath) + "\n\n[Additional Instructions]\nMessages preceding with different: \"[@{Username}]: are seperate players!";

            Console.WriteLine($"Requesting NVIDIA API response for thread {threadId}.");

            var conversationThread = ChatDB.GetThread(threadId);
            var messages = new List<object>
            {
                new { role = "system", content = _cachedSystemPrompt }
            };

            if (conversationThread != null)
            {
                var historicalMessages = conversationThread.Messages
                    .Where(m => m.SenderPlayerId != -5)
                    .OrderByDescending(m => m.ChatMessageId)
                    .Take(CoachHistoryMessageLimit)
                    .OrderBy(m => m.ChatMessageId);

                foreach (var historicalMessage in historicalMessages)
                {
                    if (!TryExtractCoachContent(historicalMessage.Contents, out var historicalContent))
                        continue;

                    var role = historicalMessage.SenderPlayerId == 1 ? "assistant" : "user";
                    messages.Add(new { role, content = $"[@{PlayerDB.GetPlayerDTOById(historicalMessage.SenderPlayerId)?.username}]:{historicalContent}" });
                }
            }

            if (messages.Count == 1 && !string.IsNullOrWhiteSpace(message))
            {
                messages.Add(new { role = "user", content = $"[@{currentUsername}]:{message.Trim()}" });
            }

            var requestPayload = new
            {
                model = "nvidia/nemotron-3.5-lightning-30b-a3b",
                messages,
                temperature = 1,
                top_p = 0.95,
                max_tokens = 16384,
                stream = false,
                chat_template_kwargs = new
                {
                    enable_thinking = true
                },
                reasoning_budget = 16384
            };

            var jsonPayload = JsonConvert.SerializeObject(requestPayload);
            var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
            Console.WriteLine($"Sending request now.");

            if (!_httpClient.DefaultRequestHeaders.Contains("Authorization"))
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {NvidiaAPIKey}");

            var response = await _httpClient.PostAsync(NvidiaAPIEndpoint, content);
            Console.WriteLine($"Status code: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseContent);
                string? generatedResponse = jsonResponse?.choices?[0]?.message?.content?.ToString().Replace("[@Coach]:", "").Trim();
                Console.WriteLine($"Received response from NVIDIA API for thread {threadId}: {generatedResponse}");

                string responseMessage = JsonConvert.SerializeObject(new
                {
                    Type = 0,
                    Version = 2,
                    Data = "<=>" + (generatedResponse ?? "Coach chat is disabled.") // fuck off 
                });
                ChatDB.AddMessage(threadId, 1, responseMessage);
                var updatedThread = ChatDB.GetThread(threadId);
                if (updatedThread == null || updatedThread.Messages.Count == 0)
                    return;

                var lastMessage = updatedThread.Messages.Last();

                var payload = new
                {
                    Id = "ChatMessageReceived",
                    Msg = new
                    {
                        chatMessageId = lastMessage.ChatMessageId,
                        chatThreadId = lastMessage.ChatThreadId,
                        senderPlayerId = lastMessage.SenderPlayerId,
                        timeSent = lastMessage.TimeSent,
                        contents = lastMessage.Contents,
                        moderationState = lastMessage.ModerationState
                    }
                };

                await NotificationsController.SendToPlayers(updatedThread.PlayerIds, JsonConvert.SerializeObject(payload));
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                ChatDB.AddMessage(threadId, 1, "{\"Type\":0,\"Version\":2,\"Data\":\"<=>Coach chat is disabled.\"}");
            }
        }
        [HttpPost("thread/{id}")]
        public async Task<IActionResult> SendMessageToThread(long id)
        {
            var pid = AuthStuff.GetPlayerId(Request);
            if (pid == null)
                return Unauthorized("");
                
            if (pid == 64)
            	return Ok();
            
            string? contents = HttpContext.Request.Query["messageContents"];
            if (string.IsNullOrWhiteSpace(contents))
                contents = HttpContext.Request.Form["messageContents"];
            var actualcontents = System.Net.WebUtility.UrlDecode(contents);
            var json = Newtonsoft.Json.Linq.JObject.Parse(contents);

            string actualMessage = json["Data"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(contents))
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 400
                };
            }

            var thread = ChatDB.GetThread(id);
            if (thread == null)
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 400
                };
            }

            if (!thread.PlayerIds.Contains((long)pid))
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 403
                };
            }
            
            var msg = ChatDB.AddMessage(id, (int)pid, contents);
            if (msg == null)
            {
                return new ContentResult
                {
                    Content = JsonConvert.SerializeObject(new { chatResult = 5, chatThread = (object)null }),
                    ContentType = "application/json",
                    StatusCode = 200
                };
            }
            if (contents.ToLower().Contains("@coach"))
            {
                Console.WriteLine($"Sending message to Nvidia API: {actualMessage}");
                _ = RequestNvidiaAPIResponseAndSend(actualMessage, id, AuthStuff.GetCurrentPlayer(Request)?.Player?.Username);
            }

            thread = ChatDB.GetThread(id);

            var messages = thread.Messages
                .OrderByDescending(m => m.ChatMessageId)
                .Select(m => new
                {
                    chatMessageId = m.ChatMessageId,
                    chatThreadId = m.ChatThreadId,
                    senderPlayerId = m.SenderPlayerId,
                    timeSent = m.TimeSent,
                    contents = m.Contents,
                    moderationState = m.ModerationState
                })
                .ToList();

            var response = new
            {
                chatResult = 0,
                chatThread = new
                {
                    messages = messages,
                    chatThreadId = thread.ChatThreadId,
                    playerIds = thread.PlayerIds,
                    lastReadMessageId = thread.LastReadMessageId,
                    chatThreadName = thread.ChatThreadName,
                    chatThreadType = 0,
                    snoozedUntil = thread.SnoozedUntil,
                    isFavorited = thread.IsFavorited
                }
            };

            var payload = new
            {
                Id = "ChatMessageReceived",
                Msg = new
                {
                    chatMessageId = msg.ChatMessageId,
                    chatThreadId = msg.ChatThreadId,
                    senderPlayerId = msg.SenderPlayerId,
                    timeSent = msg.TimeSent,
                    contents = msg.Contents,
                    moderationState = msg.ModerationState
                }
            };
            
            await NotificationsController.SendToPlayers(thread.PlayerIds, JsonConvert.SerializeObject(payload));

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(response),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        internal static async Task<string> RequestNvidiaAPIResponseAndReturnString(string message)
        {
            var systemPromptPath = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "systemprompt.txt");
            _cachedSystemPrompt ??= System.IO.File.ReadAllText(systemPromptPath) + "\n\n[Additional Instructions]\nMessages preceding with different: \"[@{Username}]: are seperate players!";

            Console.WriteLine($"Requesting NVIDIA API response.");

            var requestPayload = new
            {
                model = "nvidia/nemotron-3.5-lightning-30b-a3b",
                messages = new[]
                {
                    new { role = "system", content = _cachedSystemPrompt },
                    new { role = "user", content = message }
                },
                temperature = 1,
                top_p = 0.9,
                max_tokens = 16384,
                stream = false
            };

            var jsonPayload = JsonConvert.SerializeObject(requestPayload);
            var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
            Console.WriteLine($"Sending request now.");

            if (!_httpClient.DefaultRequestHeaders.Contains("Authorization"))
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {NvidiaAPIKey}");

            var response = await _httpClient.PostAsync(NvidiaAPIEndpoint, content);
            Console.WriteLine($"Status code: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseContent);
                string? generatedResponse = jsonResponse?.choices?[0]?.message?.content?.ToString();
                Console.WriteLine($"Received response from NVIDIA API: {generatedResponse}");

                return generatedResponse;
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                return null;
            }
        }

        [HttpGet("thread/{id}/message")]
        public IActionResult GetThreadMessages(long id, [FromQuery] int messageCount = 50, [FromQuery] int mode = 0, [FromQuery] int referenceMessageId = 0)
        {
            var pid = AuthStuff.GetPlayerId(Request);
            if (pid == null)
                return Unauthorized("");

            var thread = ChatDB.GetThread(id);
            if (thread == null)
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 404
                };
            }

            if (!thread.PlayerIds.Contains((long)pid))
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 403
                };
            }

            IEnumerable<ChatDBClasses.ChatMessage> messages = thread.Messages;

            switch ((ChatDBClasses.QueryMode)mode)
            {
                case ChatDBClasses.QueryMode.Latest:
                    messages = messages
                        .OrderByDescending(m => m.ChatMessageId)
                        .Take(messageCount)
                        .OrderBy(m => m.ChatMessageId);
                    break;

                case ChatDBClasses.QueryMode.NewerThan:
                    messages = messages
                        .Where(m => m.ChatMessageId > referenceMessageId)
                        .OrderBy(m => m.ChatMessageId)
                        .Take(messageCount);
                    break;

                case ChatDBClasses.QueryMode.OlderThan:
                    messages = messages
                        .Where(m => m.ChatMessageId < referenceMessageId)
                        .OrderByDescending(m => m.ChatMessageId)
                        .Take(messageCount)
                        .OrderBy(m => m.ChatMessageId);
                    break;
            }

            /*var response = new[]
            {
                new
                {
                    chatThreadId = thread.ChatThreadId,
                    playerIds = thread.PlayerIds,
                    lastReadMessageId = thread.LastReadMessageId,
                    chatThreadName = thread.ChatThreadName,
                    snoozedUntil = thread.SnoozedUntil,
                    isFavorited = thread.IsFavorited,
                    messages = messages.Select(m => new
                    {
                        chatMessageId = m.ChatMessageId,
                        chatThreadId = m.ChatThreadId,
                        senderPlayerId = m.SenderPlayerId,
                        timeSent = m.TimeSent,
                        contents = m.Contents,
                        moderationState = m.ModerationState
                    }).ToList()
                }
            };*/

            var response = messages.Select(m => new
            {
                chatMessageId = m.ChatMessageId,
                chatThreadId = m.ChatThreadId,
                senderPlayerId = m.SenderPlayerId,
                timeSent = m.TimeSent,
                contents = m.Contents,
                moderationState = m.ModerationState
            }).ToList();

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(response),
                ContentType = "application/json",
                StatusCode = 200
            };
        }
        
        [HttpPost("thread/withmembers")]
        public IActionResult CreateThread([FromForm] List<long> ids)
        {
           var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
                
            var player = PlayerDB.Players.FindById(id.Value);
				if (player?.Player?.PlayerExtra == null)
						return NotFound("");

            string messageCountStr = Request.Form["messageCount"].ToString();
            int messageCount = int.TryParse(messageCountStr, out var mc) ? mc : 0;

            ids ??= new List<long>();
			ids.Add((long)id);
            var thread = ChatDB.CreateThread(ids, (long)id, null, out var createdNew);

            var response = new
            {
                chatResult = ChatDBClasses.ChatResults.Success,
                chatThread = new
                {
                    messages = thread.Messages.FirstOrDefault(),
                    chatThreadId = thread.ChatThreadId,
                    playerIds = thread.PlayerIds,
                    lastReadMessageId = thread.LastReadMessageId,
                    chatThreadName = thread.ChatThreadName,
                    chatThreadType = 0,
                    snoozedUntil = thread.SnoozedUntil,
                    isFavorited = thread.IsFavorited
                }
            };

            var starterMessage = thread.Messages.FirstOrDefault();
            if (createdNew && starterMessage != null)
            {
                var payload = new
                {
                    Id = "ChatMessageReceived",
                    Msg = new
                    {
                        chatMessageId = starterMessage.ChatMessageId,
                        chatThreadId = starterMessage.ChatThreadId,
                        senderPlayerId = starterMessage.SenderPlayerId,
                        timeSent = starterMessage.TimeSent,
                        contents = starterMessage.Contents,
                        moderationState = starterMessage.ModerationState
                    }
                };

                _ = NotificationsController.SendToPlayers(thread.PlayerIds, JsonConvert.SerializeObject(payload));
            }

            return new ContentResult()
            {
                Content = JsonConvert.SerializeObject(response),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("thread/{threadid}/rename")]
        public IActionResult RenameThread(long threadid)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
                
            string newName = Request.Form["name"].ToString();
            newName = Uri.UnescapeDataString(newName);
            var thread = ChatDB.GetThread(threadid);

            if (thread == null)
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 200
                };
            }

            if (!thread.PlayerIds.Contains((long)id))
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 403
                };
            }

            var updatedthread = ChatDB.RenameThread(threadid, (long)id, newName);

            return new ContentResult
            {
                Content = ChatResults.Success.ToString(),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("thread/{threadid}/leave")]
        public IActionResult LeaveThread(long threadid)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var thread = ChatDB.GetThread(threadid);

            if (thread == null)
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 200
                };
            }

           if (!thread.PlayerIds.Contains((long)id))
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 403
                };
            }

            var updatedthread = ChatDB.LeaveThread(threadid, (long)id);

            return Ok("0");
        }

        [HttpPut("thread/{threadid}/favorite")]
        public IActionResult SetFavorite(long threadid, [FromQuery] bool favorite)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var response = ChatDB.SetThreadFavorited(threadid, (long)id, favorite);
            if (response == null) return NotFound("");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(response),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("thread/{threadid}/message/{messageid}/read")]
        public IActionResult Readgay(long threadid, long messageid)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var thread = ChatDB.GetThread(threadid);

            if (thread == null)
                return Ok(2);

            if (!thread.PlayerIds.Contains((long)id))
                return StatusCode(403);

            bool updated = ChatDB.SetLastReadMessage(threadid, (long)id, messageid);
            if (!updated)
                return Ok(2);

            return Ok(0);
        }

     [HttpPost("thread/{threadid}/member/{memberid}")]
        public IActionResult AddMemberToThread(long threadid, long memberid)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var thread = ChatDB.GetThread(threadid);

            if (thread == null)
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 200
                };
            }

            var updatedthread = ChatDB.AddMemberToThread(threadid, (long)id, memberid);

            return Ok("0");
        }
    }
}