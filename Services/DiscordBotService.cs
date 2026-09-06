using Discord;
using Discord.Net;
using Discord.WebSocket;
using SixLabors.ImageSharp.PixelFormats;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Utils.NotiController;
using Vanadium.Classes.WebSocket;
using Vanadium.Controllers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using Vanadium.Hubs;

namespace Vanadium.Services
{
	public class DiscordBotService : IHostedService
	{
        public static List<ulong> WhitelistedIds = new List<ulong>
		{
			1034585922186002432,
            1505639244142608594,
			1228278497194147860,
			693274900731002931,
            1252146617772015616
		};

		private const ulong BoosterRoleId = 1511181684438204457UL;
		private const ulong SkyfireUserId = 1034585922186002432UL;
		public const ulong GuildId = 1350986037526007919UL;
		private const int BoosterDailyImportLimit = 3;

		private static readonly SemaphoreSlim _importSemaphore = new SemaphoreSlim(1, 1);
		private static int _importQueueLength = 0;

		private static readonly string _tokenStorePath = Path.Combine(Environment.CurrentDirectory, "Data", "UserTokens.json");
		private static Dictionary<ulong, SavedTokenEntry> _savedTokens = new();
		private static readonly Dictionary<ulong, DailyImportRecord> _dailyImports = new();
		private static readonly object _dailyLock = new();
		private static readonly object _tokenPersistLock = new();

		private static readonly Dictionary<ulong, StickyMessage> _stickyMessages = new();
		private static readonly object _stickyLock = new();

		private readonly DiscordSocketClient _client;
		private readonly ILogger<DiscordBotService> _logger;
		private readonly IHttpClientFactory _httpFactory;

		/*private static NotificationService? _notificationService;

		public static void Init(NotificationService service)
		{
			_notificationService = service;
		}
        */

		public DiscordBotService(ILogger<DiscordBotService> logger, IHttpClientFactory httpFactory)
		{
			_logger = logger;
			_httpFactory = httpFactory;

			var config = new DiscordSocketConfig
			{
				GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
				LogLevel = LogSeverity.Info
			};

			_client = new DiscordSocketClient(config);
			_client.Log += msg =>
			{
				_logger.LogInformation("[Discord] {msg}", msg.ToString());
				return Task.CompletedTask;
			};
			_client.Ready += OnReady;
			_client.SlashCommandExecuted += HandleSlashCommand;
			_client.AutocompleteExecuted += HandleAutocomplete;
			_client.MessageReceived += HandleMessageReceived;
		}

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			LoadSavedTokens();
			string token = ServerConfig.BotToken;
			if (string.IsNullOrWhiteSpace(token))
			{
				_logger.LogWarning("[Discord] BotToken is empty and that means the bot will not start.");
				return;
			}

			await _client.LoginAsync(TokenType.Bot, token);
			await _client.StartAsync();
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			await _client.StopAsync();
		}

