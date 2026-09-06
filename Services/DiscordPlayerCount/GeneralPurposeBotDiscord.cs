using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Controllers;
using static Vanadium.Classes.DBs.DBClasses.InventionDBClasses;
using static Vanadium.Classes.DBs.DBClasses.RoomDBClasses;

namespace Vanadium.Services
{
    public class PlayerCountService : BackgroundService
    {
        public class PendingLink
        {
            public ulong DiscordUserId { get; init; }
            public string DiscordUsername { get; init; } = "";
            public DateTime ExpiresAt { get; init; }
        }

        private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
        private static readonly ConcurrentDictionary<string, PendingLink> _codes = new();
        private static DiscordSocketClient? _discordClient;

        private static readonly HttpClient _http = new();
        private static ClientWebSocket? _ws;
        private static readonly string ConfigPath = Path.Combine(
            Environment.CurrentDirectory, "Services", "DiscordPlayerCount", "Config.json");
        private static readonly string ErrorLogPath = Path.Combine(
            Environment.CurrentDirectory, "Services", "DiscordPlayerCount", "errors.txt");

        private const string UploadAudioErrorWebhook =
            "https://discord.com/api/webhooks/1543130746254794853/3Y0nd5PydrGZAV_9kA2JjC2Z6n4DSAVLyPVjBadHVJlMjztxBRAl9Iu7AhM6u4BJ7BWm";

        private static async Task LogErrorAsync(string message)
        {
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] {message}";
            Console.WriteLine($"[PlayerCount] {line}");
            try
            {
                await File.AppendAllTextAsync(ErrorLogPath, line + Environment.NewLine);
            }
            catch { }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _ = MaintainGatewayConnectionAsync(stoppingToken);
            _ = InitDiscordClientAsync();

            while (!stoppingToken.IsCancellationRequested)
            {
                await UpdateMessageAndStatusAsync();
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }

        private async Task MaintainGatewayConnectionAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _ws = new ClientWebSocket();
                    await _ws.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), stoppingToken);

                    var buffer = new byte[4096];
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
                    string helloJson = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    using var doc = JsonDocument.Parse(helloJson);
                    int heartbeatInterval = doc.RootElement.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();

                    var identifyPayload = new
                    {
                        op = 2,
                        d = new
                        {
                            token = ServerConfig.CountBotToken,
                            intents = 0,
                            properties = new
                            {
                                os = "linux",
                                browser = "bro",
                                device = "bro"
                            }
                        }
                    };

                    byte[] identifyBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identifyPayload));
                    await _ws.SendAsync(new ArraySegment<byte>(identifyBytes), WebSocketMessageType.Text, true, stoppingToken);

                    _ = HeartbeatLoopAsync(heartbeatInterval, stoppingToken);

                    while (_ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                    {
                        var receiveBuffer = new byte[4096];
                        var res = await _ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), stoppingToken);
                        if (res.MessageType == WebSocketMessageType.Close)
                            break;
                    }
                }
                catch (Exception ex)
                {
                    await LogErrorAsync($"Gateway connection error: {ex}");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }

        private async Task HeartbeatLoopAsync(int intervalMs, CancellationToken stoppingToken)
        {
            while (_ws != null && _ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(intervalMs, stoppingToken);
                if (_ws.State != WebSocketState.Open) break;

                var heartbeat = new { op = 1, d = (object?)null };
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(heartbeat));
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, stoppingToken);
            }
        }

        private static async Task UpdateMessageAndStatusAsync()
        {
            try
            {
                var connectedPlayerIds = NotificationsController.PlayerConnections.Keys.ToHashSet();

                var inInstancePlayers = PlayerDB.Players.FindAll()
                    .Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true &&
                                connectedPlayerIds.Contains(p.PlayerId) &&
                                p.Player?.PlayerExtra?.Heartbeat?.roomInstance != null)
                    .ToList();

                int playerCount = inInstancePlayers.Count;

                if (_ws != null && _ws.State == WebSocketState.Open)
                {
                    string presencePlayerText = playerCount == 1 ? "player" : "players";
                    var presencePayload = new
                    {
                        op = 3,
                        d = new
                        {
                            since = (object?)null,
                            activities = new[]
                            {
                                new
                                {
                                    name = $"{playerCount} {presencePlayerText} online.",
                                    type = 3
                                }
                            },
                            status = "online",
                            afk = false
                        }
                    };

                    byte[] presenceBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(presencePayload));
                    await _ws.SendAsync(new ArraySegment<byte>(presenceBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }

                if (!File.Exists(ConfigPath))
                    return;

                using var configDoc = JsonDocument.Parse(await File.ReadAllTextAsync(ConfigPath));
                var root = configDoc.RootElement;

                if (!root.TryGetProperty("channelId", out var cid) || string.IsNullOrWhiteSpace(cid.GetString()))
                    return;
                if (!root.TryGetProperty("messageId", out var mid) || string.IsNullOrWhiteSpace(mid.GetString()))
                    return;

                string channelId = cid.GetString()!;
                string messageId = mid.GetString()!;

                var versionGroups = inInstancePlayers
                    .GroupBy(p => p.Player?.PlayerExtra?.Heartbeat?.appVersion ?? "Unknown")
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var embeds = new List<object>();

                foreach (var versionGroup in versionGroups)
                {
                    var activeInstancesLines = new List<string>();
                    var dormLines = new List<string>();
                    var privateRoomLines = new List<string>();
                    int versionPlayerCount = versionGroup.Count();
                    string versionPlayerText = versionPlayerCount == 1 ? "player" : "players";
                    string versionPrefix = versionPlayerCount == 1 ? "There's" : "There are";
                    string versionLabel = string.IsNullOrWhiteSpace(versionGroup.Key) || versionGroup.Key.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                        ? "Unknown Version"
                        : versionGroup.Key;

                    foreach (var p in versionGroup)
                    {
                        string displayName = p.Player?.DisplayName ?? p.Player?.Username ?? "Unknown";
                        var ri = p.Player?.PlayerExtra?.Heartbeat?.roomInstance!;
                        var deviceClass = p.Player?.PlayerExtra?.Heartbeat?.deviceClass == PlayerDBClasses.DeviceClasses.Quest2 ? " <:Oculus:1531704664209489980>" : "";
                        string strippedName = ri.Name?.TrimStart('^') ?? "";

                        bool isDorm = strippedName.StartsWith("DormRoom", StringComparison.OrdinalIgnoreCase) ||
                                     (ri.roomId > 0 && RoomDB.GetRoom(ri.roomId)?.Name?.StartsWith("DormRoom", StringComparison.OrdinalIgnoreCase) == true);

                        if (isDorm)
                        {
                            string ownerName = displayName;
                            if (!string.IsNullOrWhiteSpace(strippedName) && strippedName.Contains('#'))
                            {
                                string potentialOwner = strippedName.Split('#')[0];
                                if (!string.IsNullOrWhiteSpace(potentialOwner) && !potentialOwner.Equals("DormRoom", StringComparison.OrdinalIgnoreCase))
                                {
                                    ownerName = potentialOwner;
                                }
                            }
                            dormLines.Add($"**{displayName}{deviceClass}** - @{ownerName}'s Dorm");
                            continue;
                        }

                        var room = ri.roomId > 0 ? RoomDB.GetRoom(ri.roomId) : (!string.IsNullOrWhiteSpace(strippedName) ? RoomDB.GetRoomByName(strippedName) : null);

                        if (room?.Accessibility == RoomAccessibility.Private)
                        {
                            privateRoomLines.Add($"**{displayName}{deviceClass}** - [PRIVATE ROOM]");
                            continue;
                        }

                        string baseName = room?.Name ?? (string.IsNullOrWhiteSpace(strippedName) ? "Unknown Room" : strippedName);
                        string formattedRoom = ri.isPrivate ? $"^{baseName} [PRIVATE]" : $"^{baseName}";
                        activeInstancesLines.Add($"**{displayName}{deviceClass}** - {formattedRoom}");
                    }

                    var sb = new StringBuilder();

                    if (activeInstancesLines.Count > 0)
                    {
                        sb.AppendLine("* *Active Instances* \n");
                        foreach (var line in activeInstancesLines)
                            sb.AppendLine(line);
                        sb.AppendLine();
                    }

                    if (dormLines.Count > 0)
                    {
                        sb.AppendLine("* *Dorms* \n");
                        foreach (var line in dormLines)
                            sb.AppendLine(line);
                        sb.AppendLine();
                    }

                    if (privateRoomLines.Count > 0)
                    {
                        sb.AppendLine("* *Private Rooms* \n");
                        foreach (var line in privateRoomLines)
                            sb.AppendLine(line);
                        sb.AppendLine();
                    }

                    string description;
                    if (versionPlayerCount == 0)
                    {
                        description = "*No players online.*";
                    }
                    else
                    {
                        description = sb.ToString();
                    }

                    var embed = new
                    {
                        color = 5757183,
                        title = $"{versionPrefix} {versionPlayerCount} {versionPlayerText} online in {versionLabel}.",
                        description
                    };

                    embeds.Add(embed);
                }
                DateTime startTime = Process.GetCurrentProcess().StartTime;
                long startTimeUnix = (long)(startTime.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds;
                var beginningEmbed = new
                    {
                        author = new
                        {
                            name = "Vanadium Player Count",
                            icon_url = "https://reloxa.xyz/imageserver/pfp_1806_1785439345432.png"
                        },
                        description = "*Updates every 15 seconds.\n-# Version and platform may not be accurate!*\n* Server Startup Time: <t:" + (long)(startTimeUnix) + ":t>\n* Server Uptime: <t:" + (long)(startTimeUnix) + ":R>"
                    };
                embeds.Insert(0, beginningEmbed);

                var payload = JsonSerializer.Serialize(new { content = (string?)null, embeds = embeds.ToArray() });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(
                    HttpMethod.Patch,
                    $"https://discord.com/api/v10/channels/{channelId}/messages/{messageId}")
                {
                    Content = content
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bot", ServerConfig.CountBotToken);

                var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    await LogErrorAsync($"Discord PATCH failed — HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
                }
            }
            catch (Exception ex)
            {
                await LogErrorAsync($"UpdateMessageAndStatusAsync exception: {ex}");
            }
        }

        private async Task InitDiscordClientAsync()
        {
            if (string.IsNullOrWhiteSpace(ServerConfig.CountBotToken)) return;

            _discordClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds,
                LogLevel = LogSeverity.Warning
            });

            _discordClient.Ready += OnDiscordReady;
            _discordClient.SlashCommandExecuted += HandleSlashCommand;

            await _discordClient.LoginAsync(TokenType.Bot, ServerConfig.CountBotToken);
            await _discordClient.StartAsync();
        }

        private async Task OnDiscordReady()
        {
            var link = new SlashCommandBuilder()
                .WithName("link")
                .WithDescription("Link your discord account to vanadium to play 2025 build (limited to booster tho)")
                .AddOption("username", ApplicationCommandOptionType.String, "Your Vanadium username", isRequired: true)
                .Build();
            await _discordClient!.CreateGlobalApplicationCommandAsync(link);

            var whoami = new SlashCommandBuilder()
                .WithName("whoami")
                .WithDescription("tells you who you are in vanadium")
                .Build();
            await _discordClient!.CreateGlobalApplicationCommandAsync(whoami);

            var changePassword = new SlashCommandBuilder()
                .WithName("change-password")
                .WithDescription("to change your vanadium password if linked")
                .AddOption("password", ApplicationCommandOptionType.String, "Your new password", isRequired: true)
                .Build();
            await _discordClient!.CreateGlobalApplicationCommandAsync(changePassword);

            var unlink = new SlashCommandBuilder()
                .WithName("unlink")
                .WithDescription("Unlink your discord account from vanadium")
                .Build();
            await _discordClient!.CreateGlobalApplicationCommandAsync(unlink);

            var uploadAudio = new SlashCommandBuilder()
                .WithName("upload-audio")
                .WithDescription("Import audio as an invention (linked account required)")
                .AddOption("name", ApplicationCommandOptionType.String, "Invention name", isRequired: true)
                .AddOption("audio_file", ApplicationCommandOptionType.Attachment, "audio to import (Supported files: mp3/4, ogg, htr, mov) - max 10 min / 10 MB", isRequired: true)
                .Build();
            await _discordClient!.CreateGlobalApplicationCommandAsync(uploadAudio);
        }

        /* start link stuff */

        private async Task HandleSlashCommand(SocketSlashCommand cmd)
        {
            switch (cmd.CommandName)
            {
                case "link":
                    await HandleLinkCommand(cmd);
                    break;
                case "whoami":
                    await HandleWhoamiCommand(cmd);
                    break;
                case "change-password":
                    await HandleChangePasswordCommand(cmd);
                    break;
                case "unlink":
                    await HandleUnlinkCommand(cmd);
                    break;
                case "upload-audio":
                    await HandleUploadAudioCommand(cmd);
                    break;
            }
        }

        private async Task HandleLinkCommand(SocketSlashCommand cmd)
        {
            string discordId = cmd.User.Id.ToString();
            var existingLinked = PlayerDB.Players.FindOne(p => p.Player != null && p.Player.DiscordLinked && p.Player.DiscordUserId == discordId);
            if (existingLinked != null)
            {
                await cmd.RespondAsync($"**Your Discord is already linked to @{existingLinked.Player.Username}. Use /unlink first.**", ephemeral: true);
                return;
            }

            var username = (string)cmd.Data.Options.First().Value;
            var player = PlayerDB.Players.FindOne(p => p.Player != null && p.Player.Username.ToLower() == username.ToLower());
            if (player == null)
            {
                await cmd.RespondAsync("No vanadium accounts with that username were found", ephemeral: true);
                return;
            }

            if (player.Player.DiscordLinked)
            {
                await cmd.RespondAsync("**This user already has an account linked.**", ephemeral: true);
                return;
            }

            string linkCode = GenerateCode(cmd.User.Id);
            string coachPin = new Random().Next(100000, 999999).ToString();

            Program.PendingInGameCodes[coachPin] = (player.PlayerId, cmd.User.Id);

            var messageData = new Vanadium.Classes.DBs.DBClasses.PlayerDBClasses.MessageData
            {
                Id = new Random().Next(1, 0x7ffffff),
                FromPlayerId = 1,
                SentTime = DateTime.UtcNow,
                Type = (Vanadium.Classes.DBs.DBClasses.PlayerDBClasses.MessageType)100,
                Data = $"Your pin code is \"{coachPin}\", don't share this or else you'd be giving someone else access to the 2025 build.",
                RoomId = 1
            };

            if (player.Player.PlayerExtra.Messages == null)
                player.Player.PlayerExtra.Messages = new List<Vanadium.Classes.DBs.DBClasses.PlayerDBClasses.MessageData>();

            player.Player.PlayerExtra.Messages.Add(messageData);
            PlayerDB.Players.Update(player);

            var msg = new
            {
                Id = "2",
                Msg = messageData
            };
            _ = NotificationsController.SendToPlayer(player.PlayerId, Newtonsoft.Json.JsonConvert.SerializeObject(msg));

            await cmd.RespondAsync($"Message was sent to the username \"**{player.Player.Username}**\" \n Verify At: https://reloxa.xyz/beta/verifydiscord", ephemeral: true);
        }

        private async Task HandleWhoamiCommand(SocketSlashCommand cmd)
        {
            string discordId = cmd.User.Id.ToString();
            var player = PlayerDB.Players.FindOne(p => p.Player != null && p.Player.DiscordLinked && p.Player.DiscordUserId == discordId);

            if (player == null)
            {
                await cmd.RespondAsync("use **/link** to link your account. It isn't linked currently.", ephemeral: true);
                return;
            }

            await cmd.RespondAsync($"**@{player.Player.Username}**, {player.Player.DisplayName}", ephemeral: true);
        }

        private async Task HandleChangePasswordCommand(SocketSlashCommand cmd)
        {
            string discordId = cmd.User.Id.ToString();
            var player = PlayerDB.Players.FindOne(p => p.Player != null && p.Player.DiscordLinked && p.Player.DiscordUserId == discordId);

            if (player == null)
            {
                await cmd.RespondAsync("use **/link** to link your account. It isn't linked currently.", ephemeral: true);
                return;
            }

            string newPassword = (string)cmd.Data.Options.First().Value;
            PlayerDB.PasswordManager.ChangePassword(player.PlayerId, newPassword);

            await cmd.RespondAsync("Password was updated successfully.", ephemeral: true);
        }

        private async Task HandleUnlinkCommand(SocketSlashCommand cmd)
        {
            string discordId = cmd.User.Id.ToString();
            var player = PlayerDB.Players.FindOne(p => p.Player != null && p.Player.DiscordLinked && p.Player.DiscordUserId == discordId);

            if (player == null)
            {
                await cmd.RespondAsync("Bro, you aint even linked an account in the first place", ephemeral: true);
                return;
            }

            string username = player.Player.Username ?? "";
            player.Player.DiscordLinked = false;
            player.Player.DiscordUserId = null;
            player.Player.DiscordUsername = null;
            player.Player.DiscordLinkedAt = null;
            PlayerDB.Players.Update(player);

            await cmd.RespondAsync($"Unlinked discord from **@{username}**", ephemeral: true);
        }

        private async Task HandleUploadAudioCommand(SocketSlashCommand cmd)
        {
            string discordId = cmd.User.Id.ToString();
            var player = PlayerDB.Players.FindOne(p => p.Player != null && p.Player.DiscordLinked && p.Player.DiscordUserId == discordId);

            if (player == null)
            {
                await cmd.RespondAsync("Link your account with **/link** to use this command.", ephemeral: true);
                return;
            }

            await cmd.DeferAsync(ephemeral: true);

            string inventionName = (string)cmd.Data.Options.First(o => o.Name == "name").Value;
            var attachment = (IAttachment)cmd.Data.Options.First(o => o.Name == "audio_file").Value;

            try
            {
                string ext = Path.GetExtension(attachment.Filename).ToLowerInvariant();
                string[] allowed = { ".mp3", ".mp4", ".mov", ".htr", ".wav", ".ogg" };
                if (!allowed.Contains(ext))
                {
                    await cmd.ModifyOriginalResponseAsync(m => m.Content = "Unsupported file type.");
                    await SendErrorWebhookAsync($"upload-audio: unsupported file type `{ext}` from discord user {discordId}");
                    return;
                }

                if (attachment.Size > 10 * 1024 * 1024)
                {
                    await cmd.ModifyOriginalResponseAsync(m => m.Content = "File must be under 10 MB.");
                    return;
                }

                byte[] fileBytes;
                using (var dlClient = new HttpClient())
                    fileBytes = await dlClient.GetByteArrayAsync(attachment.Url);

                string htrStoredName;
                string invStoredName;

                if (ext == ".htr")
                {
                    htrStoredName = Guid.NewGuid().ToString("N") + ".htr";
                    string htrDir = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "htr"));
                    Directory.CreateDirectory(htrDir);
                    await File.WriteAllBytesAsync(Path.Combine(htrDir, htrStoredName), fileBytes);
                }
                else
                {
                    using var makeHtrForm = new MultipartFormDataContent();
                    makeHtrForm.Add(new ByteArrayContent(fileBytes), "audio", attachment.Filename);

                    using var apiClient = new HttpClient();
                    var htrResponse = await apiClient.PostAsync("https://vanadium.reloxa.xyz/audio/makeHtr", makeHtrForm);
                    if (!htrResponse.IsSuccessStatusCode)
                    {
                        string body = await htrResponse.Content.ReadAsStringAsync();
                        throw new Exception($"makeHtr failed {(int)htrResponse.StatusCode}: {body}");
                    }

                    byte[] htrBytes = await htrResponse.Content.ReadAsByteArrayAsync();
                    htrStoredName = Guid.NewGuid().ToString("N") + ".htr";
                    string htrDir = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "htr"));
                    Directory.CreateDirectory(htrDir);
                    await File.WriteAllBytesAsync(Path.Combine(htrDir, htrStoredName), htrBytes);
                }

                using var makeInvForm = new MultipartFormDataContent();
                makeInvForm.Add(new StringContent(htrStoredName), "name");

                using var invApiClient = new HttpClient();
                var invResponse = await invApiClient.PostAsync("https://vanadium.reloxa.xyz/audio/makeInv", makeInvForm);
                if (!invResponse.IsSuccessStatusCode)
                {
                    string body = await invResponse.Content.ReadAsStringAsync();
                    throw new Exception($"makeInv failed {(int)invResponse.StatusCode}: {body}");
                }

                byte[] invBytes = await invResponse.Content.ReadAsByteArrayAsync();
                invStoredName = Guid.NewGuid().ToString("N") + ".inv";
                string invDir = Path.GetFullPath(Path.Join(Program.dataDir, "cdn", "inv"));
                Directory.CreateDirectory(invDir);
                await File.WriteAllBytesAsync(Path.Combine(invDir, invStoredName), invBytes);

                var saveRequest = new SaveInventionRequest
                {
                    name = inventionName,
                    description = "Imported Audio through Discord.",
                    imageName = "DefaultRoomImage.jpg",
                    instantiationCost = 0,
                    lightsCost = 0,
                    chipsCost = 0,
                    cloudVariablesCost = 0,
                    aiCost = 0,
                    inkCost = 1,
                    creationRoomId = 1,
                    inventionDataFilename = invStoredName,
                    referencedInventions = new List<long>()
                };

                var result = InventionDB.SaveInvention(player.PlayerId, saveRequest);
                if (result == null)
                    throw new Exception("InventionDB.SaveInvention returned null");

                var invention = result.Value.invention;
                invention.LongDescription = "Imported Audio through Discord.";
                invention.ForceCannotPublish = true;
                invention.IsRecRoomApproved = true;
                invention.AllowTrial = false;
                invention.Tags = new List<InventionTag> { new InventionTag { Tag = "imported", Type = 1 } };
                InventionDB.Inventions.Update(invention);

                await cmd.ModifyOriginalResponseAsync(m =>
                    m.Content = $"Invention \"**{inventionName}**\" imported successfully.");
            }
            catch (Exception ex)
            {
                await cmd.ModifyOriginalResponseAsync(m =>
                    m.Content = "Servers decided to say no, this has been logged and will likely be fixed within a few hours.");
                await SendErrorWebhookAsync($"upload-audio exception from discord user {discordId}: {ex}");
            }
        }

        private static async Task SendErrorWebhookAsync(string message)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    embeds = new[]
                    {
                        new
                        {
                            title = "upload-audio error",
                            description = message.Length > 4000 ? message[..4000] : message,
                            color = 15158332
                        }
                    }
                });
                await _http.PostAsync(UploadAudioErrorWebhook,
                    new StringContent(payload, Encoding.UTF8, "application/json"));
            }
            catch { }
        }

        private static string GenerateCode(ulong discordUserId)
        {
            PruneExpired();
            _codes.Where(kv => kv.Value.DiscordUserId == discordUserId)
                  .Select(kv => kv.Key).ToList()
                  .ForEach(k => _codes.TryRemove(k, out _));

            string code;
            do
            {
                code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
            } while (_codes.ContainsKey(code));
            return code;
        }

        public static PendingLink? ConsumeCode(string? code)
        {
            PruneExpired();
            if (string.IsNullOrWhiteSpace(code) || !_codes.TryRemove(code.Trim(), out var pending))
                return null;
            if (DateTime.UtcNow > pending.ExpiresAt)
                return null;
            return pending;
        }

        private static void PruneExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _codes)
                if (now > kv.Value.ExpiresAt)
                    _codes.TryRemove(kv.Key, out _);
        }

        /* end link stuff */

    }
}