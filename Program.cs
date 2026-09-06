using System.Runtime.InteropServices;
using Microsoft.AspNetCore.HttpOverrides;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http.Features;
using Vanadium.Classes.DBs;
using Vanadium.Controllers;
using Vanadium.Hubs;
using Vanadium.Utils.NotiController;
using Vanadium.Services;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using Vanadium.Classes;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using Vanadium.Auth;
using static Vanadium.Classes.DBs.DBClasses.RoomDBClasses;

namespace Vanadium
{
    public class NoOpAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public NoOpAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 403;
            return Task.CompletedTask;
        }
    }

    public class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        public static string dataDir = Path.Join(Environment.CurrentDirectory, "Data");
        public static string configDir = Path.Join(Environment.CurrentDirectory, "Configs");
        public static ConcurrentDictionary<string, long> PendingConnections = new();
        public static ConcurrentDictionary<long, WebSocket> ActiveSockets = new();
        public static ConcurrentDictionary<string, ulong> PendingDiscordCodes = new();
        public static ConcurrentDictionary<string, (long AccountId, ulong DiscordUserId)> PendingInGameCodes = new();

        private static readonly HashSet<string> RNSIGBypassPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/auth/connect/token",
            "/vannet",
            "/" // TEMPORARY
        };

        private static bool IsRNSIGBypassed(string path)
        {
            foreach (var bypass in RNSIGBypassPaths)
            {
                if (path.StartsWith(bypass, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static async Task Main(string[] args)
        {
            Console.WriteLine("Step 1. Checking for data directory...");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var handle = GetStdHandle(-11);
                SetConsoleMode(handle, 0x0001 | 0x0002 | 0x0004);
            }
            Console.WriteLine("Step 2. Starting Vanadium server...");

            var builder = WebApplication.CreateBuilder(args);
            Console.WriteLine("Step 3. Configuring logging...");

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.Mvc", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Warning);
            Console.WriteLine("Step 4. Configuring services...");

            if (!Directory.Exists(dataDir))
            {
                Console.WriteLine("Setting up data directory...");
                string[] foldersToCreate =
                {
                    dataDir,
                    Path.Join(dataDir, "Images"),
                    Path.Join(dataDir, "Images", "PlayerImages"),
                    Path.Join(dataDir, "Images", "PolaroidImages"),
                    Path.Join(dataDir, "CDN",    "DataBlobs"),
                    Path.Join(dataDir, "CDN",    "InventionBlobs"),
                    Path.Join(dataDir, "CDN",    "RoomBlobs"),
                    Path.Join(dataDir, "Imports"),
                    Path.Join(dataDir, "DBs")
                };
                foreach (var folder in foldersToCreate)
                    Directory.CreateDirectory(folder);

                Console.WriteLine($"Data directory created at {dataDir}");
            }
            Console.WriteLine("Step 5. Configuring form options...");
            
            builder.Services.Configure<FormOptions>(o =>
            {
                o.MultipartBodyLengthLimit = 10_000_000;
            });
            
            builder.Services.Configure<FormOptions>(options =>
            {
                options.ValueCountLimit = 1500; 
            });

            builder.Services.AddControllers().AddJsonOptions(o =>
			{
    			o.JsonSerializerOptions.PropertyNamingPolicy = null;
    			o.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // fixes invention v{ver} save
			});

            builder.Services.AddHttpClient();

            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.All;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });
            Console.WriteLine("Step 6. Configuring authentication and authorization...");

            builder.Services.AddAuthentication("NoOp")
                .AddScheme<AuthenticationSchemeOptions, NoOpAuthHandler>("NoOp", null);

            builder.Services.AddAuthorization();

            builder.Services.AddHostedService<DiscordBotService>();
            builder.Services.AddHostedService<Vanadium.Services.PlayerCountService>();
            builder.Services.AddHostedService<NotificationStartupHeartbeatCleanupService>();
            builder.Services.AddHostedService<PlayerCountService>();

            // ---- VanadiumGuard anti-cheat endpoints (/v1/session/*) ----
            // The guard mod's server side, hosted in-process here rather than as a separate
            // service. Auth reuses the game's own JWT (same secret/issuer as AuthStuff).
            // AllowAnonymousSessions is on because the mod is not yet wired to send the
            // player's token; flip it off once it is. See Guard/ for the vendored source.
            var guardContentRoot = builder.Environment.ContentRootPath;
            var guardOptions = new VanadiumGuard.Server.Hosting.GuardServerOptions
            {
                JwtSecret = "asdahuiwefuhawefipoklefjauiegofwkplijhbgyuTiefhugytefui",
                JwtIssuer = "ajnshuyauhj",
                InternalApiKey = "c7744487f2616e3bdfaa99d9861416ae90b9f9f46a57c80d25edb75f9f1895e8",
                AllowAnonymousSessions = true,
                PolicyDirectory = "Guard/policies",
                ReferenceImageDirectory = "Guard/builds",
                BanDirectory = "Guard/bans",
            };
            builder.Services.AddSingleton(guardOptions);
            builder.Services.AddSingleton<VanadiumGuard.Server.Sessions.ISessionStore, VanadiumGuard.Server.Sessions.InMemorySessionStore>();
            builder.Services.AddSingleton<VanadiumGuard.Server.Policy.PolicyEngine>();
            builder.Services.AddSingleton<VanadiumGuard.Server.Challenges.ChallengeService>();
            builder.Services.AddSingleton(sp => new VanadiumGuard.Server.Policy.PolicyProvider(
                sp.GetRequiredService<ILogger<VanadiumGuard.Server.Policy.PolicyProvider>>(),
                System.IO.Path.Combine(guardContentRoot, guardOptions.PolicyDirectory)));
            builder.Services.AddSingleton<VanadiumGuard.Server.Challenges.IReferenceImageStore>(sp =>
                new VanadiumGuard.Server.Challenges.FileReferenceImageStore(
                    sp.GetRequiredService<ILogger<VanadiumGuard.Server.Challenges.FileReferenceImageStore>>(),
                    System.IO.Path.Combine(guardContentRoot, guardOptions.ReferenceImageDirectory)));
            builder.Services.AddSingleton(sp => new VanadiumGuard.Server.Bans.MachineRegistry(
                sp.GetRequiredService<ILogger<VanadiumGuard.Server.Bans.MachineRegistry>>(),
                System.IO.Path.Combine(guardContentRoot, guardOptions.BanDirectory)));
            builder.Services.AddSingleton<VanadiumGuard.Server.Security.IPlayerAuthenticator, VanadiumGuard.Server.Security.JwtPlayerAuthenticator>();
            builder.Services.AddHostedService<VanadiumGuard.Server.Hosting.LivenessSweeper>();
            // ---- end VanadiumGuard ----

            Console.WriteLine("Step 7. Configuring builder...");

            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                await next();

                string timeStr = DateTime.UtcNow.ToString("HH:mm:ss");
                string timePart = $"\u001b[91m[{timeStr}]\u001b[0m";

                string method = context.Request.Method.ToUpper();
                string methodColor = "\u001b[37m";

                if (method == "GET")
                    methodColor = "\u001b[32m";
                else if (method == "POST" || method == "PUT")
                    methodColor = "\u001b[33m";
                else if (method == "DELETE")
                    methodColor = "\u001b[31m";

                string methodPart = $"{methodColor}{method}\u001b[0m";

                string fullUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
                string urlPart = $"\u001b[37m{fullUrl}\u001b[0m";

                int statusCode = context.Response.StatusCode;
                string statusColor = "\u001b[37m";

				if (statusCode >= 100 && statusCode < 200)
    				statusColor = "\u001b[33m";
				else if (statusCode >= 200 && statusCode < 300)
    				statusColor = "\u001b[32m";
				else if (statusCode >= 300 && statusCode < 400)
    				statusColor = "\u001b[36m";
				else if (statusCode >= 400)
    				statusColor = "\u001b[31m";

                string statusPart = $"{statusColor}{statusCode}\u001b[0m";

                string ip = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "Unknown";

                string ipPart = $"\u001b[38;5;208m[{ip}]\u001b[0m";

                Console.WriteLine($"{timePart} {ipPart} {methodPart}: {urlPart} {statusPart}");
            });
            Console.WriteLine("Step 8. Configuring middleware...");

            app.UseForwardedHeaders();
            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            });

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.Use(async (context, next) =>
            {
                string method = context.Request.Method.ToUpperInvariant();

                if ((method == "POST" || method == "PUT") && !IsRNSIGBypassed(context.Request.Path))
                {
                    context.Request.EnableBuffering();
                    var playerId = AuthStuff.GetPlayerId(context.Request) ?? -1;
                    RNSIGHandler.IssuePlayerFixedKey(playerId, AuthStuff.GetXRNSIG(context.Request) ?? "");
                    if (!await RNSIGHandler.ValidateRNSIG(context.Request))
                    {
                        context.Response.StatusCode = 403;
                        return;
                    }
                }

                await next();
            });
            Console.WriteLine("Step 9. Mapping controllers...");

            app.MapControllers();

            // VanadiumGuard anti-cheat endpoints (/v1/session/* and /v1/internal/*).
            VanadiumGuard.Server.Endpoints.GuardEndpoints.MapGuardEndpoints(app);

            ChatDB.Initialize();

            RoomDB.Setup();

            // await Task.Run(() => RoomDB.Rooms.FindAll().Where(r => r.ImageName == "DefaultRoomImage.png").ToList().ForEach(async r => { r.ImageName = "DefaultRoomImage.jpg"; RoomDB.Rooms.Update(r); await NotificationsController.RefreshRoom(r.RoomId); })); // temp

            Console.WriteLine("Step 10. Starting background tasks...");
            Vanadium.Cloudflare.StartCloudflared();
            Vanadium.Classes.Bluetint.StartSweep();
            Console.WriteLine("Step 11. Starting server...");
	
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    var now = DateTime.Now;
                    var nextMidnight = now.Date.AddDays(1);
                    await Task.Delay(nextMidnight - now);

                    var players = PlayerDB.Players.FindAll().ToList();
                    foreach (var player in players)
                    {
                        if (player?.Player?.Reputation == null) continue;
                        player.Player.Reputation.CheerCredit = 20;
                        PlayerDB.Players.Update(player);
                    }

                    Console.WriteLine("[CheerReset] its 12:00! all cheers get reset");
                }
            });
            
            /*var _patchRoom = RoomDB.Rooms.FindById(1027L);
if (_patchRoom != null)
{
    var _patchSub = _patchRoom.SubRooms?.FirstOrDefault(s => s.SubRoomId == 2090);
    if (_patchSub != null)
    {
        var _patchSave = new currentSave
        {
            SubRoomDataSaveId = 79817305,
            SubRoomId = 2090,
            RoomId = 1027,
            DataBlob = "December.room",
            DataBlobHash = null,
            ReferencedUnityAssetIds = new List<string>(),
            UnitySubAssets = new List<string>(),
            ReferencedUnityAssets = new List<string>(),
            UnityAssetId = "MITM",
            PersistenceVersion = 0,
            OMVersion = 0,
            UgcSubVersion = 0,
            SavedByAccountId = 1,
            SavedOnPlatform = 0,
            SavedOnDeviceClass = -1,
            Description = "Generators randomly dissapearing fixed",
            Tags = new List<string>(),
            ModerationState = 0,
            CreatedAt = new DateTime(2023, 12, 22, 23, 6, 18, 899, DateTimeKind.Unspecified)
                .AddTicks(-TimeSpan.FromHours(7).Ticks)
        };

        _patchSub.CurrentSave = _patchSave;
        _patchSub.DataBlob = "December.room";

        RoomDB.SubRoomSaves.DeleteMany(s => s.SubRoomDataSaveId == 79817305);
        RoomDB.SubRoomSaves.Insert(_patchSave);
        RoomDB.Rooms.Update(_patchRoom);
    }
}*/

            Console.WriteLine("Step 13. Starting server really...");

            app.Run("http://localhost:2059");
        }
    }
}