		private async Task OnReady()
		{
			_logger.LogInformation("[Discord] Bot ready as {user}", _client.CurrentUser);

			await _client.SetGameAsync(ServerConfig.BotStatus, type: ActivityType.Streaming);

			var globalCommands = new List<ApplicationCommandProperties>
			{
            	 new SlashCommandBuilder()
					.WithName("instance-findall")
					.WithDescription("Find all players in a specific room instance.")
					.AddOption("instanceid", ApplicationCommandOptionType.Integer, "Room instance ID", isRequired: true)
					.Build(),
                
				new SlashCommandBuilder()
					.WithName("player-shove")
					.WithDescription("Shove a player into a specific room.")
					.AddOption("username", ApplicationCommandOptionType.String, "IGN of the target player", isRequired: true)
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "The target Room ID to force them into", isRequired: true)
					.AddOption("subroomid", ApplicationCommandOptionType.Integer, "Optional SubRoom ID", isRequired: false)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-ban")
					.WithDescription("Ban a player from Vanadium")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID to ban", isRequired: true)
					.AddOption("reason", ApplicationCommandOptionType.String, "Why are you banning this player?", isRequired: true)
					.AddOption("duration", ApplicationCommandOptionType.Integer, "How long should they be banned?", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-unban")
					.WithDescription("Unban a player from Vanadium")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID to unban", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-info")
					.WithDescription("Look up a player by ID or username.")
					.AddOption("search", ApplicationCommandOptionType.String, "Player ID or username", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-delete")
					.WithDescription("Permanently delete a player account.")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.Build(),
                new SlashCommandBuilder()
					.WithName("rooms-delete-all-from-player")
					.WithDescription("Delete all room from player id.")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-rename")
					.WithDescription("Change a player's username and display name.")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.AddOption("username", ApplicationCommandOptionType.String, "New username", isRequired: true)
					.Build(),
                
				new SlashCommandBuilder()
					.WithName("player-roles")
					.WithDescription("Add or remove a specific role from a player.")
					.AddOption("username", ApplicationCommandOptionType.String, "In-game username", isRequired: true)
					.AddOption(new SlashCommandOptionBuilder()
						.WithName("role")
						.WithDescription("Role to add or remove")
						.WithRequired(true)
						.WithType(ApplicationCommandOptionType.String)
						.AddChoice("Developer", "Developer")
						.AddChoice("Moderator", "Moderator")
						.AddChoice("Screenshare", "Screenshare")
						.AddChoice("Keepsake", "Keepsake")
						.AddChoice("gameClient", "gameClient"))
					.AddOption(new SlashCommandOptionBuilder()
						.WithName("type")
						.WithDescription("Add or remove role?")
						.WithRequired(true)
						.WithType(ApplicationCommandOptionType.String)
						.AddChoice("Add", "add")
						.AddChoice("Remove", "remove"))
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-set-level")
					.WithDescription("Set a player's level and XP.")
					.AddOption("username", ApplicationCommandOptionType.String, "In-game username", isRequired: true)
					.AddOption("level", ApplicationCommandOptionType.Integer, "Level to set", isRequired: true)
					.AddOption("xp", ApplicationCommandOptionType.Integer, "XP override (defaults to level's required XP)", isRequired: false)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-give-skin")
					.WithDescription("Grant or remove a skin from a player.")
					.AddOption("username", ApplicationCommandOptionType.String, "In-game player username", isRequired: true)
					.AddOption(new SlashCommandOptionBuilder()
						.WithName("skin")
						.WithDescription("Skin to grant or remove")
						.WithRequired(true)
						.WithType(ApplicationCommandOptionType.String)
						.WithAutocomplete(true))
					.AddOption(new SlashCommandOptionBuilder()
						.WithName("type")
						.WithDescription("Add or remove skin?")
						.WithRequired(true)
						.WithType(ApplicationCommandOptionType.String)
						.AddChoice("Add", "add")
						.AddChoice("Remove", "remove"))
					.Build(),
                
				new SlashCommandBuilder()
					.WithName("player-heartbeat")
					.WithDescription("View a player's last heartbeat / online status.")
					.AddOption("search", ApplicationCommandOptionType.String, "Player ID or username", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-heartbeat-clear")
					.WithDescription("Clear / reset a player's heartbeat.")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.Build(),
                 
				new SlashCommandBuilder()
					.WithName("player-reset-pfp")
					.WithDescription("Reset a player's profile picture to default.")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.Build(),
                new SlashCommandBuilder()
					.WithName("player-upload-pfp")
					.WithDescription("Upload an image and set it as a player's profile picture.")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.AddOption("file", ApplicationCommandOptionType.Attachment, "Image file to use as PFP", isRequired: true)
					.Build(),
                new SlashCommandBuilder()
                    .WithName("player-delete-all-images")
                    .WithDescription("Delete all images owned by a player.")
                    .AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
                    .Build(),
				new SlashCommandBuilder()
					.WithName("player-repair")
					.WithDescription("Reset values of an account with \"Null Values\"")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.Build(),
                
				new SlashCommandBuilder()
					.WithName("player-influencer")
					.WithDescription("Grant or remove influencer status from a player.")
					.AddOption("username", ApplicationCommandOptionType.String, "In-game username", isRequired: true)
					.AddOption(new SlashCommandOptionBuilder()
						.WithName("type")
						.WithDescription("Add or remove influencer status?")
						.WithRequired(true)
						.WithType(ApplicationCommandOptionType.String)
						.AddChoice("Add", "add")
						.AddChoice("Remove", "remove"))
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-set-platformid")
					.WithDescription("Override a player's first platform ID.")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.AddOption("platformid", ApplicationCommandOptionType.String, "New platform ID (ulong)", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-transfer-rooms")
					.WithDescription("Transfer all rooms from one player to another.")
					.AddOption("oldplayerid", ApplicationCommandOptionType.Integer, "Old owner player ID", isRequired: true)
					.AddOption("newplayerid", ApplicationCommandOptionType.Integer, "New owner player ID", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("account-create")
					.WithDescription("Create a new game account linked to a Discord user.")
					.AddOption("user", ApplicationCommandOptionType.User, "Discord user to link", isRequired: true)
					.AddOption("username", ApplicationCommandOptionType.String, "In-game username", isRequired: true)
					.AddOption("platformid", ApplicationCommandOptionType.String, "Steam ID (ulong)", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-set-tokens")
					.WithDescription("Set a player's RecCenterTokens balance.")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.AddOption("amount", ApplicationCommandOptionType.Integer, "New token balance", isRequired: true)
					.AddOption("balancetype", ApplicationCommandOptionType.Integer, "Balance type (default 0 = NonPurchasedDefault)", isRequired: false)
					.Build(),

				new SlashCommandBuilder()
					.WithName("room-info")
					.WithDescription("Look up a room by ID.")
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "Room ID", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("room-delete")
					.WithDescription("Delete a room permanently.")
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "Room ID", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("room-reset-image")
					.WithDescription("Reset a room's thumbnail to default.")
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "Room ID", isRequired: true)
					.Build(),
                 
				new SlashCommandBuilder()
					.WithName("room-change-owner")
					.WithDescription("Transfer ownership of a room to another player.")
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "Room ID", isRequired: true)
					.AddOption("newownerid", ApplicationCommandOptionType.Integer, "New owner player ID", isRequired: true)
					.Build(),
                 
				new SlashCommandBuilder()
					.WithName("room-set-role")
					.WithDescription("Assign a role to a player inside a room.")
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "Room ID", isRequired: true)
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.AddOption("role", ApplicationCommandOptionType.Integer, "Role integer value", isRequired: true)
					.Build(),
                    
				new SlashCommandBuilder()
					.WithName("room-toggle-beta")
					.WithDescription("Toggle the beta tag on a room.")
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "Room ID", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("room-change-datablob")
					.WithDescription("Import a datablob into a subroom's current save.")
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "Room ID", isRequired: true)
					.AddOption("subroomid", ApplicationCommandOptionType.Integer, "SubRoom ID", isRequired: true)
					.AddOption("datablob", ApplicationCommandOptionType.String, "Datablob filename", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("room-clear")
					.WithDescription("Clear all non-dorm rooms and re-import from ImportRooms.json.")
					.Build(),
				new SlashCommandBuilder()
					.WithName("room-import")
					.WithDescription("Import rooms from rec.net AUTHORIZATION REQUIRED")
					.AddOption("roomname", ApplicationCommandOptionType.String, "Exact RecNet room name (e.g. TheBackDoor)", isRequired: true)
					.AddOption("authentication", ApplicationCommandOptionType.String, "Rec.Net session JSON (optional if you have a saved token)", isRequired: false)
					.Build(),
				new SlashCommandBuilder()
					.WithName("import-valid")
					.WithDescription("Check if your saved Rec.Net session token is valid.")
					.Build(),
				new SlashCommandBuilder()
					.WithName("room-set-image")
					.WithDescription("Set a room's image by filename (e.g. abc123.jpg). Must already exist in Data/Images/.")
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "Room ID", isRequired: true)
					.AddOption("imagename", ApplicationCommandOptionType.String, "Image filename (e.g. abc123.jpg)", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("db-reset")
					.WithDescription("WIPE the entire database and re-import base rooms.")
					.Build(),
				new SlashCommandBuilder()
					.WithName("db-info")
					.WithDescription("View counts of all database collections.")
					.Build(),
				new SlashCommandBuilder()
					.WithName("upload-cdn")
					.WithDescription("Upload a file to the internal CDN.")
					.AddOption("file", ApplicationCommandOptionType.Attachment, "File to upload", isRequired: true)
					.Build(),
          
				new SlashCommandBuilder()
					.WithName("upload-image")
					.WithDescription("Upload an image to the server (saved under account ID 1).")
					.AddOption("file", ApplicationCommandOptionType.Attachment, "Image file", isRequired: true)
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "Room ID to associate with", isRequired: false)
					.AddOption("description", ApplicationCommandOptionType.String, "Optional description", isRequired: false)
					.Build(),
				new SlashCommandBuilder()
					.WithName("whitelist-add")
					.WithDescription("Whitelist a Steam platform ID.")
					.AddOption("steamid", ApplicationCommandOptionType.String, "Steam ID (ulong)", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("whitelist-remove")
					.WithDescription("Remove a Steam platform ID from the whitelist.")
					.AddOption("steamid", ApplicationCommandOptionType.String, "Steam ID (ulong)", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("whitelist-list")
					.WithDescription("Show all currently whitelisted Steam IDs.")
					.Build(),
                    
				new SlashCommandBuilder()
					.WithName("room-find-by-name")
					.WithDescription("Find a room ID by its name (exact or partial match).")
					.AddOption("name", ApplicationCommandOptionType.String, "Room name to search", isRequired: true)
					.Build(),
               new SlashCommandBuilder()
					.WithName("get-image-name-to-playerid")
					.WithDescription("Get a image name to player id.")
					.AddOption("image-name", ApplicationCommandOptionType.String, "Image Name", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("room-find-by-id")
					.WithDescription("Find a room's name and basic info by its ID.")
					.AddOption("roomid", ApplicationCommandOptionType.Integer, "Room ID", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-find-by-username")
					.WithDescription("Find a player ID by username.")
					.AddOption("username", ApplicationCommandOptionType.String, "Username to search", isRequired: true)
					.Build(),
				new SlashCommandBuilder()
					.WithName("player-find-by-id")
					.WithDescription("Find a player's username by their ID.")
					.AddOption("playerid", ApplicationCommandOptionType.Integer, "Player ID", isRequired: true)
					.Build(),
                    
				new SlashCommandBuilder()
					.WithName("db-execute")
					.WithDescription("Execute a raw database expression against RoomDB or PlayerDB.")
					.AddOption("expression", ApplicationCommandOptionType.String, "e.g. RoomDB.Rooms.Count() or PlayerDB.Players.FindAll().Count()", isRequired: true)
                    
					.Build(),
				new SlashCommandBuilder()
					.WithName("server-scheduled-maintenance")
					.WithDescription("Start fake server scheduled maintenance.")
					.AddOption("minutes", ApplicationCommandOptionType.Integer, "Minutes", isRequired: true)
					.Build(),
			};

			try
			{
				await _client.BulkOverwriteGlobalApplicationCommandsAsync(globalCommands.ToArray());
				_logger.LogInformation("[Discord] Registered {count} global slash commands.", globalCommands.Count);
			}
			catch (HttpException ex)
			{
				_logger.LogError("[Discord] Failed to register global commands: {err}", ex.Message);
			}

var guildCommands = new List<ApplicationCommandProperties>

			{
				new SlashCommandBuilder()
					.WithName("status")
					.WithDescription("See if vanadium is up")
					.Build(),
				new SlashCommandBuilder()
					.WithName("ping")
					.WithDescription("See if vanadium is up")
					.Build(),
				new SlashCommandBuilder()
					.WithName("stick")
					.WithDescription("\u200b")
					.AddOption("message", ApplicationCommandOptionType.String, "\u200b", isRequired: true)
					.AddOption("channel", ApplicationCommandOptionType.Channel, "\u200b", isRequired: false)
					.Build(),
				new SlashCommandBuilder()
					.WithName("stick-stop")
					.WithDescription("\u200b")
					.AddOption("channel", ApplicationCommandOptionType.Channel, "\u200b", isRequired: false)
					.Build(),
			};

			try
			{
				var guild = _client.GetGuild(GuildId);
				if (guild != null)
				{
					await guild.BulkOverwriteApplicationCommandAsync(guildCommands.ToArray());
					_logger.LogInformation("[Discord] Registered {count} guild slash commands.", guildCommands.Count);
				}
				else
				{
					_logger.LogWarning("[Discord] Guild {id} not found — guild commands not registered.", GuildId);
				}
			}
			catch (HttpException ex)
			{
				_logger.LogError("[Discord] Failed to register guild commands: {err}", ex.Message);
			}
		}

		private async Task HandleAutocomplete(SocketAutocompleteInteraction interaction)
		{
			if (interaction.Data.CommandName != "player-give-skin") return;
			if (interaction.Data.Current.Name != "skin") return;

			string typed = interaction.Data.Current.Value?.ToString() ?? "";
			string path = Path.Join(Program.dataDir, "APIS", "Items", "Equipment.json");

			if (!File.Exists(path))
			{
				await interaction.RespondAsync(Array.Empty<AutocompleteResult>());
				return;
			}

			var items = System.Text.Json.JsonSerializer.Deserialize<List<OwnedEquipmentItem>>(
				await File.ReadAllTextAsync(path),
				new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

			var results = items?
				.Where(i => i.FriendlyName.Contains(typed, StringComparison.OrdinalIgnoreCase))
				.Take(25)
				.Select(i => new AutocompleteResult(i.FriendlyName, i.FriendlyName))
				.ToArray() ?? Array.Empty<AutocompleteResult>();

			await interaction.RespondAsync(results);
		}

		private async Task<bool> IsBoosterAsync(ulong userId)
		{
			try
			{
				var guild = _client.GetGuild(GuildId);
				var member = guild?.GetUser(userId) ?? (IGuildUser)await _client.Rest.GetGuildUserAsync(GuildId, userId);
				return member?.RoleIds?.Contains(BoosterRoleId) ?? false;
			}
			catch
			{
				return false;
			}
		}

		private async Task<bool> CanUseImportCommandAsync(SocketSlashCommand cmd)
		{
			return WhitelistedIds.Contains(cmd.User.Id) || await IsBoosterAsync(cmd.User.Id);
		}

		private async Task HandleMessageReceived(SocketMessage msg)
		{
			if (msg.Author.IsBot) return;
			if (msg.Channel is not ISocketMessageChannel channel) return;

			StickyMessage? sticky;
			lock (_stickyLock)
			{
				if (!_stickyMessages.TryGetValue(channel.Id, out sticky)) return;
			}

			try
			{
				await TryDeleteStickyMessage(channel, sticky.MessageId);
			}
			catch { }

			try
			{
				var embed = BuildStickyEmbed(sticky.Content, sticky.StickedAt);
				var newMessage = await channel.SendMessageAsync(embed: embed);

				lock (_stickyLock)
				{
					if (_stickyMessages.TryGetValue(channel.Id, out var current) && current.MessageId == sticky.MessageId)
					{
						current.MessageId = newMessage.Id;
					}
				}
			}
			catch { }
		}

		private async Task HandleSlashCommand(SocketSlashCommand cmd)
		{
			if (cmd.CommandName is "status" or "ping")
			{
				await CmdStatus(cmd, cmd.CommandName == "ping");
				return;
			}

			if (cmd.CommandName is "stick" or "stick-stop")
			{
				if (!WhitelistedIds.Contains(cmd.User.Id))
				{
					await cmd.RespondAsync("nice try loser", ephemeral: true);
					return;
				}

				if (cmd.CommandName == "stick")
					await CmdStick(cmd);
				else
					await CmdStickStop(cmd);

				return;
			}

			bool _isWhitelistedTop = WhitelistedIds.Contains(cmd.User.Id);
			bool _isImportCmd = cmd.CommandName is "room-import" or "import-valid";
			bool _boosterAllowed = _isImportCmd && await IsBoosterAsync(cmd.User.Id);

			if (!_isWhitelistedTop && !_boosterAllowed)
			{
				await cmd.RespondAsync(
					embed: ErrorEmbed("permission denied", "nice try loser"),
					ephemeral: true);
				return;
			}

			bool ephemeral = true;

			try
			{
				switch (cmd.CommandName)
				{
                	case "instance-findall": await CmdInstanceFindAll(cmd, ephemeral); break;
					case "player-shove": await CmdPlayerForceMatchmake(cmd, ephemeral); break;
					case "player-ban": await CmdPlayerBan(cmd, ephemeral); break;
					case "player-unban": await CmdPlayerUnban(cmd, ephemeral); break;
					case "player-info": await CmdPlayerInfo(cmd, ephemeral); break;
					case "player-delete": await CmdPlayerDelete(cmd, ephemeral); break;
					case "player-rename": await CmdPlayerRename(cmd, ephemeral); break;
					case "player-roles": await CmdPlayerRoles(cmd, ephemeral); break;
					case "player-set-level": await CmdPlayerSetLevel(cmd, ephemeral); break;
					case "player-give-skin": await CmdPlayerGiveSkin(cmd, ephemeral); break;
					case "player-heartbeat": await CmdPlayerHeartbeat(cmd, ephemeral); break;
					case "player-heartbeat-clear": await CmdPlayerHeartbeatClear(cmd, ephemeral); break;
					case "player-reset-pfp": await CmdPlayerResetPfp(cmd, ephemeral); break;
                    case "player-upload-pfp": await CmdPlayerUploadPfp(cmd, ephemeral); break;
					case "player-repair": await CmdPlayerRepair(cmd, ephemeral); break;
					case "player-influencer": await CmdPlayerInfluencer(cmd, ephemeral); break;
					case "player-set-platformid": await CmdPlayerSetPlatformId(cmd, ephemeral); break;
					case "player-transfer-rooms": await CmdPlayerTransferRooms(cmd, ephemeral); break;
					case "account-create": await CmdAccountCreate(cmd, ephemeral); break;
					case "player-set-tokens": await CmdPlayerSetTokens(cmd, ephemeral); break;
					case "room-info": await CmdRoomInfo(cmd, ephemeral); break;
					case "room-delete": await CmdRoomDelete(cmd, ephemeral); break;
					case "room-reset-image": await CmdRoomResetImage(cmd, ephemeral); break;
					case "room-change-owner": await CmdRoomChangeOwner(cmd, ephemeral); break;
					case "room-set-role": await CmdRoomSetRole(cmd, ephemeral); break;
					case "room-toggle-beta": await CmdRoomToggleBeta(cmd, ephemeral); break;
					case "room-change-datablob": await CmdRoomChangeDatablob(cmd, ephemeral); break;
					case "room-clear": await CmdRoomClear(cmd, ephemeral); break;
                    case "player-delete-all-images": await CmdPlayerDeleteAllImages(cmd, ephemeral); break;
					case "room-import": await CmdRoomImport(cmd, ephemeral); break;
					case "import-valid": await CmdImportValid(cmd, ephemeral); break;
					case "room-set-image": await CmdRoomSetImage(cmd, ephemeral); break;
					case "db-reset": await CmdDbReset(cmd, ephemeral); break;
					case "db-info": await CmdDbInfo(cmd, ephemeral); break;
					case "upload-cdn": await CmdUploadCdn(cmd, ephemeral); break;
					case "upload-image": await CmdUploadImage(cmd, ephemeral); break;
					case "whitelist-add": await CmdWhitelistAdd(cmd, ephemeral); break;
					case "whitelist-remove": await CmdWhitelistRemove(cmd, ephemeral); break;
					case "whitelist-list": await CmdWhitelistList(cmd, ephemeral); break;
					case "room-find-by-name": await CmdRoomFindByName(cmd, ephemeral); break;
                    case "get-image-name-to-playerid": await CmdGetImageNameToPlayerId(cmd, ephemeral); break;
					case "room-find-by-id": await CmdRoomFindById(cmd, ephemeral); break;
					case "player-find-by-username": await CmdPlayerFindByUsername(cmd, ephemeral); break;
					case "player-find-by-id": await CmdPlayerFindById(cmd, ephemeral); break;
					case "db-execute": await CmdDbExecute(cmd, ephemeral); break;
                    case "rooms-delete-all-from-player": await CmdDeleteAllRoomsFromPlayer(cmd, ephemeral); break;
					case "server-scheduled-maintenance": await CmdScheduledMaintenance(cmd, ephemeral); break;
					default:
						await cmd.RespondAsync(
							embed: ErrorEmbed("Unknown Command", $"`{cmd.CommandName}` is not handled."),
							ephemeral: ephemeral);
						break;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[Discord] Unhandled exception in command {cmd}", cmd.CommandName);
				try
				{
					await cmd.RespondAsync(
						embed: ErrorEmbed("Internal Error", $"```\n{ex.Message}\n```"),
						ephemeral: ephemeral);
				}
				catch { }
			}
		}

		private async Task CmdStick(SocketSlashCommand cmd)
		{
			string message = (string)cmd.Data.Options.First(o => o.Name == "message").Value;
			var channelOption = cmd.Data.Options.FirstOrDefault(o => o.Name == "channel")?.Value as SocketChannel;
			var targetChannel = channelOption as ISocketMessageChannel ?? cmd.Channel;

			DateTime stickedAt = DateTime.UtcNow;

			lock (_stickyLock)
			{
				if (_stickyMessages.TryGetValue(targetChannel.Id, out var existing))
				{
					_ = TryDeleteStickyMessage(targetChannel, existing.MessageId);
					_stickyMessages.Remove(targetChannel.Id);
				}
			}

			var embed = BuildStickyEmbed(message, stickedAt);
			var sentMessage = await targetChannel.SendMessageAsync(embed: embed);

			lock (_stickyLock)
			{
				_stickyMessages[targetChannel.Id] = new StickyMessage
				{
					ChannelId = targetChannel.Id,
					MessageId = sentMessage.Id,
					Content = message,
					StickedAt = stickedAt
				};
			}

			await cmd.RespondAsync("done", ephemeral: true);
		}

		private async Task CmdStickStop(SocketSlashCommand cmd)
		{
			var channelOption = cmd.Data.Options.FirstOrDefault(o => o.Name == "channel")?.Value as SocketChannel;
			var targetChannel = channelOption as ISocketMessageChannel ?? cmd.Channel;

			StickyMessage? removed = null;
			lock (_stickyLock)
			{
				if (_stickyMessages.TryGetValue(targetChannel.Id, out var existing))
				{
					removed = existing;
					_stickyMessages.Remove(targetChannel.Id);
				}
			}

			if (removed == null)
			{
				await cmd.RespondAsync("done", ephemeral: true);
				return;
			}

			await TryDeleteStickyMessage(targetChannel, removed.MessageId);
			await cmd.RespondAsync("done", ephemeral: true);
		}

		private async Task TryDeleteStickyMessage(ISocketMessageChannel channel, ulong messageId)
		{
			try
			{
				var oldMessage = await channel.GetMessageAsync(messageId);
				if (oldMessage != null)
					await oldMessage.DeleteAsync();
			}
			catch { }
		}

		private Embed BuildStickyEmbed(string message, DateTime stickedAtUtc)
		{
			return new EmbedBuilder()
				.WithTitle("Vanadium")
				.WithDescription($"```\n{message}\n```")
				.WithColor(Color.Default)
				.WithFooter("Sticked at")
				.WithTimestamp(stickedAtUtc)
				.Build();
		}

		private async Task CmdStatus(SocketSlashCommand cmd, bool isPing)
		{
			await cmd.DeferAsync(ephemeral: false);

			var sw = Stopwatch.StartNew();
			bool isOnline = false;

			try
			{
				using var http = _httpFactory.CreateClient();
				var resp = await http.GetAsync($"{ServerConfig.BaseURL}/roomserver/rooms?name=MakerRoom");
				isOnline = resp.StatusCode == System.Net.HttpStatusCode.OK;
			}
			catch
			{
				isOnline = false;
			}

			sw.Stop();
			long ms = sw.ElapsedMilliseconds;

			string footerLabel = isPing ? "Ping" : "Status";

			var embed = new EmbedBuilder()
				.WithTitle("Vanadium")
				.WithDescription(isOnline ? $"Online {ms}ms" : $"Offline {ms}ms")
				.WithColor(Color.Default)
				.WithFooter(footerLabel)
				.WithTimestamp(DateTimeOffset.UtcNow)
				.Build();

			await cmd.FollowupAsync(embed: embed, ephemeral: false);
		}
        
        private async Task CmdDeleteAllRoomsFromPlayer(SocketSlashCommand cmd, bool ephemeral)
        {
            await cmd.DeferAsync(ephemeral: ephemeral);

            var playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;

            try
            {
                var roomsToDelete = RoomDB.Rooms.Find(r => r.CreatorAccountId == playerId).ToList();

                if (roomsToDelete.Count == 0)
                {
                    var emptyEmbed = new EmbedBuilder()
                        .WithTitle("No Rooms Found")
                        .WithDescription($"Player ID `{playerId}` does not have any rooms in the database.")
                        .WithColor(Color.Orange)
                        .WithFooter("Delete Rooms")
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    await cmd.FollowupAsync(embed: emptyEmbed, ephemeral: ephemeral);
                    return;
                }

                int deletedCount = 0;
                foreach (var room in roomsToDelete)
                {
                    RoomDB.SubRoomSaves.DeleteMany(s => s.RoomId == room.RoomId);

                    if (RoomDB.Rooms.Delete(room.RoomId))
                    {
                        deletedCount++;
                    }
                }

                var embed = new EmbedBuilder()
                    .WithTitle("Rooms Purged")
                    .WithDescription($"Successfully deleted **{deletedCount}** room(s) owned by Player ID `{playerId}`.")
                    .WithColor(Color.Green)
                    .WithFooter("Delete Rooms")
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await cmd.FollowupAsync(embed: embed, ephemeral: ephemeral);
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("Error")
                    .WithDescription($"Failed to clear player rooms: `{ex.Message}`")
                    .WithColor(Color.Red)
                    .WithFooter("Delete Rooms")
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await cmd.FollowupAsync(embed: errorEmbed, ephemeral: ephemeral);
            }
        }

        private async Task CmdPlayerDeleteAllImages(SocketSlashCommand cmd, bool eph)
        {
            ulong playerId = Convert.ToUInt64(
                cmd.Data.Options.First(x => x.Name == "playerid").Value);

            await cmd.DeferAsync(ephemeral: eph);

            var db = ImageMetadataDB.GetDb();
            var imageCol = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");

            var images = imageCol.Query()
                .Where(x => x.PlayerId == playerId)
                .ToList();

            int deletedFiles = 0;

            foreach (var image in images)
            {
                try
                {
                    string path = Path.Combine("Data", "Images", image.ImageName);

                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        deletedFiles++;
                    }
                }
                catch { }
            }

            ImageMetadataDB.DeleteImagesFromPlayerId(playerId);

            await DiscordBotController.BroadcastBotEvent("player-delete-all-images", new
            {
                playerId,
                deletedFiles
            });

            await cmd.FollowupAsync(
                embed: SuccessEmbed(
                    "Images Deleted",
                    $"Deleted `{images.Count}` image records and `{deletedFiles}` files belonging to (`{playerId}`)."
                ).Build(),
                ephemeral: eph);
        }

		private async Task CmdScheduledMaintenance(SocketSlashCommand cmd, bool eph)
		{
			var minutesOption = cmd.Data.Options.FirstOrDefault(o => o.Name == "minutes");
			if (minutesOption == null || minutesOption.Value == null)
			{
				await cmd.RespondAsync(embed: ErrorEmbed("Missing Argument", "Please specify the minutes option."), ephemeral: eph);
				return;
			}

			int minutes = Convert.ToInt32(minutesOption.Value);

			await cmd.DeferAsync(ephemeral: eph);

			try
			{
				await Vanadium.Controllers.NotificationsController.SendMaintenance(minutes);

				var eb = new EmbedBuilder()
					.WithTitle("🚨 Maintenance Alert Dispatched")
					.WithDescription($"The scheduled maintenance broadcast has been successfully sent out over SignalR websockets.")
					.WithColor(Color.Orange)
					.AddField("Countdown Triggered", $"`{minutes}` minutes remaining", true)
					.AddField("Target Audience", "📢 All connected game clients", true)
					.WithFooter("Vanadium • Server Controller")
					.WithCurrentTimestamp()
					.Build();

				await cmd.FollowupAsync(embed: eb, ephemeral: eph);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[Discord] Failed to dispatch maintenance sequence.");
				await cmd.FollowupAsync(embed: ErrorEmbed("Broadcast Failure", $"Failed to alert game servers: {ex.Message}"), ephemeral: eph);
			}
		}
        
        private async Task CmdInstanceFindAll(SocketSlashCommand cmd, bool eph)
		{
			long instanceId = (long)cmd.Data.Options.First(o => o.Name == "instanceid").Value;

			await cmd.DeferAsync(ephemeral: eph);

			var matches = PlayerDB.Players
				.FindAll()
				.Where(x =>
					x.Player?.PlayerExtra?.Heartbeat?.isOnline == true &&
					x.Player.PlayerExtra.Heartbeat.roomInstance != null &&
					x.Player.PlayerExtra.Heartbeat.roomInstance.roomInstanceId == instanceId)
				.ToList();

			if (matches.Count == 0)
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("No Players Found", $"No online players found in instance `{instanceId}`."),
					ephemeral: eph);
				return;
			}

			var firstMatch = matches.First();
			long roomId = firstMatch.Player!.PlayerExtra.Heartbeat.roomInstance!.roomId;
			var room = RoomDB.GetRoom(roomId);
			string roomLabel = room?.Name ?? roomId.ToString();

			var eb = new EmbedBuilder()
				.WithTitle($"Instance — {roomLabel}")
				.WithDescription($"Found **{matches.Count}** player(s) in instance `{instanceId}`.")
				.WithColor(RandomColor())
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{room?.ImageName}")
				.WithFooter("Vanadium • Instance FindAll")
				.WithCurrentTimestamp();

			var sb = new StringBuilder();
			int fieldIndex = 1;

			foreach (var player in matches)
			{
				string line = $"`{player.PlayerId}` — **{player.Player?.DisplayName ?? player.Player?.Username ?? "Unknown"}** (`{player.Player?.Username ?? "?"}`)\n";
				if (sb.Length + line.Length > 1000)
				{
					eb.AddField($"Players (page {fieldIndex})", sb.ToString().TrimEnd(), false);
					sb.Clear();
					fieldIndex++;
				}
				sb.Append(line);
			}

			if (sb.Length > 0)
				eb.AddField($"Players (page {fieldIndex})", sb.ToString().TrimEnd(), false);

			await DiscordBotController.BroadcastBotEvent("instance-findall", new
			{
				instanceId,
				roomId,
				playerCount = matches.Count,
				players = matches.Select(x => new { x.PlayerId, x.Player?.Username }).ToList()
			});

			await cmd.FollowupAsync(embed: eb.Build(), ephemeral: eph);
		}

		private async Task CmdPlayerForceMatchmake(SocketSlashCommand cmd, bool eph)
		{
			string username = (string)cmd.Data.Options.First(o => o.Name == "username").Value;
			long roomId = (long)cmd.Data.Options.First(o => o.Name == "roomid").Value;
			long? subRoomId = cmd.Data.Options.FirstOrDefault(o => o.Name == "subroomid")?.Value is long srid ? srid : (long?)null;

			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindOne(p =>
				p.Player != null && p.Player.Username != null &&
				p.Player.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with username `{username}`."), ephemeral: eph);
				return;
			}

			var room = RoomDB.GetRoom(roomId);
			if (room == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"Target Room ID `{roomId}` does not exist."), ephemeral: eph);
				return;
			}

			var heartbeat = Sessions.CreateRoom(
				player.PlayerId,
				roomId,
				subRoomId,
				joinMode: 0,
				additionalPlayersAutoFollow: false);

			player.Player.PlayerExtra ??= new PlayerExtra();
			player.Player.PlayerExtra.Heartbeat = heartbeat;
			PlayerDB.Players.Update(player);

			await NotiController.SendPresenceUpdate(player.PlayerId, heartbeat);

			var friends = PlayerDB.Players.FindAll()
				.Where(p => p.Player?.Relationships != null &&
							p.Player.Relationships.Any(r => r.PlayerId == player.PlayerId && r.RelationshipType == 5))
				.Select(p => p.PlayerId)
				.ToList();

			foreach (var fid in friends)
			{
				await NotiController.SendPresenceUpdate(fid, heartbeat);
			}

			string roomName = room.Name;
			await DiscordBotController.BroadcastBotEvent("player-shove", new
			{
				adminUser = cmd.User.Username,
				targetPlayerId = player.PlayerId,
				targetUsername = player.Player.Username,
				roomId,
				roomName
			});

			var eb = SuccessEmbed("Shove Success", $"Successfully shoved **{player.Player.DisplayName}**.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player.ProfileImage}")
				.AddField("Player Target", $"{player.Player.DisplayName} (`{player.PlayerId}`)", true)
				.AddField("Forced Destination", $"{roomName} (`{roomId}`)", true)
				.AddField("SubRoom ID", subRoomId.HasValue ? $"`{subRoomId.Value}`" : "`None`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerInfo(SocketSlashCommand cmd, bool eph)
		{
			string search = (string)cmd.Data.Options.First(o => o.Name == "search").Value;
			await cmd.DeferAsync(ephemeral: eph);

			FullPlayer? player = null;
			if (long.TryParse(search, out long pid))
				player = PlayerDB.Players.FindOne(p => p.PlayerId == pid);
			else
				player = PlayerDB.Players.FindOne(p =>
					p.Player.Username.Equals(search, StringComparison.OrdinalIgnoreCase));

			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Player Not Found", $"No player matched `{search}`."), ephemeral: eph);
				return;
			}

			var p = player.Player;
			var hb = p.PlayerExtra?.Heartbeat;
			string onlineStatus = (hb?.isOnline == true) ? "Online" : "Offline";
			string roomStr = hb?.roomInstance != null ? $"Room `{hb.roomInstance.roomId}`" : "—";
			string rolesStr = player.PlayerRoles?.Count > 0
				? string.Join(", ", player.PlayerRoles.Select(r => $"`{r}`"))
				: "None";
			string pfpUrl = $"{ServerConfig.BaseURL}/imageserver/{p.ProfileImage}";

			var eb = new EmbedBuilder()
				.WithTitle($"{p.DisplayName ?? p.Username}")
				.WithDescription($"Detailed profile for player **{p.Username}**")
				.WithColor(RandomColor())
				.WithThumbnailUrl(pfpUrl)
				.AddField("Player ID", $"`{player.PlayerId}`", true)
				.AddField("Username", $"`{p.Username}`", true)
				.AddField("Display Name", $"`{p.DisplayName}`", true)
				.AddField("Status", onlineStatus, true)
				.AddField("Current Room", roomStr, true)
				.AddField("Dorm Room ID", $"`{p.PlayerExtra?.DormRoomId}`", true)
				.AddField("Roles", rolesStr, false)
				.AddField("Created", $"<t:{ToUnix(p.CreatedAt)}:F>", true)
				.AddField("Last Login", $"<t:{ToUnix(p.LastLoginAt)}:F>", true)
				.AddField("Junior", p.IsJunior == true ? "Yes" : "No", true)
				.AddField("Level / XP", $"Level `{p.Level}` — `{p.XP}` XP", true)
				.AddField("Profile Image", $"`{p.ProfileImage}`", true)
				.WithFooter("Vanadium • Player Info")
				.WithCurrentTimestamp()
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerUnban(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;

			await cmd.DeferAsync(ephemeral: eph);

			var targetPlayer = PlayerDB.Players.FindById(playerId);

			if (targetPlayer == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."), ephemeral: eph);
				return;
			}

			ulong adminPlayerId = cmd.User.Id;

			targetPlayer.Player ??= new PlayerDBClasses.Player();
			targetPlayer.Player.PlayerExtra ??= new PlayerExtra();
			targetPlayer.Player.PlayerExtra.ModerationBlockDetails = new ModerationBlockDetails
			{
				IsBan = false,
			};

			PlayerDB.Players.Update(targetPlayer);

			await DiscordBotController.BroadcastBotEvent("player-ban", new
			{
				playerId,
				displayName = targetPlayer.Player?.DisplayName ?? "Unknown",
				bannedBy = adminPlayerId.ToString()
			});

			var eb = SuccessEmbed("Player Banned", $"**{targetPlayer.Player?.DisplayName ?? "Unknown"}** has been unbanned from Vanadium.net.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{targetPlayer.Player?.ProfileImage}")
				.AddField("Player ID", $"`{playerId}`", true)
				.AddField("Banned By", $"<@{adminPlayerId}>", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerBan(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			string reason = (string)cmd.Data.Options.FirstOrDefault(o => o.Name == "reason")?.Value ?? "You have been banned from the server.";
			int duration = cmd.Data.Options.FirstOrDefault(o => o.Name == "duration")?.Value is long d ? (int)d : 99999;

			if (cmd.User.Id != 1505639244142608594 && cmd.User.Id != 1034585922186002432 && cmd.User.Id != 1228278497194147860 && cmd.User.Id != 1252146617772015616)
			{
				await cmd.RespondAsync(embed: ErrorEmbed("Access Denied", "You are not authorized to use this administrative tool."), ephemeral: true);
				return;
			}

			await cmd.DeferAsync(ephemeral: eph);

			var targetPlayer = PlayerDB.Players.FindById(playerId);

			if (targetPlayer == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."), ephemeral: eph);
				return;
			}

			ulong adminPlayerId = cmd.User.Id;

			targetPlayer.Player ??= new PlayerDBClasses.Player();
			targetPlayer.Player.PlayerExtra ??= new PlayerExtra();

			targetPlayer.Player.PlayerExtra.ModerationBlockDetails = new ModerationBlockDetails
			{
				IsBan = true,
				ReportCategory = ReportCategory.Moderator,
				Duration = duration,
				Message = reason,
				BannedByPlayerId = adminPlayerId,
				ModerationSetUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
			};

			targetPlayer.Player.PlayerExtra.Heartbeat = new Heartbeat
			{
				playerId = playerId,
				isOnline = false,
				roomInstance = null,
				errorCode = 0
			};

			PlayerDB.Players.Update(targetPlayer);

			await NotiController.SendPresenceUpdate(playerId, targetPlayer.Player.PlayerExtra.Heartbeat);
			await NotiController.SendAccountUpdate(playerId, PlayerDB.GetAccountMe(playerId));

			await DiscordBotController.BroadcastBotEvent("player-ban", new
			{
				playerId,
				displayName = targetPlayer.Player?.DisplayName ?? "Unknown",
				bannedBy = adminPlayerId.ToString()
			});

			var eb = SuccessEmbed("Player Banned", $"**{targetPlayer.Player?.DisplayName ?? "Unknown"}** has been banned from Vanadium.net and returned to their dorm.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{targetPlayer.Player?.ProfileImage}")
				.AddField("Player ID", $"`{playerId}`", true)
				.AddField("Banned By", $"<@{adminPlayerId}>", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerDelete(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindOne(p => p.PlayerId == playerId);
			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."), ephemeral: eph);
				return;
			}

			string name = player.Player?.DisplayName ?? player.Player?.Username ?? playerId.ToString();
			PlayerDB.Players.Delete(playerId);

			await DiscordBotController.BroadcastBotEvent("player-delete", new
			{
				playerId,
				name
			});

			var eb = SuccessEmbed("Account Deleted", $"Player **{name}** (`{playerId}`) has been permanently deleted.")
				.AddField("Player ID", $"`{playerId}`", true)
				.AddField("Name", name, true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerRename(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			string newName = (string)cmd.Data.Options.First(o => o.Name == "username").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindOne(p => p.PlayerId == playerId);
			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."), ephemeral: eph);
				return;
			}

			string oldName = player.Player.Username;
			player.Player.Username = newName;
			player.Player.DisplayName = newName;
			PlayerDB.Players.Update(player);

			var updatedAccount = PlayerDB.GetAccountMe(playerId);
			await NotiController.SendAccountUpdate(playerId, updatedAccount);
			var renameFriends = PlayerDB.Players.FindAll()
				.Where(p => p.Player?.Relationships != null &&
							p.Player.Relationships.Any(r => r.PlayerId == playerId && r.RelationshipType == 5))
				.Select(p => p.PlayerId)
				.ToList();
			foreach (var fid in renameFriends)
				await NotiController.SendAccountUpdate(fid, updatedAccount);

			await DiscordBotController.BroadcastBotEvent("player-rename", new
			{
				playerId,
				oldName,
				newName
			});

			var eb = SuccessEmbed("Username Changed", $"Player `{playerId}` has been renamed.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player.ProfileImage}")
				.AddField("Old Username", $"`{oldName}`", true)
				.AddField("New Username", $"`{newName}`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerRoles(SocketSlashCommand cmd, bool eph)
		{
			string username = (string)cmd.Data.Options.First(o => o.Name == "username").Value;
			string roleStr = (string)cmd.Data.Options.First(o => o.Name == "role").Value;
			string type = (string)cmd.Data.Options.First(o => o.Name == "type").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindOne(p =>
				p.Player != null && p.Player.Username != null &&
				p.Player.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with username `{username}`."), ephemeral: eph);
				return;
			}

			if (!Enum.TryParse<PlayerRoles>(roleStr, out var role))
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Invalid Role", $"`{roleStr}` is not a valid role."), ephemeral: eph);
				return;
			}

			player.PlayerRoles ??= new List<PlayerRoles>();

			bool hasRole = player.PlayerRoles.Contains(role);

			if (type == "add")
			{
				if (hasRole)
				{
					await cmd.FollowupAsync(embed: ErrorEmbed("Already Has Role", $"**{player.Player.DisplayName}** already has the **{roleStr}** role."), ephemeral: eph);
					return;
				}
				player.PlayerRoles.Add(role);
			}
			else
			{
				if (!hasRole)
				{
					await cmd.FollowupAsync(embed: ErrorEmbed("Role Not Found", $"**{player.Player.DisplayName}** does not have the **{roleStr}** role."), ephemeral: eph);
					return;
				}
				player.PlayerRoles.Remove(role);
			}

			PlayerDB.Players.Update(player);

			var updatedAccount = PlayerDB.GetAccountMe(player.PlayerId);
			await NotiController.SendAccountUpdate(player.PlayerId, updatedAccount);

			await DiscordBotController.BroadcastBotEvent("player-roles-change", new
			{
				playerId = player.PlayerId,
				action = type == "add" ? "granted" : "stripped",
				role = roleStr,
				roles = player.PlayerRoles?.Select(r => r.ToString()).ToList()
			});

			string rolesStr = player.PlayerRoles?.Count > 0
				? string.Join(", ", player.PlayerRoles.Select(r => $"`{r}`"))
				: "None";

			var eb = SuccessEmbed($"Role {(type == "add" ? "Added" : "Removed")}", $"Role **{roleStr}** {(type == "add" ? "granted to" : "removed from")} **{player.Player.DisplayName}**.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player.ProfileImage}")
				.AddField("Player", $"`{player.PlayerId}` — {player.Player.DisplayName}", true)
				.AddField("Role Changed", $"`{roleStr}`", true)
				.AddField("Action", type == "add" ? "Added" : "Removed", true)
				.AddField("All Roles Now", rolesStr, false)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerGiveSkin(SocketSlashCommand cmd, bool eph)
		{
			string username = (string)cmd.Data.Options.First(o => o.Name == "username").Value;
			string skinName = (string)cmd.Data.Options.First(o => o.Name == "skin").Value;
			string type = (string)cmd.Data.Options.First(o => o.Name == "type").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindOne(p =>
				p.Player != null && p.Player.Username != null &&
				p.Player.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with username `{username}`."), ephemeral: eph);
				return;
			}

			string equipmentPath = Path.Join(Program.dataDir, "APIS", "Items", "Equipment.json");
			if (!File.Exists(equipmentPath))
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Equipment Not Found", "Equipment.json could not be located on the server."), ephemeral: eph);
				return;
			}

			var allItems = System.Text.Json.JsonSerializer.Deserialize<List<OwnedEquipmentItem>>(
				await File.ReadAllTextAsync(equipmentPath),
				new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

			var targetItem = allItems?.FirstOrDefault(i =>
				string.Equals(i.FriendlyName, skinName, StringComparison.OrdinalIgnoreCase));

			if (targetItem == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Skin Not Found", $"No skin matching `{skinName}` exists in Equipment.json."), ephemeral: eph);
				return;
			}

			player.Player.PlayerExtra ??= new PlayerExtra();
			player.Player.PlayerExtra.OwnedEquipment ??= new List<OwnedEquipmentItem>();

			bool alreadyOwns = player.Player.PlayerExtra.OwnedEquipment
				.Any(e => string.Equals(e.ModificationGuid, targetItem.ModificationGuid, StringComparison.OrdinalIgnoreCase));

			if (type == "add")
			{
				if (alreadyOwns)
				{
					await cmd.FollowupAsync(embed: ErrorEmbed("Already Owned", $"**{player.Player.DisplayName}** already owns **{skinName}**."), ephemeral: eph);
					return;
				}

				player.Player.PlayerExtra.OwnedEquipment.Add(new OwnedEquipmentItem
				{
					PrefabName = targetItem.PrefabName,
					ModificationGuid = targetItem.ModificationGuid,
					FriendlyName = targetItem.FriendlyName,
					PlatformMask = targetItem.PlatformMask,
					Tooltip = targetItem.Tooltip,
					Rarity = targetItem.Rarity,
					Favorited = false
				});

				PlayerDB.Players.Update(player);

				var eb = SuccessEmbed("Skin Granted", $"**{skinName}** has been granted to **{player.Player.DisplayName}**.")
					.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player.ProfileImage}")
					.AddField("Player", $"`{player.PlayerId}` — {player.Player.DisplayName}", true)
					.AddField("Skin", skinName, true)
					.AddField("Action", "`Add`", true)
					.Build();

				await cmd.FollowupAsync(embed: eb, ephemeral: eph);
			}
			else
			{
				if (!alreadyOwns)
				{
					await cmd.FollowupAsync(embed: ErrorEmbed("Not Owned", $"**{player.Player.DisplayName}** does not own **{skinName}**."), ephemeral: eph);
					return;
				}

				player.Player.PlayerExtra.OwnedEquipment.RemoveAll(e =>
					string.Equals(e.ModificationGuid, targetItem.ModificationGuid, StringComparison.OrdinalIgnoreCase));

				PlayerDB.Players.Update(player);

				var eb = SuccessEmbed("Skin Removed", $"**{skinName}** has been removed from **{player.Player.DisplayName}**.")
					.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player.ProfileImage}")
					.AddField("Player", $"`{player.PlayerId}` — {player.Player.DisplayName}", true)
					.AddField("Skin", skinName, true)
					.AddField("Action", "`Remove`", true)
					.Build();

				await cmd.FollowupAsync(embed: eb, ephemeral: eph);
			}
		}

		private async Task CmdPlayerHeartbeat(SocketSlashCommand cmd, bool eph)
		{
			string search = (string)cmd.Data.Options.First(o => o.Name == "search").Value;
			await cmd.DeferAsync(ephemeral: eph);

			FullPlayer? player = null;
			if (long.TryParse(search, out long pid))
				player = PlayerDB.Players.FindOne(p => p.PlayerId == pid);
			else
				player = PlayerDB.Players.FindOne(p =>
					p.Player.Username.Equals(search, StringComparison.OrdinalIgnoreCase));

			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player matched `{search}`."), ephemeral: eph);
				return;
			}

			var hb = player.Player.PlayerExtra?.Heartbeat;
			string onlineStatus = (hb?.isOnline == true) ? "**Online**" : "**Offline**";
			string roomStr = hb?.roomInstance != null ? $"Room `{hb.roomInstance.roomId}`" : "—";
			string errorCode = hb?.errorCode?.ToString() ?? "0";

			var eb = new EmbedBuilder()
				.WithTitle($"Heartbeat — {player.Player.DisplayName}")
				.WithColor(RandomColor())
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player.ProfileImage}")
				.AddField("Player ID", $"`{player.PlayerId}`", true)
				.AddField("Status", onlineStatus, true)
				.AddField("Room", roomStr, true)
				.AddField(" Error Code", $"`{errorCode}`", true)
				.WithFooter("Vanadium • Heartbeat")
				.WithCurrentTimestamp()
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerHeartbeatClear(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindOne(p => p.PlayerId == playerId);
			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."), ephemeral: eph);
				return;
			}

			player.Player.PlayerExtra ??= new PlayerExtra();
			player.Player.PlayerExtra.Heartbeat = new Heartbeat
			{
				playerId = playerId,
				isOnline = false,
				roomInstance = null,
				errorCode = 0
			};
			PlayerDB.Players.Update(player);

			await NotiController.SendPresenceUpdate(playerId, player.Player.PlayerExtra.Heartbeat);

			await DiscordBotController.BroadcastBotEvent("player-heartbeat-clear", new
			{
				playerId,
				displayName = player.Player.DisplayName
			});

			var eb = SuccessEmbed("Heartbeat Cleared", $"Heartbeat for **{player.Player.DisplayName}** has been reset.")
				.AddField("Player ID", $"`{playerId}`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerResetPfp(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindOne(p => p.PlayerId == playerId);
			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."), ephemeral: eph);
				return;
			}

			player.Player.ProfileImage = "DefaultPFP.png";
			PlayerDB.Players.Update(player);

			var pfpUpdatedAccount = PlayerDB.GetAccountMe(playerId);
			await NotiController.SendAccountUpdate(playerId, pfpUpdatedAccount);
			var pfpFriends = PlayerDB.Players.FindAll()
				.Where(p => p.Player?.Relationships != null &&
							p.Player.Relationships.Any(r => r.PlayerId == playerId && r.RelationshipType == 5))
				.Select(p => p.PlayerId)
				.ToList();
			foreach (var fid in pfpFriends)
				await NotiController.SendAccountUpdate(fid, pfpUpdatedAccount);

			await DiscordBotController.BroadcastBotEvent("player-reset-pfp", new
			{
				playerId,
				displayName = player.Player.DisplayName
			});

			var eb = SuccessEmbed("Profile Picture Reset", $"**{player.Player.DisplayName}**'s avatar has been set to the default.")
				.AddField("Player ID", $"`{playerId}`", true)
				.AddField("New Image", "`DefaultPFP.png`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}
        
        private async Task CmdPlayerUploadPfp(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			var att = (IAttachment)cmd.Data.Options.First(o => o.Name == "file").Value;

			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindById(playerId);
			if (player == null)
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."),
					ephemeral: eph);
				return;
			}

			byte[] imgBytes;
			try
			{
				using var http = _httpFactory.CreateClient();
				imgBytes = await http.GetByteArrayAsync(att.Url);
			}
			catch (Exception ex)
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Download Failed", $"```\n{ex.Message}\n```"),
					ephemeral: eph);
				return;
			}

			byte[] pngBytes;
			try
			{
				using var ms = new MemoryStream(imgBytes);
				using var img = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(ms);
				using var outMs = new MemoryStream();
				await img.SaveAsync(outMs, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
				pngBytes = outMs.ToArray();
			}
			catch (Exception ex)
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Image Processing Failed", $"```\n{ex.Message}\n```"),
					ephemeral: eph);
				return;
			}

			string imagesDir = Path.Combine(Environment.CurrentDirectory, "Data", "Images");
			Directory.CreateDirectory(imagesDir);
			string fileName = $"{Guid.NewGuid()}.png";
			string filePath = Path.Combine(imagesDir, fileName);
			await File.WriteAllBytesAsync(filePath, pngBytes);

			string oldPfp = player.Player.ProfileImage ?? "DefaultPFP.png";
			player.Player.ProfileImage = fileName;
			PlayerDB.Players.Update(player);

			var updatedAccount = PlayerDB.GetAccountMe(playerId);
			await NotiController.SendAccountUpdate(playerId, updatedAccount);

			var pfpFriends = PlayerDB.Players.FindAll()
				.Where(p => p.Player?.Relationships != null &&
							p.Player.Relationships.Any(r => r.PlayerId == playerId && r.RelationshipType == 5))
				.Select(p => p.PlayerId)
				.ToList();

			foreach (var fid in pfpFriends)
				await NotiController.SendAccountUpdate(fid, updatedAccount);

			await DiscordBotController.BroadcastBotEvent("player-upload-pfp", new
			{
				playerId,
				displayName = player.Player.DisplayName,
				oldPfp,
				newPfp = fileName
			});

			var eb = SuccessEmbed("Profile Picture Updated", $"**{player.Player.DisplayName}**'s profile picture has been updated to what u wanted it to be.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{fileName}")
				.AddField("Player ID", $"`{playerId}`", true)
				.AddField("Old Image", $"`{oldPfp}`", true)
				.AddField("New Image", $"`{fileName}`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerRepair(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindOne(p => p.PlayerId == playerId);
			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."), ephemeral: eph);
				return;
			}

			var now = DateTime.UtcNow;
			if (player.Player.CreatedAt == default || player.Player.CreatedAt.Year == 1)
				player.Player.CreatedAt = now;
			if (player.Player.LastLoginAt == default || player.Player.LastLoginAt.Year == 1)
				player.Player.LastLoginAt = now;
			player.Player.IsJunior ??= false;
			if (string.IsNullOrWhiteSpace(player.Player.ProfileImage))
				player.Player.ProfileImage = "DefaultPFP.png";
			player.Player.PlayerExtra ??= new PlayerExtra();
			player.Player.PlayerExtra.Avatar ??= new Avatar();
			player.Player.PlayerExtra.Influencer ??= new Influencer();
			player.Player.PlayerExtra.Heartbeat ??= new Heartbeat();
			player.Player.PlayerExtra.Heartbeat.isOnline = false;
			player.Player.PlayerExtra.Heartbeat.roomInstance = null;
			player.Player.PlayerExtra.Heartbeat.errorCode = 0;

			if (player.Player.PlayerExtra.DormRoomId == 0)
			{
				bool dormExists = RoomDB.DoesPlayerDormExist(player.PlayerId);
				if (!dormExists)
				{
					long newDormId = RoomDB.CloneDormRoom(player.PlayerId);
					player.Player.PlayerExtra.DormRoomId = newDormId;
				}
				else
				{
					var dorm = RoomDB.GetPlayerDormRoom(player.PlayerId);
					if (dorm != null)
						player.Player.PlayerExtra.DormRoomId = dorm.RoomId;
				}
			}

			PlayerDB.Players.Update(player);

			var repairedAccount = PlayerDB.GetAccountMe(playerId);
			await NotiController.SendAccountUpdate(playerId, repairedAccount);
			await NotiController.SendPresenceUpdate(playerId, player.Player.PlayerExtra.Heartbeat);

			await DiscordBotController.BroadcastBotEvent("player-repair", new
			{
				playerId,
				displayName = player.Player.DisplayName,
				dormRoomId = player.Player.PlayerExtra.DormRoomId
			});

			var eb = SuccessEmbed("Player Repaired", $"**{player.Player.DisplayName}** has been fully repaired.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player.ProfileImage}")
				.AddField("Player ID", $"`{playerId}`", true)
				.AddField("Dorm Room", $"`{player.Player.PlayerExtra.DormRoomId}`", true)
				.AddField("Created At", $"<t:{ToUnix(player.Player.CreatedAt)}:F>", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerInfluencer(SocketSlashCommand cmd, bool eph)
		{
			string username = (string)cmd.Data.Options.First(o => o.Name == "username").Value;
			string type = (string)cmd.Data.Options.First(o => o.Name == "type").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindOne(p =>
				p.Player != null && p.Player.Username != null &&
				p.Player.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with username `{username}`."), ephemeral: eph);
				return;
			}

			player.Player.PlayerExtra ??= new PlayerExtra();
			player.Player.PlayerExtra.Influencer ??= new Influencer();
			bool isAdd = type == "add";
			player.Player.PlayerExtra.Influencer.IsInfluencer = isAdd;
			PlayerDB.Players.Update(player);

			await NotiController.SendEvent(player.PlayerId, NotiController.EventTypes.InfluencerSupportedUpdate, new { IsInfluencer = isAdd, AccountId = player.PlayerId });

			await DiscordBotController.BroadcastBotEvent("player-influencer", new
			{
				playerId = player.PlayerId,
				displayName = player.Player.DisplayName,
				action = type
			});

			var eb = SuccessEmbed($"Influencer Status {(isAdd ? "Granted" : "Removed")}", $"**{player.Player.DisplayName}** is {(isAdd ? "now" : "no longer")} an influencer.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player.ProfileImage}")
				.AddField("Player", $"`{player.PlayerId}` — {player.Player.DisplayName}", true)
				.AddField("Is Influencer", $"`{isAdd}`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerSetPlatformId(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			string pidStr = (string)cmd.Data.Options.First(o => o.Name == "platformid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			if (!ulong.TryParse(pidStr, out _))
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Invalid Input", $"`{pidStr}` is not a valid platform ID."), ephemeral: eph);
				return;
			}

			var player = PlayerDB.Players.FindById(playerId);
			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."), ephemeral: eph);
				return;
			}

			if (player.PlatformIds == null || player.PlatformIds.Count == 0)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("No Platform IDs", "This player has no existing platform IDs."), ephemeral: eph);
				return;
			}

			string old = player.PlatformIds[0].PlatformId;
			player.PlatformIds[0].PlatformId = pidStr;
			PlayerDB.Players.Update(player);

			await DiscordBotController.BroadcastBotEvent("player-set-platformid", new
			{
				playerId,
				displayName = player.Player.DisplayName,
				oldPlatformId = old,
				newPlatformId = pidStr
			});

			var eb = SuccessEmbed("Platform ID Updated", $"First platform ID for **{player.Player.DisplayName}** changed.")
				.AddField("Player ID", $"`{playerId}`", true)
				.AddField("Old Platform ID", $"`{old}`", true)
				.AddField("New Platform ID", $"`{pidStr}`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerTransferRooms(SocketSlashCommand cmd, bool eph)
		{
			long oldId = (long)cmd.Data.Options.First(o => o.Name == "oldplayerid").Value;
			long newId = (long)cmd.Data.Options.First(o => o.Name == "newplayerid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var oldPlayer = PlayerDB.Players.FindOne(p => p.PlayerId == oldId);
			var newPlayer = PlayerDB.Players.FindOne(p => p.PlayerId == newId);

			if (oldPlayer == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"Old player `{oldId}` not found."), ephemeral: eph);
				return;
			}
			if (newPlayer == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"New player `{newId}` not found."), ephemeral: eph);
				return;
			}

			var rooms = RoomDB.Rooms.Find(r => r.CreatorAccountId == oldId).ToList();
			foreach (var room in rooms)
			{
				room.CreatorAccountId = newId;
				room.Roles ??= new List<RoomDBClasses.Roles>();
				room.Roles.RemoveAll(r => r.AccountId == oldId && r.Role == RoomDBClasses.Role.Creator);
				room.Roles.RemoveAll(r => r.AccountId == newId && r.Role == RoomDBClasses.Role.Creator);
				room.Roles.Add(new RoomDBClasses.Roles
				{
					AccountId = newId,
					Role = RoomDBClasses.Role.Creator,
					InvitedRole = RoomDBClasses.Role.Creator,
					LastChangedByAccountId = newId
				});
				if (room.SubRooms != null)
					foreach (var sub in room.SubRooms)
					{
						sub.CreatorAccountId = newId;
						sub.SavedByAccountId = newId;
					}
				RoomDB.Rooms.Update(room);
			}

			var eb = SuccessEmbed("Rooms Transferred", $"`{rooms.Count}` room(s) moved from **{oldPlayer.Player.DisplayName}** → **{newPlayer.Player.DisplayName}**.")
				.AddField("Old Owner", $"`{oldId}` — {oldPlayer.Player.DisplayName}", true)
				.AddField("New Owner", $"`{newId}` — {newPlayer.Player.DisplayName}", true)
				.AddField("Rooms Transferred", $"`{rooms.Count}`", true)
				.Build();

			var onlinePlayerIdsForTransfer = PlayerDB.Players
				.FindAll()
				.Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
				.Select(p => p.PlayerId)
				.ToList();
			foreach (var pid in onlinePlayerIdsForTransfer)
				foreach (var transferredRoom in rooms)
					await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(transferredRoom.RoomId));

			await DiscordBotController.BroadcastBotEvent("player-transfer-rooms", new
			{
				oldOwnerId = oldId,
				oldOwnerName = oldPlayer.Player.DisplayName,
				newOwnerId = newId,
				newOwnerName = newPlayer.Player.DisplayName,
				roomCount = rooms.Count
			});

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdAccountCreate(SocketSlashCommand cmd, bool eph)
		{
			await cmd.DeferAsync(ephemeral: eph);

			var discordUser = (IUser)cmd.Data.Options.First(o => o.Name == "user").Value;
			var username = (string)cmd.Data.Options.First(o => o.Name == "username").Value;
			var platformIdStr = (string)cmd.Data.Options.First(o => o.Name == "platformid").Value;

			if (!ulong.TryParse(platformIdStr, out _))
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Account Create Failed", $"`{platformIdStr}` is not a valid platform ID."),
					ephemeral: eph);
				return;
			}

			var existing = PlayerDB.Players.FindOne(p =>
				p.Player != null &&
				p.Player.Username != null &&
				p.Player.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

			if (existing != null)
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Account Create Failed", $"Username `{username}` is already taken (Player ID: `{existing.PlayerId}`)."),
					ephemeral: eph);
				return;
			}

			var alreadyLinked = PlayerDB.Players.FindOne(p =>
				p.Player != null && p.Player.DiscordUserId == discordUser.Id.ToString());

			if (alreadyLinked != null)
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Account Create Failed", $"<@{discordUser.Id}> is already linked to Player ID `{alreadyLinked.PlayerId}` (`{alreadyLinked.Player?.Username}`)."),
					ephemeral: eph);
				return;
			}

			var platformTaken = PlayerDB.Players.FindOne(p =>
				p.PlatformIds != null &&
				p.PlatformIds.Any(pid => pid.Platform == Platforms.Steam && pid.PlatformId == platformIdStr));

			if (platformTaken != null)
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Account Create Failed", $"Platform ID `{platformIdStr}` is already linked to Player ID `{platformTaken.PlayerId}`."),
					ephemeral: eph);
				return;
			}

			var newPlayer = PlayerDB.CreateAccount(Platforms.Steam, platformIdStr, false);
			newPlayer.Player!.Username = username;
			newPlayer.Player.DisplayName = username;
			newPlayer.Player.DiscordUserId = discordUser.Id.ToString();
			PlayerDB.Players.Update(newPlayer);

			await cmd.FollowupAsync(
				embed: SuccessEmbed("Account Created",
					$"**Player ID:** `{newPlayer.PlayerId}`\n" +
					$"**Username:** `{username}`\n" +
					$"**Platform ID:** `{platformIdStr}`\n" +
					$"**Discord:** <@{discordUser.Id}> (`{discordUser.Id}`)")
					.Build(),
				ephemeral: eph);
		}

		private async Task CmdPlayerSetTokens(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			int amount = (int)(long)cmd.Data.Options.First(o => o.Name == "amount").Value;
			long rawBalanceType = cmd.Data.Options.FirstOrDefault(o => o.Name == "balancetype")?.Value is long bt ? bt : 0L;
			await cmd.DeferAsync(ephemeral: eph);

			if (!Enum.IsDefined(typeof(BalanceType), (int)rawBalanceType))
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Invalid Balance Type", $"`{rawBalanceType}` is not a valid BalanceType value."),
					ephemeral: eph);
				return;
			}

			var balanceType = (BalanceType)(int)rawBalanceType;

			var player = PlayerDB.Players.FindById(playerId);
			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."), ephemeral: eph);
				return;
			}

			player.Player.PlayerExtra ??= new PlayerExtra();
			player.Player.PlayerExtra.Currencies ??= new List<PlayerCurrency>();

			var existing = player.Player.PlayerExtra.Currencies
				.FirstOrDefault(c => c.CurrencyType == CurrencyType.RecCenterTokens && c.BalanceType == balanceType);

			int oldBalance = 0;

			if (existing != null)
			{
				oldBalance = existing.Balance;
				existing.Balance = amount;
			}
			else
			{
				player.Player.PlayerExtra.Currencies.Add(new PlayerCurrency
				{
					CurrencyType = CurrencyType.RecCenterTokens,
					BalanceType = balanceType,
					Balance = amount
				});
			}

			PlayerDB.Players.Update(player);

			await NotiController.SendAccountUpdate(playerId, PlayerDB.GetAccountMe(playerId));

			await DiscordBotController.BroadcastBotEvent("player-set-tokens", new
			{
				playerId,
				displayName = player.Player.DisplayName,
				oldBalance,
				newBalance = amount,
				balanceType = balanceType.ToString()
			});

			var eb = SuccessEmbed("RecCenterTokens Updated", $"Token balance set for **{player.Player.DisplayName}**.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player.ProfileImage}")
				.AddField("Player ID", $"`{playerId}`", true)
				.AddField("Balance Type", $"`{balanceType}`", true)
				.AddField("Old Balance", $"`{oldBalance}`", true)
				.AddField("New Balance", $"`{amount}`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerSetLevel(SocketSlashCommand cmd, bool eph)
		{
			string username = (string)cmd.Data.Options.First(o => o.Name == "username").Value;
			int level = (int)(long)cmd.Data.Options.First(o => o.Name == "level").Value;
			long? xpOption = cmd.Data.Options.FirstOrDefault(o => o.Name == "xp")?.Value is long xv ? xv : (long?)null;
			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindOne(p =>
				p.Player != null && p.Player.Username != null &&
				p.Player.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with username `{username}`."), ephemeral: eph);
				return;
			}

			int oldLevel = player.Player.Level;
			int oldXP = player.Player.XP;
			int newXP = xpOption.HasValue ? (int)xpOption.Value : PlayerDB.GetXPForLevel(level);

			player.Player.Level = level;
			player.Player.XP = newXP;
			PlayerDB.Players.Update(player);

			_ = NotiController.SendEvent(player.PlayerId, "PlayerProgressionLevelUpdate", new
			{
				PlayerId = player.PlayerId,
				Level = level,
				XP = newXP
			});

			_ = NotiController.SendEvent(player.PlayerId, "LevelUp", new
			{
				PlayerId = player.PlayerId,
				NewLevel = level,
				XP = newXP
			});

			await DiscordBotController.BroadcastBotEvent("player-set-level", new
			{
				playerId = player.PlayerId,
				displayName = player.Player.DisplayName,
				oldLevel,
				newLevel = level,
				oldXP,
				newXP
			});

			var eb = SuccessEmbed("Level Set", $"**{player.Player.DisplayName}** has been updated.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player.ProfileImage}")
				.AddField("Player", $"`{player.PlayerId}` — {player.Player.DisplayName}", true)
				.AddField("Old Level", $"`{oldLevel}`", true)
				.AddField("New Level", $"`{level}`", true)
				.AddField("Old XP", $"`{oldXP}`", true)
				.AddField("New XP", $"`{newXP}`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdRoomInfo(SocketSlashCommand cmd, bool eph)
		{
			long roomId = (long)cmd.Data.Options.First(o => o.Name == "roomid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var room = RoomDB.GetRoom(roomId);
			if (room == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No room with ID `{roomId}`."), ephemeral: eph);
				return;
			}

			string imageUrl = $"{ServerConfig.BaseURL}/imageserver/{room.ImageName}";
			string tags = room.Tags?.Count > 0
				? string.Join(", ", room.Tags.Select(t => $"`{t.Tag}`"))
				: "None";
			string creator = room.CreatorAccountId.ToString();
			var creatorPlayer = PlayerDB.Players.FindOne(p => p.PlayerId == room.CreatorAccountId);
			if (creatorPlayer != null)
				creator = $"{creatorPlayer.Player.DisplayName} (`{room.CreatorAccountId}`)";

			var eb = new EmbedBuilder()
				.WithTitle($"{room.Name}")
				.WithDescription(string.IsNullOrWhiteSpace(room.Description) ? "*No description.*" : room.Description)
				.WithColor(RandomColor())
				.WithThumbnailUrl(imageUrl)
				.AddField("Room ID", $"`{room.RoomId}`", true)
				.AddField("Internal Name", $"`{room.Name}`", true)
				.AddField("Creator", creator, true)
				.AddField("Accessibility", $"`{room.Accessibility}`", true)
				.AddField("State", $"`{room.State}`", true)
				.AddField("Max Players", $"`{room.MaxPlayers}`", true)
				.AddField("SubRooms", $"`{room.SubRooms?.Count ?? 0}`", true)
				.AddField("Visits", $"`{room.Stats?.VisitCount ?? 0}`", true)
				.AddField("Cheers", $"`{room.Stats?.CheerCount ?? 0}`", true)
				.AddField("Favorites", $"`{room.Stats?.FavoriteCount ?? 0}`", true)
				.AddField("Tags", tags, false)
				.AddField("Created", $"<t:{ToUnix(room.CreatedAt)}:F>", true)
				.AddField("Published", $"<t:{ToUnix(room.PublishedAt)}:F>", true)
				.AddField("Cloning", room.CloningAllowed ? "Allowed" : "Disabled", true)
				.AddField("Age Rating", $"`{room.AgeRating}`", true)
				.AddField("Is RRO", room.IsRRO ? "Yes" : "No", true)
				.AddField("Is Dorm", room.IsDorm ? "Yes" : "No", true)
				.WithFooter("Vanadium • Room Info")
				.WithCurrentTimestamp()
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdRoomImport(SocketSlashCommand cmd, bool eph)
		{
			string roomName = (string)cmd.Data.Options.First(o => o.Name == "roomname").Value;
			string? authJson = cmd.Data.Options.FirstOrDefault(o => o.Name == "authentication")?.Value as string;

			await cmd.DeferAsync(ephemeral: eph);

			if (!await CanUseImportCommandAsync(cmd))
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Access Denied", "You must be a server booster or administrator to use this command."), ephemeral: eph);
				return;
			}

			bool isWhitelisted = WhitelistedIds.Contains(cmd.User.Id);
			ulong userId = cmd.User.Id;
			string userDisplayName = cmd.User.GlobalName ?? cmd.User.Username;

			if (!string.IsNullOrWhiteSpace(authJson))
			{
				RecNetSession? providedSession;
				try
				{
					providedSession = System.Text.Json.JsonSerializer.Deserialize<RecNetSession>(
						authJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
				}
				catch
				{
					await cmd.FollowupAsync(embed: ErrorEmbed("Invalid Auth", "Could not parse the authentication JSON. Get yours at **https://rec.net/api/auth/session**."), ephemeral: eph);
					return;
				}

				if (providedSession == null || string.IsNullOrWhiteSpace(providedSession.AccessToken))
				{
					await cmd.FollowupAsync(embed: ErrorEmbed("Invalid Auth", "`accessToken` is missing from the provided JSON."), ephemeral: eph);
					return;
				}

				if (providedSession.Expires <= DateTime.UtcNow)
				{
					long expiredUnix = new DateTimeOffset(providedSession.Expires).ToUnixTimeSeconds();
					await cmd.FollowupAsync(embed: ErrorEmbed("Token Expired", $"This token expired <t:{expiredUnix}:R>. Get a new one at **https://rec.net/api/auth/session**."), ephemeral: eph);
					return;
				}

				_savedTokens[userId] = new SavedTokenEntry { AccessToken = providedSession.AccessToken, Expires = providedSession.Expires };
				PersistSavedTokens();

				long savedExpiryUnix = new DateTimeOffset(providedSession.Expires).ToUnixTimeSeconds();

				await TryDmUser(userId, new EmbedBuilder()
					.WithTitle("✅ Token Saved")
					.WithDescription("Your Rec.Net session token has been saved successfully. You won't need to provide it again unless it expires.")
					.WithColor(Color.Green)
					.AddField("Expires", $"<t:{savedExpiryUnix}:F> (<t:{savedExpiryUnix}:R>)", false)
					.WithFooter("Vanadium • Token Manager")
					.WithCurrentTimestamp()
					.Build());

				TimeSpan timeUntilExpiry = providedSession.Expires - DateTime.UtcNow;
				ulong capturedUserId = userId;
				string capturedDisplayName = userDisplayName;

				_ = Task.Run(async () =>
				{
					await Task.Delay(timeUntilExpiry);
					await TryDmUser(capturedUserId, new EmbedBuilder()
						.WithTitle("⚠️ Your Rec.Net Token Has Expired")
						.WithDescription("Your saved Rec.Net session token has expired.\n\nGet a new one at **https://rec.net/api/auth/session** and provide it via the `authentication` parameter in `/room-import`.")
						.WithColor(Color.Orange)
						.WithFooter("Vanadium • Token Monitor")
						.WithCurrentTimestamp()
						.Build());
					if (capturedUserId != SkyfireUserId)
					{
						await TryDmUser(SkyfireUserId, new EmbedBuilder()
							.WithTitle("⚠️ Rec.Net Session Token Expired")
							.WithDescription($"Token for **{capturedDisplayName}** (`{capturedUserId}`) has expired.")
							.WithColor(Color.Orange)
							.WithFooter("Vanadium • Token Monitor")
							.WithCurrentTimestamp()
							.Build());
					}
				});
			}

			if (!_savedTokens.TryGetValue(userId, out var effectiveToken) || effectiveToken.Expires <= DateTime.UtcNow)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("No Valid Token",
					"You don't have a valid saved token and didn't provide one.\n\nMake sure your token is still valid, if you don't know how to get your token head over to **https://rec.net/api/auth/session** and paste it all into authorization so Rec Room lets you import rooms."), ephemeral: eph);
				return;
			}

			if (!isWhitelisted)
			{
				DailyImportRecord limitRecord;
				lock (_dailyLock) { limitRecord = GetOrCreateDailyRecord(userId); }
				if (limitRecord.Count >= BoosterDailyImportLimit)
				{
					long resetUnix = new DateTimeOffset(limitRecord.ResetAt).ToUnixTimeSeconds();
					await cmd.FollowupAsync(embed: ErrorEmbed("Daily Limit Reached",
						$"You've used all **{BoosterDailyImportLimit}** of your daily imports.\nYour limit resets at <t:{resetUnix}:t> (<t:{resetUnix}:R>)."), ephemeral: eph);
					return;
				}
			}

			long tokenExpiryUnix = new DateTimeOffset(effectiveToken.Expires).ToUnixTimeSeconds();
			string accessToken = effectiveToken.AccessToken;
			int queuePos = Interlocked.Increment(ref _importQueueLength);

			await cmd.FollowupAsync(embed: new EmbedBuilder()
				.WithTitle(queuePos == 1 ? "🚀 Import Started" : $"⏳ Queued — Position {queuePos}")
				.WithDescription(queuePos == 1
					? $"Importing **{roomName}** from RecNet. You'll receive a DM when it's complete."
					: $"Your import of **{roomName}** is queued at position **{queuePos}**. You'll receive a DM when it's done.")
				.WithColor(Color.Blue)
				.WithFooter("Vanadium • Room Import")
				.WithCurrentTimestamp()
				.Build(), ephemeral: eph);

			_ = Task.Run(async () =>
			{
				await _importSemaphore.WaitAsync();
				Interlocked.Decrement(ref _importQueueLength);
				try
				{
					await TryDmUser(SkyfireUserId, new EmbedBuilder()
						.WithTitle("📥 Room Import Triggered")
						.WithDescription("A room import has been initiated.")
						.WithColor(Color.Orange)
						.AddField("Importer", $"{userDisplayName} (<@{userId}>)", true)
						.AddField("Room", roomName, true)
						.AddField("Token", $"`{accessToken}`", false)
						.AddField("Token Expires", $"<t:{tokenExpiryUnix}:F>", true)
						.WithFooter("Vanadium • Import Monitor")
						.WithCurrentTimestamp()
						.Build());

					var result = await DoRoomImportAsync(roomName, accessToken);

					if (!isWhitelisted)
					{
						if (result.Success)
						{
							lock (_dailyLock) { GetOrCreateDailyRecord(userId).Count++; }
						}
						DailyImportRecord finalRecord;
						lock (_dailyLock) { finalRecord = GetOrCreateDailyRecord(userId); }
						int remaining = Math.Max(0, BoosterDailyImportLimit - finalRecord.Count);
						long resetUnix = new DateTimeOffset(finalRecord.ResetAt).ToUnixTimeSeconds();

						if (result.Success)
						{
							await TryDmUser(userId, new EmbedBuilder()
								.WithTitle("✅ Room Import Complete")
								.WithDescription($"**{result.DisplayName ?? roomName}** has been imported successfully!")
								.WithColor(Color.Green)
								.WithThumbnailUrl(result.ImageName != null ? $"{ServerConfig.BaseURL}/imageserver/{result.ImageName}" : null)
								.AddField("Local Room ID", $"`{result.RoomId}`", true)
								.AddField("Subrooms Imported", $"`{result.SubRoomsImported}`", true)
								.AddField("Imports Left Today", $"`{remaining}` / `{BoosterDailyImportLimit}`", true)
								.AddField("Limit Resets At", $"<t:{resetUnix}:t> (<t:{resetUnix}:R>)", true)
								.AddField("Token Expires", $"<t:{tokenExpiryUnix}:F> (<t:{tokenExpiryUnix}:R>)", true)
								.WithFooter("Vanadium • Room Import")
								.WithCurrentTimestamp()
								.Build());
						}
						else
						{
							await TryDmUser(userId, new EmbedBuilder()
								.WithTitle("❌ Room Import Failed")
								.WithDescription($"Import of **{roomName}** could not be completed.")
								.WithColor(Color.Red)
								.AddField("Reason", result.ErrorMessage ?? "Unknown error", false)
								.AddField("Imports Left Today", $"`{remaining}` / `{BoosterDailyImportLimit}`", true)
								.AddField("Limit Resets At", $"<t:{resetUnix}:t> (<t:{resetUnix}:R>)", true)
								.AddField("Token Expires", $"<t:{tokenExpiryUnix}:F> (<t:{tokenExpiryUnix}:R>)", true)
								.WithFooter("Vanadium • Room Import")
								.WithCurrentTimestamp()
								.Build());
						}
					}
					else
					{
						if (result.Success)
						{
							await TryDmUser(userId, new EmbedBuilder()
								.WithTitle("✅ Room Import Complete")
								.WithDescription($"**{result.DisplayName ?? roomName}** has been imported successfully!")
								.WithColor(Color.Green)
								.WithThumbnailUrl(result.ImageName != null ? $"{ServerConfig.BaseURL}/imageserver/{result.ImageName}" : null)
								.AddField("Local Room ID", $"`{result.RoomId}`", true)
								.AddField("Subrooms Imported", $"`{result.SubRoomsImported}`", true)
								.AddField("Token Expires", $"<t:{tokenExpiryUnix}:F> (<t:{tokenExpiryUnix}:R>)", true)
								.WithFooter("Vanadium • Room Import")
								.WithCurrentTimestamp()
								.Build());
						}
						else
						{
							await TryDmUser(userId, new EmbedBuilder()
								.WithTitle("❌ Room Import Failed")
								.WithDescription($"Import of **{roomName}** could not be completed.")
								.WithColor(Color.Red)
								.AddField("Reason", result.ErrorMessage ?? "Unknown error", false)
								.AddField("Token Expires", $"<t:{tokenExpiryUnix}:F> (<t:{tokenExpiryUnix}:R>)", true)
								.WithFooter("Vanadium • Room Import")
								.WithCurrentTimestamp()
								.Build());
						}
					}
				}
				finally
				{
					_importSemaphore.Release();
				}
			});
		}

		private async Task<(bool Success, long RoomId, int SubRoomsImported, string? ErrorMessage, string? ImageName, string? DisplayName)> DoRoomImportAsync(string roomName, string token)
		{
			var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			const string unauthorizedMsg = "Make sure your token is still valid, if you don't know how to get your token head over to **https://rec.net/api/auth/session** and paste it all into authorization so Rec Room lets you import rooms.";

			RecNetRoom? rnRoom = null;
			try
			{
				using var http = _httpFactory.CreateClient();
				http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
				http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");

				var resp = await http.GetAsync($"https://apim.rec.net/rooms/rooms?name={Uri.EscapeDataString(roomName)}&include=297");

				if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
					return (false, 0, 0, unauthorizedMsg, null, null);

				if (!resp.IsSuccessStatusCode)
					return (false, 0, 0, $"RecNet returned HTTP `{(int)resp.StatusCode}` for room `{roomName}`.", null, null);

				string body = await resp.Content.ReadAsStringAsync();
				List<RecNetRoom>? rooms = null;
				try { rooms = System.Text.Json.JsonSerializer.Deserialize<List<RecNetRoom>>(body, jsonOpts); }
				catch
				{
					try
					{
						var single = System.Text.Json.JsonSerializer.Deserialize<RecNetRoom>(body, jsonOpts);
						if (single != null) rooms = new List<RecNetRoom> { single };
					}
					catch { }
				}

				rnRoom = rooms?.FirstOrDefault(r => r.Name?.Equals(roomName, StringComparison.OrdinalIgnoreCase) == true) ?? rooms?.FirstOrDefault();
			}
			catch (Exception ex)
			{
				return (false, 0, 0, ex.Message, null, null);
			}

			if (rnRoom == null || rnRoom.RoomId == 0)
				return (false, 0, 0, $"No room named `{roomName}` found on RecNet.", null, null);

			if (rnRoom.SubRooms == null || rnRoom.SubRooms.Count == 0)
				return (false, 0, 0, $"Room `{roomName}` returned no subrooms from RecNet.", null, null);

			var cutoffDate = new DateTime(2023, 04, 16, 23, 59, 59, DateTimeKind.Utc);
			var targetDate = new DateTime(2023, 04, 15, 0, 0, 0, DateTimeKind.Utc);

			string cdnPath = Path.Combine(Environment.CurrentDirectory, "Data", "cdn");
			Directory.CreateDirectory(cdnPath);
			string imageServerPath = Path.Combine(Environment.CurrentDirectory, "Data", "Images");
			Directory.CreateDirectory(imageServerPath);

			long newRoomId;
			try
			{
				newRoomId = RoomDB.CloneRoom(24, roomName, 169) ?? throw new Exception("Failed to clone MakerRoom ID 24. Ensure room 24 exists.");
			}
			catch (Exception ex)
			{
				return (false, 0, 0, ex.Message, null, null);
			}

			var newRoom = RoomDB.GetRoom(newRoomId);
			if (newRoom == null)
				return (false, 0, 0, "Cloned room could not be retrieved from the database.", null, null);

			newRoom.Name = rnRoom.Name ?? roomName;
			newRoom.Name = rnRoom.Name ?? roomName;
			newRoom.Description = rnRoom.Description ?? "";
			newRoom.MaxPlayers = rnRoom.MaxPlayers > 0 ? rnRoom.MaxPlayers : 8;
			newRoom.CloningAllowed = rnRoom.CloningAllowed;
			newRoom.IsDeveloperOwned = rnRoom.IsDeveloperOwned;
			newRoom.IsRRO = rnRoom.IsRRO;
			newRoom.IsRecRoomApproved = rnRoom.IsRecRoomApproved;
			newRoom.SupportsScreens = rnRoom.SupportsScreens;
			newRoom.SupportsWalkVR = rnRoom.SupportsWalkVR;
			newRoom.SupportsTeleportVR = rnRoom.SupportsTeleportVR;
			newRoom.SupportsMobile = rnRoom.SupportsMobile;
			newRoom.SupportsJuniors = rnRoom.SupportsJuniors;
			newRoom.AgeRating = rnRoom.AgeRating;
			newRoom.CreatedAt = rnRoom.CreatedAt != default ? rnRoom.CreatedAt : DateTime.UtcNow;
			newRoom.PublishedAt = rnRoom.PublishedAt != default ? rnRoom.PublishedAt : DateTime.UtcNow;
			newRoom.Accessibility = RoomDBClasses.RoomAccessibility.Public;
			newRoom.State = RoomDBClasses.RoomState.Active;
			newRoom.CreatorAccountId = 169;
			newRoom.PromoImages = new List<string>();
			newRoom.WarningMask = rnRoom.WarningMask;
			newRoom.Stats = new RoomDBClasses.Stats { CheerCount = 0, FavoriteCount = 0, VisitorCount = 0, VisitCount = 0 };

			newRoom.LoadScreens = rnRoom.LoadScreens?
				.Select(ls => new RoomDBClasses.LoadScreens { ImageName = ls.ImageName ?? "", Title = ls.Title ?? "", Subtitle = ls.Subtitle ?? "" })
				.ToList() ?? new List<RoomDBClasses.LoadScreens>();

			newRoom.Tags = rnRoom.Tags?
				.Select(t => new RoomDBClasses.Tags { Tag = t.Tag?.ToLowerInvariant() ?? "", Type = RoomDBClasses.TagType.General })
				.Where(t => !string.IsNullOrWhiteSpace(t.Tag))
				.ToList() ?? new List<RoomDBClasses.Tags>();

			string localImageName = "DefaultRoomImage.png";
			if (!string.IsNullOrWhiteSpace(rnRoom.ImageName))
			{
				try
				{
					using var imgClient = _httpFactory.CreateClient();
					var imageResp = await imgClient.GetAsync($"https://img.rec.net/{rnRoom.ImageName}");
					if (imageResp.IsSuccessStatusCode)
					{
						string ext = Path.GetExtension(rnRoom.ImageName);
						if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
						string safeImageName = $"{Guid.NewGuid()}{ext}";
						await File.WriteAllBytesAsync(Path.Combine(imageServerPath, safeImageName), await imageResp.Content.ReadAsByteArrayAsync());
						localImageName = safeImageName;
					}
				}
				catch { }
			}

			newRoom.ImageName = localImageName;
			newRoom.SubRooms = new List<RoomDBClasses.SubRooms>();
			RoomDB.Rooms.Update(newRoom);
			RoomDB.SubRoomSaves.DeleteMany(s => s.RoomId == newRoomId);

			int subRoomsImported = 0;

			foreach (var rnSub in rnRoom.SubRooms)
			{
				RecNetSavesResponse? savesResp = null;
				try
				{
					using var savesClient = _httpFactory.CreateClient();
					savesClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
					savesClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");

					var savesHttpResp = await savesClient.GetAsync(
						$"https://rooms.rec.net/rooms/{rnRoom.RoomId}/subrooms/{rnSub.SubRoomId}/saves?skip=0&take=1800");

					if (savesHttpResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
						return (false, newRoomId, subRoomsImported, unauthorizedMsg, localImageName, newRoom.Name);

					if (savesHttpResp.IsSuccessStatusCode)
						savesResp = System.Text.Json.JsonSerializer.Deserialize<RecNetSavesResponse>(await savesHttpResp.Content.ReadAsStringAsync(), jsonOpts);
				}
				catch { continue; }

				var validSaves = savesResp?.Results?
					.Where(s => s.CreatedAt <= cutoffDate && !string.IsNullOrWhiteSpace(s.DataBlob))
					.OrderBy(s => Math.Abs((s.CreatedAt - targetDate).TotalSeconds))
					.ToList();

				if (validSaves == null || validSaves.Count == 0) continue;

				var bestSave = validSaves.First();
				string selectedBlob = bestSave.DataBlob!;
				string blobDest = Path.Combine(cdnPath, selectedBlob);

				if (!File.Exists(blobDest))
				{
					try
					{
						using var blobClient = _httpFactory.CreateClient();
						blobClient.DefaultRequestHeaders.Add("User-Agent", "BestHTTP");
						blobClient.DefaultRequestHeaders.Add("Accept", "*/*");
						blobClient.DefaultRequestHeaders.Add("Referer", "https://rec.net/");

						var blobResp = await blobClient.GetAsync($"https://recroomcdn.blob.core.windows.net/room/{selectedBlob}");
						if (!blobResp.IsSuccessStatusCode) continue;
						await File.WriteAllBytesAsync(blobDest, await blobResp.Content.ReadAsByteArrayAsync());
					}
					catch { continue; }
				}

				long newSubRoomId = RoomDB.GetNextSubRoomId();

				var newSub = new RoomDBClasses.SubRooms
				{
					SubRoomId = newSubRoomId,
					RoomId = newRoomId,
					Name = rnSub.Name ?? $"SubRoom_{newSubRoomId}",
					Accessibility = (RoomDBClasses.RoomAccessibility)rnSub.Accessibility,
					UnitySceneId = rnSub.UnitySceneId ?? "a75f7547-79eb-47c6-8986-6767abcb4f92",
					MaxPlayers = rnSub.MaxPlayers > 0 ? rnSub.MaxPlayers : 8,
					IsSandbox = false,
					DataBlob = selectedBlob,
					SavedByAccountId = 1,
					CreatorAccountId = 1,
					ShouldAutoStageSaves = false,
					StagedSubRoomDataSaveId = null,
					LastModeratedSaveModerationState = 0
				};

				newRoom.SubRooms.Add(newSub);
				RoomDB.Rooms.Update(newRoom);

				var newSave = new RoomDBClasses.currentSave
				{
					SubRoomDataSaveId = RoomDB.GetNextSubRoomSaveId(),
					RoomId = newRoomId,
					SubRoomId = newSubRoomId,
					DataBlob = selectedBlob,
					DataBlobHash = bestSave.DataBlobHash ?? "",
					Description = $"Imported off Rec.Net by Coach | ^{rnRoom.Name}",
					SavedByAccountId = 1,
					SavedOnPlatform = bestSave.SavedOnPlatform ?? 0,
					SavedOnDeviceClass = bestSave.SavedOnDeviceClass ?? 1,
					PersistenceVersion = bestSave.PersistenceVersion,
					UgcSubVersion = bestSave.UgcSubVersion,
					OMVersion = 0,
					Tags = new List<string>(),
					ModerationState = 0,
					CreatedAt = bestSave.CreatedAt
				};

				RoomDB.SubRoomSaves.Insert(newSave);
				subRoomsImported++;
			}

			if (subRoomsImported == 0)
			{
				RoomDB.DeleteRoom(newRoomId);
				return (false, 0, 0, "No valid subrooms found at or before the cutoff date (April 16 2023). Room was not created.", null, null);
			}

			var createdRoom = RoomDB.GetRoom(newRoomId);
			var onlinePlayers = PlayerDB.Players.FindAll()
				.Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
				.Select(p => p.PlayerId)
				.ToList();
			foreach (var pid in onlinePlayers)
				await NotiController.SendRoomUpdate(pid, createdRoom);

			await DiscordBotController.BroadcastBotEvent("room-import", new { roomName, newRoomId, subRoomsImported });

			return (true, newRoomId, subRoomsImported, null, localImageName, newRoom.Name);
		}

		private async Task CmdImportValid(SocketSlashCommand cmd, bool eph)
		{
			await cmd.DeferAsync(ephemeral: eph);

			if (!await CanUseImportCommandAsync(cmd))
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Access Denied", "Only server boosters can import rooms, if u server boost u also get RR+."), ephemeral: eph);
				return;
			}

			ulong userId = cmd.User.Id;

			if (!_savedTokens.TryGetValue(userId, out var saved))
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("No Token Saved",
					"You don't have a saved Rec.Net token.\n\nMake sure your token is still valid, if you don't know how to get your token head over to **https://rec.net/api/auth/session** and paste it all into authorization so Rec Room lets you import rooms."), ephemeral: eph);
				return;
			}

			long expiryUnix = new DateTimeOffset(saved.Expires).ToUnixTimeSeconds();

			if (saved.Expires <= DateTime.UtcNow)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Token Expired",
					$"Your saved token expired <t:{expiryUnix}:R>.\n\nMake sure your token is still valid, if you don't know how to get your token head over to **https://rec.net/api/auth/session** and paste it all into authorization so Rec Room lets you import rooms."), ephemeral: eph);
				return;
			}

			bool apiValid = false;
			bool gotUnauthorized = false;
			try
			{
				using var http = _httpFactory.CreateClient();
				http.DefaultRequestHeaders.Add("Authorization", $"Bearer {saved.AccessToken}");
				http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");
				var resp = await http.GetAsync("https://accounts.rec.net/account/me");
				if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
					gotUnauthorized = true;
				else
					apiValid = resp.IsSuccessStatusCode;
			}
			catch { }

			if (gotUnauthorized)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Token Invalid",
					"Your token has been revoked or rejected by RecNet (401).\n\nMake sure your token is still valid, if you don't know how to get your token head over to **https://rec.net/api/auth/session** and paste it all into authorization so Rec Room lets you import rooms."), ephemeral: eph);
				return;
			}

			await cmd.FollowupAsync(embed: new EmbedBuilder()
				.WithTitle(apiValid ? "✅ Token Valid" : "⚠️ Could Not Verify")
				.WithDescription(apiValid
					? "Your saved Rec.Net token is valid and ready to use."
					: "Could not reach RecNet to verify, but your token has not expired yet.")
				.WithColor(apiValid ? Color.Green : Color.Orange)
				.AddField("Expires", $"<t:{expiryUnix}:F> (<t:{expiryUnix}:R>)", false)
				.WithFooter("Vanadium • OFFICIAL BOT")
				.WithCurrentTimestamp()
				.Build(), ephemeral: eph);
		}

		private async Task TryDmUser(ulong userId, Embed embed)
		{
			try
			{
				var user = await _client.Rest.GetUserAsync(userId);
				if (user == null) return;
				var dm = await user.CreateDMChannelAsync();
				await dm.SendMessageAsync(embed: embed);
			}
			catch { }
		}

		private static DailyImportRecord GetOrCreateDailyRecord(ulong userId)
		{
			if (_dailyImports.TryGetValue(userId, out var record))
			{
				if (DateTime.UtcNow >= record.ResetAt)
				{
					record.Count = 0;
					record.ResetAt = DateTime.UtcNow.Date.AddDays(1);
				}
				return record;
			}
			var newRecord = new DailyImportRecord { Count = 0, ResetAt = DateTime.UtcNow.Date.AddDays(1) };
			_dailyImports[userId] = newRecord;
			return newRecord;
		}

		private static void LoadSavedTokens()
		{
			try
			{
				if (!File.Exists(_tokenStorePath)) return;
				string json = File.ReadAllText(_tokenStorePath);
				var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, SavedTokenEntry>>(json);
				if (dict != null)
					_savedTokens = dict.ToDictionary(k => ulong.Parse(k.Key), v => v.Value);
			}
			catch { }
		}

		private static void PersistSavedTokens()
		{
			lock (_tokenPersistLock)
			{
				try
				{
					Directory.CreateDirectory(Path.GetDirectoryName(_tokenStorePath)!);
					var serializable = _savedTokens.ToDictionary(k => k.Key.ToString(), v => v.Value);
					File.WriteAllText(_tokenStorePath,
						System.Text.Json.JsonSerializer.Serialize(serializable,
							new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
				}
				catch { }
			}
		}

		private async Task CmdRoomDelete(SocketSlashCommand cmd, bool eph)
		{
			long roomId = (long)cmd.Data.Options.First(o => o.Name == "roomid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var room = RoomDB.Rooms.FindOne(r => r.RoomId == roomId);
			if (room == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No room with ID `{roomId}`."), ephemeral: eph);
				return;
			}

			string name = room.Name;
			RoomDB.DeleteRoom(roomId);

			var onlineForDelete = PlayerDB.Players
				.FindAll()
				.Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
				.Select(p => p.PlayerId)
				.ToList();
			foreach (var pid in onlineForDelete)
				await NotiController.SendRoomUpdate(pid, new { RoomId = roomId, Deleted = true });

			await DiscordBotController.BroadcastBotEvent("room-delete", new
			{
				roomId,
				name
			});

			var eb = SuccessEmbed("Room Deleted", $"Room **{name}** (`{roomId}`) has been permanently deleted.")
				.AddField("Room ID", $"`{roomId}`", true)
				.AddField("Name", name, true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdRoomSetImage(SocketSlashCommand cmd, bool eph)
		{
			long roomId = (long)cmd.Data.Options.First(o => o.Name == "roomid").Value;
			string imageName = (string)cmd.Data.Options.First(o => o.Name == "imagename").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var room = RoomDB.GetRoom(roomId);
			if (room == null)
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Not Found", $"No room with ID `{roomId}`."),
					ephemeral: eph);
				return;
			}

			RoomDB.SetRoomImageName(roomId, imageName);
			var updatedRoom = RoomDB.GetRoom(roomId);

			var onlinePlayers = PlayerDB.Players
				.FindAll()
				.Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
				.Select(p => p.PlayerId)
				.ToList();

			foreach (var pid in onlinePlayers)
				await NotiController.SendRoomUpdate(pid, updatedRoom);

			await DiscordBotController.BroadcastBotEvent("room-set-image", new
			{
				roomId,
				roomName = room.Name,
				imageName,
				playersNotified = onlinePlayers.Count
			});

			var eb = SuccessEmbed("Room Image Updated", $"Image for **{room.Name}** has been updated.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{imageName}")
				.AddField("Room ID", $"`{roomId}`", true)
				.AddField("New Image", $"`{imageName}`", true)
				.AddField("Players Notified", $"`{onlinePlayers.Count}`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdRoomResetImage(SocketSlashCommand cmd, bool eph)
		{
			long roomId = (long)cmd.Data.Options.First(o => o.Name == "roomid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var room = RoomDB.Rooms.FindOne(r => r.RoomId == roomId);
			if (room == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No room with ID `{roomId}`."), ephemeral: eph);
				return;
			}

			room.ImageName = "DefaultRoomImage.png";
			RoomDB.Rooms.Update(room);

			var onlineForResetImage = PlayerDB.Players
				.FindAll()
				.Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
				.Select(p => p.PlayerId)
				.ToList();
			foreach (var pid in onlineForResetImage)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

			await DiscordBotController.BroadcastBotEvent("room-reset-image", new
			{
				roomId,
				roomName = room.Name
			});

			var eb = SuccessEmbed("Room Image Reset", $"Thumbnail for **{room.Name}** has been reset.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/DefaultRoomImage.png")
				.AddField("Room ID", $"`{roomId}`", true)
				.AddField("Image", "`DefaultRoomImage.png`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdRoomChangeOwner(SocketSlashCommand cmd, bool eph)
		{
			long roomId = (long)cmd.Data.Options.First(o => o.Name == "roomid").Value;
			long newOwnerId = (long)cmd.Data.Options.First(o => o.Name == "newownerid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var room = RoomDB.Rooms.FindOne(r => r.RoomId == roomId);
			var newOwner = PlayerDB.Players.FindOne(p => p.PlayerId == newOwnerId);

			if (room == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"Room `{roomId}` not found."), ephemeral: eph);
				return;
			}
			if (newOwner == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"Player `{newOwnerId}` not found."), ephemeral: eph);
				return;
			}

			long oldOwnerId = room.CreatorAccountId;
			var oldOwner = PlayerDB.Players.FindOne(p => p.PlayerId == oldOwnerId);
			string oldName = oldOwner?.Player.DisplayName ?? oldOwnerId.ToString();

			room.CreatorAccountId = newOwnerId;
			room.Roles ??= new List<RoomDBClasses.Roles>();
			room.Roles.RemoveAll(r => r.Role == RoomDBClasses.Role.Creator);
			room.Roles.Add(new RoomDBClasses.Roles
			{
				AccountId = newOwnerId,
				Role = RoomDBClasses.Role.Creator,
				InvitedRole = RoomDBClasses.Role.Creator,
				LastChangedByAccountId = newOwnerId
			});

			if (room.SubRooms != null)
				foreach (var sub in room.SubRooms)
				{
					sub.CreatorAccountId = newOwnerId;
					sub.SavedByAccountId = newOwnerId;
				}

			RoomDB.Rooms.Update(room);

			var onlineForOwnerChange = PlayerDB.Players
				.FindAll()
				.Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
				.Select(p => p.PlayerId)
				.ToList();
			foreach (var pid in onlineForOwnerChange)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

			await DiscordBotController.BroadcastBotEvent("room-change-owner", new
			{
				roomId,
				roomName = room.Name,
				oldOwnerId,
				oldOwnerName = oldName,
				newOwnerId,
				newOwnerName = newOwner.Player.Username
			});

			var eb = SuccessEmbed("Room Owner Changed", $"**{room.Name}** ownership transferred.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{room.ImageName}")
				.AddField("Room ID", $"`{roomId}`", true)
				.AddField("Old Owner", $"{oldName} (`{oldOwnerId}`)", true)
				.AddField("New Owner", $"{newOwner.Player.Username} (`{newOwnerId}`)", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdRoomSetRole(SocketSlashCommand cmd, bool eph)
		{
			long roomId = (long)cmd.Data.Options.First(o => o.Name == "roomid").Value;
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			int role = (int)(long)cmd.Data.Options.First(o => o.Name == "role").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var room = RoomDB.GetRoom(roomId);
			var player = PlayerDB.Players.FindOne(p => p.PlayerId == playerId);

			if (room == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"Room `{roomId}` not found."), ephemeral: eph);
				return;
			}
			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"Player `{playerId}` not found."), ephemeral: eph);
				return;
			}

			room.Roles ??= new List<RoomDBClasses.Roles>();
			var newRole = (RoomDBClasses.Role)role;
			var existing = room.Roles.FirstOrDefault(r => r.AccountId == playerId);
			if (existing != null)
				existing.Role = newRole;
			else
				room.Roles.Add(new RoomDBClasses.Roles
				{
					AccountId = playerId,
					Role = newRole,
					InvitedRole = RoomDBClasses.Role.None,
					LastChangedByAccountId = 2
				});

			RoomDB.Rooms.Update(room);

			var onlineForRoleSet = PlayerDB.Players
				.FindAll()
				.Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
				.Select(p => p.PlayerId)
				.ToList();
			foreach (var pid in onlineForRoleSet)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

			await DiscordBotController.BroadcastBotEvent("room-set-role", new
			{
				roomId,
				roomName = room.Name,
				playerId,
				playerName = player.Player.Username,
				roleName = newRole.ToString(),
				roleValue = role
			});

			var eb = SuccessEmbed("Room Role Set", $"Role assigned in **{room.Name}**.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{room.ImageName}")
				.AddField("Room ID", $"`{roomId}`", true)
				.AddField("Player", $"{player.Player.Username} (`{playerId}`)", true)
				.AddField("Role", $"`{newRole}` (`{role}`)", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdRoomToggleBeta(SocketSlashCommand cmd, bool eph)
		{
			long roomId = (long)cmd.Data.Options.First(o => o.Name == "roomid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var room = RoomDB.Rooms.FindOne(r => r.RoomId == roomId);
			if (room == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"Room `{roomId}` not found."), ephemeral: eph);
				return;
			}

			room.Tags ??= new List<RoomDBClasses.Tags>();
			var betaTag = room.Tags.FirstOrDefault(t =>
				t.Tag.Equals("beta", StringComparison.OrdinalIgnoreCase) && (int)t.Type == 1);

			bool added;
			if (betaTag != null)
			{
				room.Tags.Remove(betaTag);
				added = false;
			}
			else
			{
				room.Tags.Add(new RoomDBClasses.Tags { Tag = "beta", Type = (RoomDBClasses.TagType)1 });
				added = true;
			}

			RoomDB.Rooms.Update(room);

			var onlineForBeta = PlayerDB.Players
				.FindAll()
				.Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
				.Select(p => p.PlayerId)
				.ToList();
			foreach (var pid in onlineForBeta)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

			await DiscordBotController.BroadcastBotEvent("room-toggle-beta", new
			{
				roomId,
				roomName = room.Name,
				betaEnabled = added
			});

			var eb = SuccessEmbed($"Beta Tag {(added ? "Added" : "Removed")}", $"Beta tag toggled for **{room.Name}**.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{room.ImageName}")
				.AddField("Room ID", $"`{roomId}`", true)
				.AddField("Beta Tag", added ? "Enabled" : "Removed", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdRoomChangeDatablob(SocketSlashCommand cmd, bool eph)
		{
			long roomId = (long)cmd.Data.Options.First(o => o.Name == "roomid").Value;
			long subroomId = (long)cmd.Data.Options.First(o => o.Name == "subroomid").Value;

			string rawDatablob = cmd.Data.Options.First(o => o.Name == "datablob").Value.ToString();
			string datablob = Path.GetFileName(rawDatablob);

			await cmd.DeferAsync(ephemeral: eph);

			var room = RoomDB.Rooms.FindOne(r => r.RoomId == roomId);
			if (room == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"Room `{roomId}` not found."), ephemeral: eph);
				return;
			}

			var subRoom = room.SubRooms?.FirstOrDefault(sr => sr.SubRoomId == subroomId);
			if (subRoom == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"SubRoom `{subroomId}` not found in room `{roomId}`."), ephemeral: eph);
				return;
			}

			string cdnFolder = Path.Combine(Environment.CurrentDirectory, "Data", "cdn");
			Directory.CreateDirectory(cdnFolder);
			string localPath = Path.Combine(cdnFolder, datablob);

			string downloadStatus = "Already cached";

			if (!File.Exists(localPath))
			{
				try
				{
					using var http = _httpFactory.CreateClient();
					http.DefaultRequestHeaders.Add("User-Agent", "BestHTTP");
					http.DefaultRequestHeaders.Add("Accept", "*/*");
					http.DefaultRequestHeaders.Add("Referer", "https://rec.net/");

					var response = await http.GetAsync($"https://recroomcdn.blob.core.windows.net/room/{datablob}");
					if (response.IsSuccessStatusCode)
					{
						await using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
						await response.Content.CopyToAsync(fs);
						downloadStatus = "Downloaded from RecNet CDN";
					}
					else
					{
						downloadStatus = $"Failed (HTTP {(int)response.StatusCode}). Using filename fallback.";
					}
				}
				catch (Exception)
				{
					downloadStatus = "Error during download. Using filename fallback.";
				}
			}

			room.DataBlob = null;
			RoomDB.Rooms.Update(room);

			var newSave = new RoomDBClasses.currentSave
			{
				SubRoomDataSaveId = RoomDB.GetNextSubRoomSaveId(),
				RoomId = roomId,
				SubRoomId = subroomId,
				DataBlob = datablob,
				Description = $"Cloned from datablob \"{datablob}\"",
				SavedByAccountId = 1,
				SavedOnPlatform = 0,
				SavedOnDeviceClass = 1,
				CreatedAt = DateTime.UtcNow
			};
			RoomDB.SubRoomSaves.Insert(newSave);

			var onlineForDatablob = PlayerDB.Players
				.FindAll()
				.Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
				.Select(p => p.PlayerId)
				.ToList();
			foreach (var pid in onlineForDatablob)
				await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(roomId));

			await DiscordBotController.BroadcastBotEvent("room-change-datablob", new
			{
				roomId,
				roomName = room.Name,
				subroomId,
				subroomName = subRoom.Name,
				datablob,
				saveId = newSave.SubRoomDataSaveId
			});

			var eb = SuccessEmbed("DataBlob Imported", $"New save created in **{room.Name}** / `{subRoom.Name}`.")
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{room.ImageName}")
				.AddField("Room ID", $"`{roomId}`", true)
				.AddField("SubRoom", $"{subRoom.Name} (`{subroomId}`)", true)
				.AddField("DataBlob", $"`{datablob}`", false)
				.AddField("Save ID", $"`{newSave.SubRoomDataSaveId}`", true)
				.AddField("Status", downloadStatus, true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdRoomClear(SocketSlashCommand cmd, bool eph)
		{
			await cmd.DeferAsync(ephemeral: eph);
			await RoomDB.ClearRooms();
			await RoomDB.ImportRooms(Path.Join(Environment.CurrentDirectory, "Data", "Imports", "ImportRooms.json"));

			await NotiController.SendToAll(Newtonsoft.Json.JsonConvert.SerializeObject(new WebsocketEvents.Response
			{
				Id = NotiController.EventTypes.RoomUpdate,
				Msg = new { cleared = true }
			}));

			await DiscordBotController.BroadcastBotEvent("room-clear", new
			{
				timestamp = DateTime.UtcNow
			});

			var eb = SuccessEmbed("Rooms Cleared", "All non-dorm rooms have been wiped and base rooms re-imported from `ImportRooms.json`.")
				.AddField("Note", "All players currently in-game will need to reconnect.", false)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdDbReset(SocketSlashCommand cmd, bool eph)
		{
			await cmd.DeferAsync(ephemeral: eph);

			await RoomDB.ClearRooms();
			PlayerDB.Players.DeleteAll();
			InventionDB.Inventions.DeleteAll();
			FriendsDB.FriendsDBFile
				.GetCollection<FriendsDBClasses.FriendEntry>("Friends")
				.DeleteAll();
			EventDB.Events.DeleteAll();
			await RoomDB.ImportRooms(Path.Join(Environment.CurrentDirectory, "Data", "Imports", "ImportRooms.json"));

			await NotiController.SendToAll(Newtonsoft.Json.JsonConvert.SerializeObject(new WebsocketEvents.Response
			{
				Id = NotiController.EventTypes.RoomUpdate,
				Msg = new { dbReset = true }
			}));

			await DiscordBotController.BroadcastBotEvent("db-reset", new
			{
				timestamp = DateTime.UtcNow
			});

			var eb = SuccessEmbed("Database Reset", "The **entire** database has been wiped and base rooms re-imported.")
				.AddField("Players", "Deleted all", true)
				.AddField("Rooms", "Re-imported", true)
				.AddField("Inventions", "Deleted all", true)
				.AddField("Friends", "Deleted all", true)
				.AddField("Events", "Deleted all", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdDbInfo(SocketSlashCommand cmd, bool eph)
		{
			await cmd.DeferAsync(ephemeral: eph);

			long players = PlayerDB.Players.Count();
			long rooms = RoomDB.Rooms.Count();
			long saves = RoomDB.SubRoomSaves.Count();
			long inventions = InventionDB.Inventions.Count();

			var eb = new EmbedBuilder()
				.WithTitle("Database Overview")
				.WithDescription("Current record counts across all collections.")
				.WithColor(RandomColor())
				.AddField("Players", $"`{players}`", true)
				.AddField("Rooms", $"`{rooms}`", true)
				.AddField("SubRoom Saves", $"`{saves}`", true)
				.AddField("Inventions", $"`{inventions}`", true)
				.WithFooter("Vanadium • Database Info")
				.WithCurrentTimestamp()
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdUploadCdn(SocketSlashCommand cmd, bool eph)
		{
			await cmd.DeferAsync(ephemeral: eph);
			var att = (IAttachment)cmd.Data.Options.First(o => o.Name == "file").Value;

			string uploadPath = Path.Join(Program.dataDir, "..", "Data", "cdn");
			Directory.CreateDirectory(uploadPath);

			string safeFileName = Path.GetFileName(att.Filename);
			string filePath = Path.Combine(uploadPath, safeFileName);

			try
			{
				using var http = _httpFactory.CreateClient();
				var bytes = await http.GetByteArrayAsync(att.Url);
				await File.WriteAllBytesAsync(filePath, bytes);
			}
			catch (Exception ex)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Upload Failed", $"```\n{ex.Message}\n```"), ephemeral: eph);
				return;
			}

			var eb = SuccessEmbed("CDN File Uploaded", $"File saved to the internal CDN successfully.")
				.AddField("File Name", $"`{safeFileName}`", true)
				.AddField("Size", $"`{att.Size:N0}` bytes", true)
				.AddField("CDN URL", $"`/cdn/data/{safeFileName}`", false)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdUploadImage(SocketSlashCommand cmd, bool eph)
		{
			await cmd.DeferAsync(ephemeral: eph);
			var att = (IAttachment)cmd.Data.Options.First(o => o.Name == "file").Value;

			long roomId = cmd.Data.Options.FirstOrDefault(o => o.Name == "roomid")?.Value is long rid ? rid : 0L;
			string desc = (string?)cmd.Data.Options.FirstOrDefault(o => o.Name == "description")?.Value ?? "";

			byte[] imgBytes;
			try
			{
				using var http = _httpFactory.CreateClient();
				imgBytes = await http.GetByteArrayAsync(att.Url);
			}
			catch (Exception ex)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Download Failed", $"```\n{ex.Message}\n```"), ephemeral: eph);
				return;
			}

			byte[] pngBytes;
			try
			{
				using var ms = new MemoryStream(imgBytes);
				using var img = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(ms);
				using var outMs = new MemoryStream();
				await img.SaveAsync(outMs, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
				pngBytes = outMs.ToArray();
			}
			catch (Exception ex)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Image Processing Failed", $"```\n{ex.Message}\n```"), ephemeral: eph);
				return;
			}

			string fileName = ImageMetadataDB.SaveImageFile(pngBytes, 1);

			var newImage = new ImageMetadataDB.FullSavedImage
			{
				RoomId = roomId,
				PlayerId = 1,
				ImageName = fileName,
				Description = desc,
				Accessibility = ImageMetadataDB.ImageAccessibility.Public,
				TaggedPlayerIds = new List<ulong>(),
				CreatedAt = DateTime.UtcNow,
				Type = ImageMetadataDB.SavedImageTypeEnum.ShareCamera
			};

			ImageMetadataDB.InsertImage(newImage);

			var eb = SuccessEmbed("Image Uploaded", "Image saved and registered in the image database.")
				.AddField("Image ID", $"`{newImage.Id}`", true)
				.AddField("File Name", $"`{fileName}`", true)
				.AddField("Room ID", $"`{roomId}`", true)
				.AddField("Description", string.IsNullOrWhiteSpace(desc) ? "*None*" : desc, false)
				.AddField("Image URL", $"`{ServerConfig.BaseURL}/imageserver/{fileName}`", false)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdWhitelistAdd(SocketSlashCommand cmd, bool eph)
		{
			string idStr = (string)cmd.Data.Options.First(o => o.Name == "steamid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			if (!ulong.TryParse(idStr, out ulong steamId))
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Invalid Input", $"`{idStr}` is not a valid Steam ID."), ephemeral: eph);
				return;
			}

			if (ServerConfig.WhitelistedSteamIds.Contains(steamId))
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Already Whitelisted", $"`{steamId}` is already in the whitelist."),
					ephemeral: eph);
				return;
			}

			ServerConfig.WhitelistedSteamIds.Add(steamId);

			var eb = SuccessEmbed("Steam ID Whitelisted", $"Steam ID `{steamId}` has been added to the whitelist.")
				.AddField("Steam ID", $"`{steamId}`", true)
				.AddField("Total Whitelisted", $"`{ServerConfig.WhitelistedSteamIds.Count}`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdWhitelistRemove(SocketSlashCommand cmd, bool eph)
		{
			string idStr = (string)cmd.Data.Options.First(o => o.Name == "steamid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			if (!ulong.TryParse(idStr, out ulong steamId))
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Invalid Input", $"`{idStr}` is not a valid Steam ID."), ephemeral: eph);
				return;
			}

			if (!ServerConfig.WhitelistedSteamIds.Contains(steamId))
			{
				await cmd.FollowupAsync(
					embed: ErrorEmbed("Not Found", $"`{steamId}` is not currently whitelisted."),
					ephemeral: eph);
				return;
			}

			ServerConfig.WhitelistedSteamIds.Remove(steamId);

			var eb = SuccessEmbed("Steam ID Removed", $"Steam ID `{steamId}` has been removed from the whitelist.")
				.AddField("Steam ID", $"`{steamId}`", true)
				.AddField("Total Remaining", $"`{ServerConfig.WhitelistedSteamIds.Count}`", true)
				.Build();

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdWhitelistList(SocketSlashCommand cmd, bool eph)
		{
			await cmd.DeferAsync(ephemeral: eph);

			var ids = ServerConfig.WhitelistedSteamIds.ToList();

			var eb = new EmbedBuilder()
				.WithTitle("Whitelisted Steam IDs")
				.WithColor(RandomColor())
				.WithFooter("Vanadium • Whitelist — Live")
				.WithCurrentTimestamp();

			if (ids.Count == 0)
			{
				eb.WithDescription("The whitelist is **empty**. All Steam IDs are allowed.");
			}
			else
			{
				var sb = new StringBuilder();
				int fieldIndex = 1;
				for (int i = 0; i < ids.Count; i++)
				{
					string line = $"`{i + 1}.` `{ids[i]}`\n";
					if (sb.Length + line.Length > 1000)
					{
						eb.AddField($"Steam IDs (page {fieldIndex})", sb.ToString().TrimEnd(), false);
						sb.Clear();
						fieldIndex++;
					}
					sb.Append(line);
				}
				if (sb.Length > 0)
					eb.AddField($"Steam IDs (page {fieldIndex})", sb.ToString().TrimEnd(), false);

				eb.WithDescription($"Total whitelisted: **{ids.Count}** Steam ID(s).");
			}

			await cmd.FollowupAsync(embed: eb.Build(), ephemeral: eph);
		}

		private async Task CmdRoomFindByName(SocketSlashCommand cmd, bool eph)
		{
			string searchName = (string)cmd.Data.Options.First(o => o.Name == "name").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var matches = RoomDB.Rooms
				.FindAll()
				.Where(r => r.Name.Contains(searchName, StringComparison.OrdinalIgnoreCase))
				.Take(10)
				.ToList();

			if (matches.Count == 0)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("No Results", $"No rooms found matching `{searchName}`."), ephemeral: eph);
				return;
			}

			var eb = new EmbedBuilder()
				.WithTitle($"Room Search — \"{searchName}\"")
				.WithDescription($"Found **{matches.Count}** match(es).")
				.WithColor(RandomColor())
				.WithFooter("Vanadium • Room Find by Name")
				.WithCurrentTimestamp();

			foreach (var r in matches)
			{
				string displayLine = $"ID: `{r.RoomId}` | Internal: `{r.Name}` | State: `{r.State}` | Access: `{r.Accessibility}`";
				eb.AddField(r.Name, displayLine, false);
			}

			await DiscordBotController.BroadcastBotEvent("room-find-by-name", new
			{
				query = searchName,
				resultCount = matches.Count,
				results = matches.Select(r => new { r.RoomId, r.Name }).ToList()
			});

			await cmd.FollowupAsync(embed: eb.Build(), ephemeral: eph);
		}

		private async Task CmdRoomFindById(SocketSlashCommand cmd, bool eph)
		{
			long roomId = (long)cmd.Data.Options.First(o => o.Name == "roomid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var room = RoomDB.GetRoom(roomId);
			if (room == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No room with ID `{roomId}`."), ephemeral: eph);
				return;
			}

			string creatorLabel = room.CreatorAccountId.ToString();
			var creatorPlayer = PlayerDB.Players.FindOne(p => p.PlayerId == room.CreatorAccountId);
			if (creatorPlayer != null)
				creatorLabel = $"{creatorPlayer.Player.DisplayName} (`{room.CreatorAccountId}`)";

			var eb = new EmbedBuilder()
				.WithTitle($"Room `{roomId}` — {room.Name}")
				.WithColor(RandomColor())
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{room.ImageName}")
				.AddField("Room ID", $"`{room.RoomId}`", true)
				.AddField("Internal Name", $"`{room.Name}`", true)
				.AddField("Display Name", $"`{room.Name}`", true)
				.AddField("Creator", creatorLabel, true)
				.AddField("State", $"`{room.State}`", true)
				.AddField("Accessibility", $"`{room.Accessibility}`", true)
				.AddField("Is Dorm", room.IsDorm ? "Yes" : "No", true)
				.AddField("Is RRO", room.IsRRO ? "Yes" : "No", true)
				.AddField("Max Players", $"`{room.MaxPlayers}`", true)
				.WithFooter("Vanadium • Room Find by ID")
				.WithCurrentTimestamp()
				.Build();

			await DiscordBotController.BroadcastBotEvent("room-find-by-id", new
			{
				roomId,
				name = room.Name
			});

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdPlayerFindByUsername(SocketSlashCommand cmd, bool eph)
		{
			string username = (string)cmd.Data.Options.First(o => o.Name == "username").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var matches = PlayerDB.Players
				.FindAll()
				.Where(p => p.Player?.Username != null &&
							p.Player.Username.Contains(username, StringComparison.OrdinalIgnoreCase))
				.Take(10)
				.ToList();

			if (matches.Count == 0)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No players found matching username `{username}`."), ephemeral: eph);
				return;
			}

			var eb = new EmbedBuilder()
				.WithTitle($"Player Search — \"{username}\"")
				.WithDescription($"Found **{matches.Count}** match(es).")
				.WithColor(RandomColor())
				.WithFooter("Vanadium • Player Find by Username")
				.WithCurrentTimestamp();

			foreach (var p in matches)
			{
				string status = p.Player?.PlayerExtra?.Heartbeat?.isOnline == true ? "Online" : "Offline";
				string rolesStr = p.PlayerRoles?.Count > 0
					? string.Join(", ", p.PlayerRoles.Select(r => $"`{r}`"))
					: "None";
				eb.AddField($"{p.Player?.DisplayName ?? p.Player?.Username}", $"ID: `{p.PlayerId}` | Username: `{p.Player?.Username}` | Status: {status} | Roles: {rolesStr}", false);
			}

			await DiscordBotController.BroadcastBotEvent("player-find-by-username", new
			{
				query = username,
				resultCount = matches.Count,
				results = matches.Select(p => new { p.PlayerId, p.Player?.Username, p.Player?.DisplayName }).ToList()
			});

			await cmd.FollowupAsync(embed: eb.Build(), ephemeral: eph);
		}
        
        private async Task CmdGetImageNameToPlayerId(SocketSlashCommand cmd, bool eph)
        {
            await cmd.DeferAsync(ephemeral: eph);

            await Task.Run(async () =>
            {
                try
                {
                    var imageNameOption = cmd.Data.Options.FirstOrDefault(x => x.Name == "image-name")?.Value?.ToString();

                    if (string.IsNullOrEmpty(imageNameOption))
                    {
                        var errorEmbed = new EmbedBuilder()
                            .WithColor(Color.Red)
                            .WithDescription("❌ Please provide a valid image name.");

                        await cmd.FollowupAsync(embed: errorEmbed.Build(), ephemeral: eph);
                        return;
                    }

                    ulong? playerId = ImageMetadataDB.GetPlayerIdFromImageName(imageNameOption);

                    var eb = new EmbedBuilder();

                    if (playerId.HasValue)
                    {
                        eb.WithTitle("Image Owner Found")
                          .WithColor(Color.Green)
                          .AddField("Image Name", $"`{imageNameOption}`", true)
                          .AddField("Player ID", $"`{playerId.Value}`", true)
                          .WithCurrentTimestamp();
                    }
                    else
                    {
                        eb.WithTitle("Image Not Found")
                          .WithColor(Color.Orange)
                          .WithDescription($"Could not find a player associated with the image name: `{imageNameOption}`")
                          .WithCurrentTimestamp();
                    }

                    await cmd.FollowupAsync(embed: eb.Build(), ephemeral: eph);
                }
                catch (Exception ex)
                {
                    var failEmbed = new EmbedBuilder()
                        .WithColor(Color.Red)
                        .WithDescription("❌ An internal database error occurred while fetching the image metadata.");

                    await cmd.FollowupAsync(embed: failEmbed.Build(), ephemeral: eph);
                }
            });
        }

		private async Task CmdPlayerFindById(SocketSlashCommand cmd, bool eph)
		{
			long playerId = (long)cmd.Data.Options.First(o => o.Name == "playerid").Value;
			await cmd.DeferAsync(ephemeral: eph);

			var player = PlayerDB.Players.FindById(playerId);
			if (player == null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Not Found", $"No player with ID `{playerId}`."), ephemeral: eph);
				return;
			}

			string status = player.Player?.PlayerExtra?.Heartbeat?.isOnline == true ? "Online" : "Offline";
			string rolesStr = player.PlayerRoles?.Count > 0
				? string.Join(", ", player.PlayerRoles.Select(r => $"`{r}`"))
				: "None";

			var eb = new EmbedBuilder()
				.WithTitle($"Player `{playerId}` — {player.Player?.DisplayName ?? player.Player?.Username}")
				.WithColor(RandomColor())
				.WithThumbnailUrl($"{ServerConfig.BaseURL}/imageserver/{player.Player?.ProfileImage}")
				.AddField("Player ID", $"`{playerId}`", true)
				.AddField("Username", $"`{player.Player?.Username}`", true)
				.AddField("Display Name", $"`{player.Player?.DisplayName ?? "—"}`", true)
				.AddField("Status", status, true)
				.AddField("Level", $"`{player.Player?.Level}`", true)
				.AddField("Roles", rolesStr, false)
				.WithFooter("Vanadium • Player Find by ID")
				.WithCurrentTimestamp()
				.Build();

			await DiscordBotController.BroadcastBotEvent("player-find-by-id", new
			{
				playerId,
				username = player.Player?.Username,
				displayName = player.Player?.DisplayName
			});

			await cmd.FollowupAsync(embed: eb, ephemeral: eph);
		}

		private async Task CmdDbExecute(SocketSlashCommand cmd, bool eph)
		{
			string expression = (string)cmd.Data.Options.First(o => o.Name == "expression").Value;
			await cmd.DeferAsync(ephemeral: eph);

			object? result = null;
			string? errorMessage = null;

			try
			{
				var setRoomMatch = System.Text.RegularExpressions.Regex.Match(
					expression,
					@"^RoomDB\.Rooms\.Set\((\d+),\s*(\w+),\s*(.*)\)$",
					System.Text.RegularExpressions.RegexOptions.IgnoreCase);

				var setPlayerMatch = System.Text.RegularExpressions.Regex.Match(
					expression,
					@"^PlayerDB\.Players\.Set\((\d+),\s*(\w+),\s*(.*)\)$",
					System.Text.RegularExpressions.RegexOptions.IgnoreCase);

				if (setRoomMatch.Success)
				{
					long rid = long.Parse(setRoomMatch.Groups[1].Value);
					string field = setRoomMatch.Groups[2].Value;
					string rawValue = setRoomMatch.Groups[3].Value.Trim().Trim('"');

					var room = RoomDB.Rooms.FindOne(r => r.RoomId == rid);
					if (room == null)
					{
						errorMessage = $"Room `{rid}` not found.";
					}
					else
					{
						var prop = typeof(RoomDBClasses.Room).GetProperty(field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
						if (prop == null)
						{
							errorMessage = $"Room has no property `{field}`.";
						}
						else
						{
							object? converted = prop.PropertyType.IsEnum
								? Enum.Parse(prop.PropertyType, rawValue, ignoreCase: true)
								: Convert.ChangeType(rawValue, prop.PropertyType);
							string oldValue = prop.GetValue(room)?.ToString() ?? "null";
							prop.SetValue(room, converted);
							RoomDB.Rooms.Update(room);

							var onlineForSet = PlayerDB.Players.FindAll()
								.Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
								.Select(p => p.PlayerId).ToList();
							foreach (var pid in onlineForSet)
								await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(rid));

							await DiscordBotController.BroadcastBotEvent("db-execute-set", new
							{
								target = "room",
								id = rid,
								field,
								oldValue,
								newValue = rawValue
							});

							result = new { updated = true, roomId = rid, field, oldValue, newValue = rawValue };
						}
					}
				}
				else if (setPlayerMatch.Success)
				{
					long pid2 = long.Parse(setPlayerMatch.Groups[1].Value);
					string field = setPlayerMatch.Groups[2].Value;
					string rawValue = setPlayerMatch.Groups[3].Value.Trim().Trim('"');

					var player = PlayerDB.Players.FindById(pid2);
					if (player == null)
					{
						errorMessage = $"Player `{pid2}` not found.";
					}
					else
					{
						var prop = typeof(PlayerDBClasses.Player).GetProperty(field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
						if (prop == null)
						{
							errorMessage = $"Player has no property `{field}`.";
						}
						else
						{
							object? converted = prop.PropertyType.IsEnum
								? Enum.Parse(prop.PropertyType, rawValue, ignoreCase: true)
								: Convert.ChangeType(rawValue, prop.PropertyType);
							string oldValue = prop.GetValue(player.Player)?.ToString() ?? "null";
							prop.SetValue(player.Player, converted);
							PlayerDB.Players.Update(player);

							var updatedAccount = PlayerDB.GetAccountMe(pid2);
							await NotiController.SendAccountUpdate(pid2, updatedAccount);

							await DiscordBotController.BroadcastBotEvent("db-execute-set", new
							{
								target = "player",
								id = pid2,
								field,
								oldValue,
								newValue = rawValue
							});

							result = new { updated = true, playerId = pid2, field, oldValue, newValue = rawValue };
						}
					}
				}
				else if (expression.StartsWith("RoomDB.Rooms.Count()", StringComparison.OrdinalIgnoreCase))
					result = RoomDB.Rooms.Count();
				else if (expression.StartsWith("RoomDB.Rooms.FindAll()", StringComparison.OrdinalIgnoreCase))
					result = RoomDB.Rooms.FindAll().Select(r => new { r.RoomId, r.Name }).ToList();
				else if (expression.StartsWith("RoomDB.SubRoomSaves.Count()", StringComparison.OrdinalIgnoreCase))
					result = RoomDB.SubRoomSaves.Count();
				else if (expression.StartsWith("PlayerDB.Players.Count()", StringComparison.OrdinalIgnoreCase))
					result = PlayerDB.Players.Count();
				else if (expression.StartsWith("PlayerDB.Players.FindAll()", StringComparison.OrdinalIgnoreCase))
					result = PlayerDB.Players.FindAll().Select(p => new { p.PlayerId, p.Player?.Username, p.Player?.DisplayName }).ToList();
				else if (expression.StartsWith("RoomDB.Rooms.FindOne(r => r.RoomId == ", StringComparison.OrdinalIgnoreCase))
				{
					var match = System.Text.RegularExpressions.Regex.Match(expression, @"\d+");
					if (match.Success && long.TryParse(match.Value, out long rid))
						result = RoomDB.GetRoom(rid);
					else
						errorMessage = "Could not parse Room ID from expression.";
				}
				else if (expression.StartsWith("PlayerDB.Players.FindOne(p => p.PlayerId == ", StringComparison.OrdinalIgnoreCase))
				{
					var match = System.Text.RegularExpressions.Regex.Match(expression, @"\d+");
					if (match.Success && long.TryParse(match.Value, out long pid))
						result = PlayerDB.Players.FindById(pid);
					else
						errorMessage = "Could not parse Player ID from expression.";
				}
				else if (expression.StartsWith("PlayerDB.Players.FindOne(p => p.Player.Username == \"", StringComparison.OrdinalIgnoreCase))
				{
					var match = System.Text.RegularExpressions.Regex.Match(expression, "\"([^\"]+)\"");
					if (match.Success)
					{
						string uname = match.Groups[1].Value;
						result = PlayerDB.Players.FindOne(p => p.Player.Username.Equals(uname, StringComparison.OrdinalIgnoreCase));
					}
					else
						errorMessage = "Could not parse username from expression.";
				}
				else if (expression.StartsWith("RoomDB.Rooms.FindOne(r => r.Name == \"", StringComparison.OrdinalIgnoreCase))
				{
					var match = System.Text.RegularExpressions.Regex.Match(expression, "\"([^\"]+)\"");
					if (match.Success)
					{
						string rname = match.Groups[1].Value;
						result = RoomDB.Rooms.FindOne(r => r.Name.Equals(rname, StringComparison.OrdinalIgnoreCase));
					}
					else
						errorMessage = "Could not parse room name from expression.";
				}
				else
				{
					errorMessage = $"Expression `{expression}` is not a recognised pattern.\n\nSupported:\n" +
						"• `RoomDB.Rooms.Set(123, Name, \"New Name\")`\n" +
						"• `RoomDB.Rooms.Set(123, ImageName, \"abc.png\")`\n" +
						"• `PlayerDB.Players.Set(123, Username, \"NewName\")`\n" +
						"• `PlayerDB.Players.Set(123, DisplayName, \"New Name\")`\n" +
						"• `RoomDB.Rooms.Count()`\n" +
						"• `RoomDB.Rooms.FindAll()`\n" +
						"• `RoomDB.Rooms.FindOne(r => r.RoomId == 123)`\n" +
						"• `RoomDB.Rooms.FindOne(r => r.Name == \"name\")`\n" +
						"• `RoomDB.SubRoomSaves.Count()`\n" +
						"• `PlayerDB.Players.Count()`\n" +
						"• `PlayerDB.Players.FindAll()`\n" +
						"• `PlayerDB.Players.FindOne(p => p.PlayerId == 123)`\n" +
						"• `PlayerDB.Players.FindOne(p => p.Player.Username == \"name\")`";
				}
			}
			catch (Exception ex)
			{
				errorMessage = ex.Message;
			}

			if (errorMessage != null)
			{
				await cmd.FollowupAsync(embed: ErrorEmbed("Execute Failed", errorMessage), ephemeral: eph);
				return;
			}

			string resultJson = Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
			if (resultJson.Length > 1900)
				resultJson = resultJson[..1900] + "\n... (truncated)";

			await DiscordBotController.BroadcastBotEvent("db-execute", new
			{
				expression,
				executedAt = DateTime.UtcNow
			});

			var eb2 = new EmbedBuilder()
				.WithTitle("DB Execute — Result")
				.WithDescription($"Expression: `{expression}`")
				.WithColor(RandomColor())
				.AddField("Result", $"```json\n{resultJson}\n```", false)
				.WithFooter("Vanadium • DB Execute")
				.WithCurrentTimestamp()
				.Build();

			await cmd.FollowupAsync(embed: eb2, ephemeral: eph);
		}

		private static Color RandomColor()
		{
			var rng = Random.Shared;
			return new Color(rng.Next(30, 230), rng.Next(30, 230), rng.Next(30, 230));
		}

		private static EmbedBuilder SuccessEmbed(string title, string description)
			=> new EmbedBuilder()
				.WithTitle($"{title}")
				.WithDescription(description)
				.WithColor(Color.Green)
				.WithFooter("Vanadium")
				.WithCurrentTimestamp();

		private static Embed ErrorEmbed(string title, string description)
			=> new EmbedBuilder()
				.WithTitle($"{title}")
				.WithDescription(description)
				.WithColor(Color.Red)
				.WithFooter("Vanadium")
				.WithCurrentTimestamp()
				.Build();

		private static long ToUnix(DateTime dt)
			=> dt == default ? DateTimeOffset.UtcNow.ToUnixTimeSeconds()
							 : new DateTimeOffset(dt, TimeKind(dt)).ToUnixTimeSeconds();

		private static TimeSpan TimeKind(DateTime dt)
			=> dt.Kind == DateTimeKind.Utc ? TimeSpan.Zero : TimeZoneInfo.Local.GetUtcOffset(dt);

		private class RecNetSession
		{
			public DateTime Expires { get; set; }
			public string AccessToken { get; set; } = "";
		}

		private class RecNetRoom
		{
			public long RoomId { get; set; }
			public string? Name { get; set; }
			public string? Description { get; set; }
			public string? ImageName { get; set; }
			public int MaxPlayers { get; set; }
			public bool CloningAllowed { get; set; }
			public bool IsDeveloperOwned { get; set; }
			public bool IsRRO { get; set; }
			public bool IsRecRoomApproved { get; set; }
			public bool SupportsScreens { get; set; }
			public bool SupportsWalkVR { get; set; }
			public bool SupportsTeleportVR { get; set; }
			public bool SupportsMobile { get; set; }
			public bool SupportsJuniors { get; set; }
			public int AgeRating { get; set; }
			public DateTime CreatedAt { get; set; }
			public DateTime PublishedAt { get; set; }
			public RoomDBClasses.WarningMaskType WarningMask { get; set; }
			public List<RecNetSubRoom>? SubRooms { get; set; }
			public List<RecNetLoadScreen>? LoadScreens { get; set; }
			public List<string>? PromoImages { get; set; }
			public List<RecNetTag>? Tags { get; set; }
		}

		private class RecNetSubRoom
		{
			public long SubRoomId { get; set; }
			public long RoomId { get; set; }
			public string? Name { get; set; }
			public string? UnitySceneId { get; set; }
			public int MaxPlayers { get; set; }
			public int Accessibility { get; set; }
		}

		private class RecNetLoadScreen
		{
			public string? ImageName { get; set; }
			public string? Title { get; set; }
			public string? Subtitle { get; set; }
		}

		private class RecNetTag
		{
			public string? Tag { get; set; }
			public int Type { get; set; }
		}

		private class RecNetSavesResponse
		{
			public List<RecNetSave>? Results { get; set; }
		}

		private class RecNetSave
		{
			public string? DataBlob { get; set; }
			public string? DataBlobHash { get; set; }
			public DateTime CreatedAt { get; set; }
			public int? SavedOnPlatform { get; set; }
			public int? SavedOnDeviceClass { get; set; }
			public int PersistenceVersion { get; set; }
			public int UgcSubVersion { get; set; }
		}

		private class SavedTokenEntry
		{
			public string AccessToken { get; set; } = "";
			public DateTime Expires { get; set; }
		}

		private class DailyImportRecord
		{
			public int Count { get; set; }
			public DateTime ResetAt { get; set; }
		}

		private class StickyMessage
		{
			public ulong ChannelId { get; set; }
			public ulong MessageId { get; set; }
			public string Content { get; set; } = "";
			public DateTime StickedAt { get; set; }
		}
	}
}