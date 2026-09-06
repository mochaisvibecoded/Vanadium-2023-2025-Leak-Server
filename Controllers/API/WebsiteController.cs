using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Utils.NotiController;
using Vanadium.Classes;
using Vanadium.Services;
using SixLabors.ImageSharp.PixelFormats;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Diagnostics;
using Vanadium.enums;
using static Vanadium.Classes.DBs.DBClasses.RoomDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using static Vanadium.Classes.DBs.DBClasses.AnnoucementDBClasses;
using System.Text.Json.Serialization;

namespace Vanadium.Controllers
{
    [ApiController]
    [TypeFilter(typeof(DirectorAuditFilter))]
    public class WebsiteController : ControllerBase
    {
        private const string DiscordClientId = "1512856861374812281";
        private const string DiscordClientSecret = "9Rpo6lwGutlv-fiMnrzGMn99AIkAfeLK";
        private const string DiscordRedirectUri = "https://reloxa.xyz/VanNet/Director";
        private const string RequiredGuildId = "1350986037526007919";
        private const string RequiredRoleId = "1517772785773051965"; // dont give urself this role or change it
        private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(45);
        internal static readonly Dictionary<string, DirectorSession> DirectorSessions = new();
        internal static readonly object SessionLock = new();

        internal class DirectorSession
        {
            public string Token { get; set; } = "";
            public DateTime ExpiresAt { get; set; }
            public string? DiscordUserId { get; set; }
            public WebSocket? Socket { get; set; }
        }

        private bool ValidateDirectorToken(out DirectorSession? session)
        {
            session = null;

            string? token = Request.Headers["X-Director-Token"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(token))
                token = Request.Cookies["director_session"];

            if (string.IsNullOrWhiteSpace(token))
                return false;

            lock (SessionLock)
            {
                if (!DirectorSessions.TryGetValue(token, out session))
                    return false;

                if (session.ExpiresAt <= DateTime.UtcNow)
                {
                    DirectorSessions.Remove(token);
                    session = null;
                    return false;
                }

                session.ExpiresAt = DateTime.UtcNow.Add(SessionDuration);
            }

            return true;
        }

        private bool ValidateDirectorCookie(out DirectorSession? session)
        {
            session = null;
            var token = Request.Cookies["director_session"];
            if (string.IsNullOrWhiteSpace(token)) return false;
            lock (SessionLock)
            {
                if (!DirectorSessions.TryGetValue(token, out session)) return false;
                if (session.ExpiresAt <= DateTime.UtcNow)
                {
                    DirectorSessions.Remove(token);
                    session = null;
                    return false;
                }
            }
            return true;
        }

        private string ResolveMime(string filename) => Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".html" => "text/html",
            _ => "application/octet-stream"
        };

        [HttpGet("/p/Share")]
        public IActionResult ServeIndex()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "VanNet.html");
            if (!System.IO.File.Exists(path)) return NotFound("");
            return PhysicalFile(path, "text/html");
        }

        [HttpGet("/vannet-but-secret")]
        public IActionResult ServeVanNet()
        {
            //string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "ActualVanNet", "index.html");
            //if (!System.IO.File.Exists(path)) return NotFound("");
            //return PhysicalFile(path, "text/html");
            return StatusCode(301, new { message = "Website has been moved to /vannet", StatusCode = 301 });
        }
        
        [HttpGet("/image/{imageId}")]
        public IActionResult ServeImagePreviewShitty()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "ActualVanNet", "image.html");
            if (!System.IO.File.Exists(path)) return NotFound("");
            return PhysicalFile(path, "text/html");
        }

        [HttpGet("/vannet")]
        public IActionResult ServeVanNetForReal()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "ActualVanNet", "New folder", "index.html");//Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "ActualVanNet", "index.html"); // delete when you add recnet i think its funny
            if (!System.IO.File.Exists(path)) return NotFound("something went wrong");
            Response.Headers.CacheControl = "no-cache";
            return PhysicalFile(path, "text/html");
        }

        [HttpGet("/vannet/asset/{filename}")]
        public IActionResult ServeVanNetAsset(string filename)
        {
            string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "ActualVanNet", "asset", filename);
            if (!System.IO.File.Exists(path)) return NotFound("");
            Response.Headers.CacheControl = "no-cache";
            return PhysicalFile(path, ResolveMime(filename));
        }
        
        [HttpGet("/vannet/Director")]
        public IActionResult ServeAuthorize()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "Director", "Authorize.html");
            if (!System.IO.File.Exists(path)) return NotFound("");
            return PhysicalFile(path, "text/html");
        }

        [HttpGet("/vannet/Director/Panel")]
        [HttpGet("/vannet/Director/AdminController")]
        public IActionResult ServeDirectorPanel()
        {
            if (!ValidateDirectorCookie(out _)) return Redirect("/vannet/Director");
            string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "Director", "Director.html");
            if (!System.IO.File.Exists(path)) return NotFound("");
            Response.Headers.CacheControl = "no-cache";
            return PhysicalFile(path, "text/html");
        }

        [HttpGet("/vannet/Director/asset/{filename}")]
        public IActionResult ServeDirectorAsset(string filename)
        {
            if (!ValidateDirectorCookie(out _)) return NotFound("");
            string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "Director", filename);
            if (!System.IO.File.Exists(path)) return NotFound("");
            Response.Headers.CacheControl = "no-cache";
            return PhysicalFile(path, ResolveMime(filename));
        }

        [HttpGet("/RecNet/Frontend/{filename}")]
        public IActionResult ServeFrontendAsset(string filename)
        {
            string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", filename);
            if (!System.IO.File.Exists(path)) return NotFound("");
            return PhysicalFile(path, ResolveMime(filename));
        }

        [HttpGet("/RecNet/Frontend/Director/{filename}")]
        public IActionResult ServeDirectorAssetLegacy(string filename)
        {
            if (!ValidateDirectorCookie(out _)) return NotFound("");
            string path = Path.Combine(Environment.CurrentDirectory, "RecNet", "Frontend", "Director", filename);
            if (!System.IO.File.Exists(path)) return NotFound("");
            Response.Headers.CacheControl = "no-cache";
            return PhysicalFile(path, ResolveMime(filename));
        }

        [HttpPost("/vannet/api/director/discord-callback")]
        public async Task<IActionResult> DirectorDiscordCallback([FromBody] DirectorDiscordCallbackRequest body)
        {
            if (string.IsNullOrWhiteSpace(body?.Code))
                return BadRequest(new { success = false, error = "Code required." });

            using var http = new HttpClient();

            var tokenResp = await http.PostAsync("https://discord.com/api/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = DiscordClientId,
                    ["client_secret"] = DiscordClientSecret,
                    ["grant_type"] = "authorization_code",
                    ["code"] = body.Code,
                    ["redirect_uri"] = DiscordRedirectUri
                }));

            if (!tokenResp.IsSuccessStatusCode)
                return Ok(new { success = false, error = "The code failed to exchange" });

            JsonElement tokenData;
            try { tokenData = JsonSerializer.Deserialize<JsonElement>(await tokenResp.Content.ReadAsStringAsync()); }
            catch { return Ok(new { success = false, error = "Wrong discord response" }); }

            if (!tokenData.TryGetProperty("access_token", out var atEl))
                return Ok(new { success = false, error = "No access token from discord" });

            string accessToken = atEl.GetString() ?? "";
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var memberResp = await http.GetAsync($"https://discord.com/api/users/@me/guilds/{RequiredGuildId}/member");
            if (!memberResp.IsSuccessStatusCode)
                return Ok(new { success = false, error = "You arent even in the discord" });

            JsonElement memberData;
            try { memberData = JsonSerializer.Deserialize<JsonElement>(await memberResp.Content.ReadAsStringAsync()); }
            catch { return Ok(new { success = false, error = "Something parsed wrong and your member data wasnt given correctly" }); }

            if (!memberData.TryGetProperty("roles", out var rolesEl) || !rolesEl.EnumerateArray().Any(r => r.GetString() == RequiredRoleId))
                return Ok(new { success = false, error = "You arent a dev therefore you cant access the panel" });

            string? discordUserId = null;
            if (memberData.TryGetProperty("user", out var userEl) && userEl.TryGetProperty("id", out var idEl))
                discordUserId = idEl.GetString();

            string sessionToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var expiry = DateTime.UtcNow.Add(SessionDuration);

            lock (SessionLock)
            {
                DirectorSessions[sessionToken] = new DirectorSession
                {
                    Token = sessionToken,
                    ExpiresAt = expiry,
                    DiscordUserId = discordUserId
                };
            }

            Response.Cookies.Append("director_session", sessionToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.Add(SessionDuration)
            });

            return Ok(new { success = true, token = sessionToken });
        }

        [HttpGet("/vannet/ws/director-session")]
        public async Task DirectorSessionWebSocket()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = 400;
                return;
            }

            string token = Request.Query["token"].ToString();
            if (string.IsNullOrWhiteSpace(token))
            {
                HttpContext.Response.StatusCode = 401;
                return;
            }

            lock (SessionLock)
            {
                if (!DirectorSessions.TryGetValue(token, out var chk) || chk.ExpiresAt <= DateTime.UtcNow)
                {
                    HttpContext.Response.StatusCode = 401;
                    return;
                }
            }

            var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();

            lock (SessionLock)
            {
                if (DirectorSessions.TryGetValue(token, out var s)) s.Socket = ws;
            }

            using var cts = new CancellationTokenSource();
            var recvBuffer = new byte[1024];

            var monitor = Task.Run(async () =>
            {
                while (true)
                {
                    try { await Task.Delay(5000, cts.Token); }
                    catch (TaskCanceledException) { return; }

                    if (ws.State != WebSocketState.Open) return;

                    bool expired;
                    lock (SessionLock)
                    {
                        expired = !DirectorSessions.TryGetValue(token, out var s) || s.ExpiresAt <= DateTime.UtcNow;
                    }

                    if (!expired) continue;

                    try
                    {
                        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            type = "session_expired",
                            message = "Your session has expired. Please re-authorize."
                        });
                        await ws.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None);
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session expired", CancellationToken.None);
                    }
                    catch { }
                    return;
                }
            });

            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(recvBuffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch { }

            cts.Cancel();
            try { await monitor; } catch { }
        }

        [HttpGet("/vannet/api/rooms/hot")]
        public IActionResult GetHotRooms([FromQuery] int skip = 0, [FromQuery] int take = 20)
        {
            var allRooms = RoomDB.Rooms.FindAll()
                .Where(r =>
                    r.Accessibility == RoomAccessibility.Public &&
                    r.State == RoomState.Active &&
                    !r.ExcludeFromLists)
                .OrderByDescending(r => r.Stats.VisitCount + r.Stats.CheerCount * 2 + r.Stats.FavoriteCount * 3)
                .Skip(skip)
                .Take(take)
                .Select(r => new
                {
                    roomId = r.RoomId,
                    name = r.Name,
                    description = r.Description,
                    imageName = r.ImageName,
                    creatorAccountId = r.CreatorAccountId,
                    stats = r.Stats,
                    maxPlayers = r.MaxPlayers,
                    ageRating = r.AgeRating,
                    createdAt = r.CreatedAt,
                    tags = r.Tags,
                    supportsMobile = r.SupportsMobile,
                    supportsQuest2 = r.SupportsQuest2,
                    supportsWalkVR = r.SupportsWalkVR
                })
                .ToList();

            return Ok(new { Results = allRooms, TotalResults = allRooms.Count });
        }
        
        /*[HttpPost("api/rooms/import-zip")]
		public async Task<IActionResult> ImportRoomFromZip([FromForm] IFormFile file, [FromForm] DateTime? cutoffDate, [FromForm] DateTime? targetDate)
		{
				if (file == null || file.Length == 0)
						return BadRequest(new { error = "No file provided." });

				string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
				if (ext != ".zip" && ext != ".rar")
						return BadRequest(new { error = "Only .zip and .rar files are accepted." });

				var effectiveCutoff = cutoffDate?.ToUniversalTime() ?? new DateTime(2023, 04, 16, 23, 59, 59, DateTimeKind.Utc);
				var effectiveTarget = targetDate?.ToUniversalTime() ?? new DateTime(2023, 04, 15, 0, 0, 0, DateTimeKind.Utc);

				string tempExtractPath = Path.Combine(Path.GetTempPath(), $"room_import_{Guid.NewGuid()}");
				Directory.CreateDirectory(tempExtractPath);

				try
				{
						string tempFilePath = Path.Combine(tempExtractPath, file.FileName);
						using (var fs = new FileStream(tempFilePath, FileMode.Create))
								await file.CopyToAsync(fs);

						string extractPath = Path.Combine(tempExtractPath, "extracted");
						Directory.CreateDirectory(extractPath);

						if (ext == ".zip")
						{
								System.IO.Compression.ZipFile.ExtractToDirectory(tempFilePath, extractPath);
						}
						else
						{
								using var archive = SharpCompress.Archives.ArchiveFactory.Open(tempFilePath);
								foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
										entry.WriteToDirectory(extractPath, new SharpCompress.Common.ExtractionOptions { ExtractFullPath = true, Overwrite = true });
						}

						var topLevelDirs = Directory.GetDirectories(extractPath);
						string roomPath = topLevelDirs.Length == 1 ? topLevelDirs[0] : extractPath;
						string roomName = Path.GetFileName(roomPath);

						string roomJsonPath = Path.Combine(roomPath, $"{roomName}.json");
						RoomImportRoomData? roomData = null;

						if (System.IO.File.Exists(roomJsonPath))
						{
								try
								{
										var json = await System.IO.File.ReadAllTextAsync(roomJsonPath);
										roomData = System.Text.Json.JsonSerializer.Deserialize<RoomImportRoomData>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
								}
								catch (Exception ex)
								{
										return StatusCode(500, new { error = $"Failed to parse room JSON: {ex.Message}" });
								}
						}

						string cdnPath = Path.Combine(Environment.CurrentDirectory, "Data", "cdn", "room");
						string imageServerPath = Path.Combine(Environment.CurrentDirectory, "Data", "Images");
						Directory.CreateDirectory(cdnPath);

						long newRoomId = RoomDB.CloneRoom(24, roomName, 1) ?? throw new Exception("Failed to clone MakerRoom ID 24");
						var newRoom = RoomDB.GetRoom(newRoomId);

						if (newRoom == null)
								return StatusCode(500, new { error = "Cloned room not found after creation." });

						newRoom.Name = roomName;
						newRoom.DisplayName = roomName;
						newRoom.Description = roomData?.Description ?? "";
						newRoom.MaxPlayers = roomData?.MaxPlayers > 0 ? roomData.MaxPlayers : 8;
						newRoom.MaxPlayerCalculationMode = 0;
						newRoom.CloningAllowed = roomData?.CloningAllowed ?? false;
						newRoom.PersistenceVersion = 0;
						newRoom.UgcSubVersion = 0;
						newRoom.UgcVersion = 0;
						newRoom.MinUgcSubVersion = 0;
						newRoom.IsDeveloperOwned = roomData?.IsDeveloperOwned ?? false;
						newRoom.IsRRO = roomData?.IsRRO ?? false;
						newRoom.IsRecRoomApproved = roomData?.IsRecRoomApproved ?? false;
						newRoom.SupportsScreens = roomData?.SupportsScreens ?? true;
						newRoom.SupportsWalkVR = roomData?.SupportsWalkVR ?? true;
						newRoom.SupportsTeleportVR = roomData?.SupportsTeleportVR ?? true;
						newRoom.SupportsMobile = roomData?.SupportsMobile ?? true;
						newRoom.SupportsJuniors = roomData?.SupportsJuniors ?? true;
						newRoom.AgeRating = roomData?.AgeRating ?? 0;
						newRoom.CreatedAt = roomData?.CreatedAt != default ? roomData.CreatedAt : DateTime.UtcNow;
						newRoom.PublishedAt = roomData?.PublishedAt != default ? roomData.PublishedAt : DateTime.UtcNow;
						newRoom.Accessibility = RoomDBClasses.RoomAccessibility.Private;
						newRoom.State = RoomDBClasses.RoomState.Active;
						newRoom.CreatorAccountId = 167;
						newRoom.PromoImages = new List<string>();
						newRoom.LoadScreens = new List<RoomDBClasses.LoadScreens>();
						newRoom.WarningMask = roomData?.WarningMask ?? default;
						newRoom.CustomWarning = roomData?.CustomWarning;
						newRoom.DisableMicAutoMute = roomData?.DisableMicAutoMute ?? false;
						newRoom.DisableRoomComments = roomData?.DisableRoomComments ?? false;
						newRoom.EncryptVoiceChat = roomData?.EncryptVoiceChat ?? false;
						newRoom.ToxmodEnabled = roomData?.ToxmodEnabled ?? true;
						newRoom.SupportsVRLow = roomData?.SupportsVRLow ?? true;
						newRoom.SupportsQuest2 = roomData?.SupportsQuest2 ?? true;
						newRoom.MinLevel = roomData?.MinLevel ?? 0;
						newRoom.ExcludeFromLists = roomData?.ExcludeFromLists ?? false;
						newRoom.ExcludeFromSearch = roomData?.ExcludeFromSearch ?? false;

						newRoom.Stats = new RoomDBClasses.Stats
						{
								CheerCount = 0,
								FavoriteCount = 0,
								VisitorCount = 0,
								VisitCount = 0
						};

						newRoom.LoadScreens = roomData?.LoadScreens?
								.Select(ls => new RoomDBClasses.LoadScreens
								{
										ImageName = ls.ImageName ?? "",
										Title = ls.Title ?? "",
										Subtitle = ls.Subtitle ?? ""
								})
								.ToList() ?? new List<RoomDBClasses.LoadScreens>();

						newRoom.Tags = roomData?.Tags?
								.Select(t => new RoomDBClasses.Tags { Tag = t.Tag?.ToLowerInvariant() ?? "", Type = RoomDBClasses.TagType.General })
								.Where(t => !string.IsNullOrWhiteSpace(t.Tag))
								.ToList() ?? new List<RoomDBClasses.Tags>();

						string localImageName = "DefaultRoomImage.png";

				var roomImagesDir = Path.Combine(roomPath, "RoomImages", "RoomImage");
				if (Directory.Exists(roomImagesDir))
				{
						var localImage = Directory.GetFiles(roomImagesDir).FirstOrDefault();
						if (localImage != null)
						{
								string ext2 = Path.GetExtension(localImage);
								string safeImageName = $"{Guid.NewGuid()}{ext2}";
								string imageDest = Path.Combine(imageServerPath, safeImageName);
								System.IO.File.Copy(localImage, imageDest);
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
						var importLog = new List<object>();

						foreach (var subRoomDir in Directory.GetDirectories(roomPath))
						{
								string subRoomFolderName = Path.GetFileName(subRoomDir);
								if (subRoomFolderName.StartsWith(".")) continue;

								string subJsonPath = Path.Combine(subRoomDir, $"{subRoomFolderName}.json");
								RoomImportSavesResponse? subSavesData = null;

								if (System.IO.File.Exists(subJsonPath))
								{
										try
										{
												var subJson = await System.IO.File.ReadAllTextAsync(subJsonPath);
												subSavesData = System.Text.Json.JsonSerializer.Deserialize<RoomImportSavesResponse>(subJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
										}
										catch { }
								}

								string? selectedBlob = null;
								DateTime selectedDate = DateTime.UtcNow;
								RoomImportSaveResult? bestSaveMeta = null;

								var directRoomFiles = Directory.GetFiles(subRoomDir, "*.room", SearchOption.TopDirectoryOnly);

								if (directRoomFiles.Length > 0)
								{
										string sourceRoomFile = directRoomFiles[0];
										selectedBlob = Path.GetFileName(sourceRoomFile);
										string destRoomFile = Path.Combine(cdnPath, selectedBlob);
										if (!System.IO.File.Exists(destRoomFile))
												System.IO.File.Copy(sourceRoomFile, destRoomFile);

										selectedDate = DateTime.UtcNow;
										bestSaveMeta = subSavesData?.Results?
												.Where(x => x.CreatedAt <= effectiveCutoff)
												.OrderBy(x => Math.Abs((x.CreatedAt - effectiveTarget).TotalSeconds))
												.FirstOrDefault();
								}
								else
								{
										var dateFolders = Directory.GetDirectories(subRoomDir)
												.Select(d =>
												{
														string folderName = Path.GetFileName(d);
														if (DateTime.TryParseExact(folderName, "yyyy-MM-dd_HH-mm-ss",
																System.Globalization.CultureInfo.InvariantCulture,
																System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
																out DateTime parsedDate))
																return new { Path = d, Date = parsedDate, Valid = true };
														return new { Path = d, Date = DateTime.MinValue, Valid = false };
												})
												.Where(x => x.Valid && x.Date <= effectiveCutoff)
												.OrderBy(x => Math.Abs((x.Date - effectiveTarget).TotalSeconds))
												.ToList();

										if (dateFolders.Count == 0)
										{
												importLog.Add(new { subRoom = subRoomFolderName, status = "skipped", reason = "no valid saves at or before cutoff date" });
												continue;
										}

										var best = dateFolders.First();
										var roomFiles = Directory.GetFiles(best.Path, "*.room");

										if (roomFiles.Length == 0)
										{
												importLog.Add(new { subRoom = subRoomFolderName, status = "skipped", reason = "no .room file in best folder" });
												continue;
										}

										string sourceRoomFile = roomFiles[0];
										selectedBlob = Path.GetFileName(sourceRoomFile);
										string destRoomFile = Path.Combine(cdnPath, selectedBlob);
										if (!System.IO.File.Exists(destRoomFile))
												System.IO.File.Copy(sourceRoomFile, destRoomFile);

										selectedDate = best.Date;
										bestSaveMeta = subSavesData?.Results?
												.Where(s => s.CreatedAt <= effectiveCutoff)
												.OrderBy(s => Math.Abs((s.CreatedAt - effectiveTarget).TotalSeconds))
												.FirstOrDefault();
								}

								if (string.IsNullOrWhiteSpace(selectedBlob)) continue;

								long newSubRoomId = RoomDB.GetNextSubRoomId();

								var newSub = new RoomDBClasses.SubRooms
								{
										SubRoomId = newSubRoomId,
										RoomId = newRoomId,
										Name = subRoomFolderName,
										Accessibility = RoomDBClasses.RoomAccessibility.Private,
										UnitySceneId = "a75f7547-79eb-47c6-8986-6767abcb4f92",
										MaxPlayers = 8,
										IsSandbox = false,
										DataBlob = selectedBlob,
										SavedByAccountId = 167,
										CreatorAccountId = 167,
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
										DataBlobHash = bestSaveMeta?.DataBlobHash ?? "",
										Description = $"Imported via zip. Room Name: \"{roomName}\"",
										SavedByAccountId = 167,
										SavedOnPlatform = bestSaveMeta?.SavedOnPlatform ?? 0,
										SavedOnDeviceClass = bestSaveMeta?.SavedOnDeviceClass ?? 1,
										PersistenceVersion = 0,
										UgcSubVersion = 0,
										OMVersion = 0,
										Tags = new List<string>(),
										ModerationState = 0,
										CreatedAt = selectedDate
								};

								RoomDB.SubRoomSaves.Insert(newSave);
								subRoomsImported++;
								importLog.Add(new { subRoom = newSub.Name, subRoomId = newSubRoomId, status = "imported", blob = selectedBlob, saveDate = selectedDate });
						}

						if (subRoomsImported == 0)
						{
								RoomDB.DeleteRoom(newRoomId);
								return BadRequest(new { error = "No valid subrooms found at or before cutoff date. Room was not created." });
						}

						var createdRoom = RoomDB.GetRoom(newRoomId);

						var allConnectedPlayers = PlayerDB.Players.FindAll().Select(p => p.PlayerId).ToList();
						foreach (var pid in allConnectedPlayers)
								await NotiController.SendRoomUpdate(pid, createdRoom);

						return Ok(new { success = true, message = $"Room '{newRoom.Name}' imported successfully.", roomId = newRoomId, subRoomsImported, log = importLog, room = createdRoom });
				}
				finally
				{
						try { Directory.Delete(tempExtractPath, true); } catch { }
				}
		}*/

        /*[HttpGet("/vannet/api/players/search")]
        public IActionResult SearchPlayers([FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int take = 20)
        {
            IEnumerable<PlayerDBClasses.FullPlayer> query = PlayerDB.Players.FindAll();

            if (!string.IsNullOrWhiteSpace(q))
            {
                string lower = q.ToLowerInvariant();
                query = query.Where(p =>
                    (p.Player?.Username != null && p.Player.Username.ToLower().Contains(lower)) ||
                    (p.Player?.DisplayName != null && p.Player.DisplayName.ToLower().Contains(lower)));
            }

            var results = query
                .OrderByDescending(p => p.Player?.CreatedAt ?? DateTime.MinValue)
                .Skip(skip)
                .Take(take)
                .Select(p => new
                {
                    accountId = p.PlayerId,
                    username = p.Player?.Username,
                    displayName = p.Player?.DisplayName,
                    profileImage = p.Player?.ProfileImage,
                    level = p.Player?.Level ?? 1,
                    createdAt = p.Player?.CreatedAt,
                    bio = p.Player?.Bio
                })
                .ToList();

            return Ok(new { Results = results, TotalResults = results.Count });
        }*/
      
        [HttpGet("/vannet/api/players/{playerId}")]
        public IActionResult GetPlayerProfile(long playerId)
        {
            var player = PlayerDB.Players.FindById(playerId);
            if (player == null || player.Player == null)
                return NotFound("");

            var rooms = RoomDB.Rooms.FindAll()
                .Where(r => r.CreatorAccountId == playerId && r.Accessibility == RoomAccessibility.Public)
                .OrderByDescending(r => r.Stats.VisitCount)
                .Take(6)
                .Select(r => new { roomId = r.RoomId, name = r.Name, imageName = r.ImageName, stats = r.Stats })
                .ToList();

            return Ok(new
            {
                accountId = player.PlayerId,
                username = player.Player.Username,
                displayName = player.Player.DisplayName,
                profileImage = player.Player.ProfileImage,
                level = player.Player.Level,
                createdAt = player.Player.CreatedAt,
                bio = player.Player.Bio,
                rooms
            });
        }

        [HttpGet("/vannet/api/rooms/{roomId:long}")]
        public IActionResult GetRoomDetail(long roomId)
        {
            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound("");

            var creator = PlayerDB.Players.FindById(room.CreatorAccountId);

            return Ok(new
            {
                roomId = room.RoomId,
                name = room.Name,
                description = room.Description,
                imageName = room.ImageName,
                creatorAccountId = room.CreatorAccountId,
                creatorUsername = creator?.Player?.Username,
                stats = room.Stats,
                maxPlayers = room.MaxPlayers,
                ageRating = room.AgeRating,
                createdAt = room.CreatedAt,
                tags = room.Tags,
                supportsMobile = room.SupportsMobile,
                supportsQuest2 = room.SupportsQuest2,
                supportsWalkVR = room.SupportsWalkVR,
                supportsTeleportVR = room.SupportsTeleportVR,
                supportsScreens = room.SupportsScreens,
                accessibility = room.Accessibility,
                loadScreens = room.LoadScreens,
                promoImages = room.PromoImages
            });
        }

        [HttpGet("/vannet/api/director/players/search")]
        public async Task<IActionResult> DirectorSearchPlayers([FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int take = 50)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            IEnumerable<FullPlayer> query = PlayerDB.Players.FindAll();

            if (!string.IsNullOrWhiteSpace(q))
            {
                string lower = q.ToLowerInvariant();
                bool isId = long.TryParse(q, out long searchId);
                query = query.Where(p =>
                    (isId && p.PlayerId == searchId) ||
                    (p.Player?.Username != null && p.Player.Username.ToLower().Contains(lower)) ||
                    (p.Player?.DisplayName != null && p.Player.DisplayName.ToLower().Contains(lower)));
            }

            var results = query
                .OrderByDescending(p => p.Player?.CreatedAt ?? DateTime.MinValue)
                .Skip(skip)
                .Take(take)
                .Select(p => BuildPlayerSummary(p))
                .ToList();

            return Ok(new { Results = results, TotalResults = results.Count });
        }

        [HttpGet("/vannet/api/director/players/list-developers")]
        public IActionResult DirectorListDevelopers()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var results = PlayerDB.Players.FindAll()
                .Where(p => p.PlayerRoles != null && p.PlayerRoles.Contains(PlayerRoles.Developer))
                .OrderByDescending(p => p.Player?.CreatedAt ?? DateTime.MinValue)
                .Select(p => BuildPlayerSummary(p))
                .ToList();

            return Ok(new { Results = results, TotalResults = results.Count });
        }

        [HttpGet("/vannet/api/director/players/list-moderators")]
        public IActionResult DirectorListModerators()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var results = PlayerDB.Players.FindAll()
                .Where(p => p.PlayerRoles != null && p.PlayerRoles.Contains(PlayerRoles.Moderator))
                .OrderByDescending(p => p.Player?.CreatedAt ?? DateTime.MinValue)
                .Select(p => BuildPlayerSummary(p))
                .ToList();

            return Ok(new { Results = results, TotalResults = results.Count });
        }

        [HttpGet("/vannet/api/director/players/{playerId:long}")]
        public async Task<IActionResult> DirectorGetPlayer(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            await ToxModService.EnsureCurrent(player);
            return Ok(BuildPlayerFull(player));
        }

        [HttpGet("/vannet/api/director/players/{playerId:long}/rooms")]
        public IActionResult DirectorGetPlayerRooms(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var rooms = RoomDB.Rooms.FindAll()
                .Where(r => r.CreatorAccountId == playerId)
                .OrderByDescending(r => r.Stats.VisitCount)
                .Take(50)
                .Select(r => new
                {
                    roomId = r.RoomId,
                    name = r.Name,
                    imageName = r.ImageName,
                    stats = r.Stats,
                    accessibility = r.Accessibility,
                    state = r.State
                })
                .ToList();

            return Ok(new { rooms });
        }

        [HttpGet("/vannet/api/director/players/{playerId:long}/heartbeat")]
        public IActionResult DirectorGetHeartbeat(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");
            return Ok(player.Player?.PlayerExtra?.Heartbeat);
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/rename")]
        public async Task<IActionResult> DirectorRenamePlayer(long playerId, [FromBody] DirectorRenameRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (string.IsNullOrWhiteSpace(body?.Username))
                return BadRequest(new { error = "Username required." });

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            string old = player.Player.Username;
            player.Player.Username = body.Username;
            player.Player.DisplayName = body.Username;
            PlayerDB.Players.Update(player);

            var updated = PlayerDB.GetAccountMe(playerId);
            await NotiController.SendAccountUpdate(playerId, updated);
            await NotificationsController.RefreshAccount(playerId);

            return Ok(new { success = true, oldName = old, newName = body.Username });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/clear-name")]
        public async Task<IActionResult> DirectorClearName(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            string? old = player.Player.Username;
            string newName = NameGen.GetRandomName();
            player.Player.Username = newName;
            player.Player.DisplayName = newName;
            PlayerDB.Players.Update(player);

            await NotiController.SendAccountUpdate(playerId, PlayerDB.GetAccountMe(playerId));
            await NotificationsController.RefreshAccount(playerId);

            return Ok(new { success = true, oldName = old, newName });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/name-lock")]
        public async Task<IActionResult> DirectorNameLock(long playerId, [FromBody] DirectorNameLockRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            bool locked = body?.Locked ?? true;
            player.Player.NameLocked = locked;
            PlayerDB.Players.Update(player);

            return Ok(new { success = true, playerId, nameLocked = locked });
        }

        [HttpPost("/vannet/api/director/players/create")]
        public IActionResult DirectorCreatePlayer([FromBody] DirectorCreatePlayerRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (body == null || body.PlayerId <= 0)
                return BadRequest(new { error = "A positive PlayerId is required." });

            string? username = string.IsNullOrWhiteSpace(body.Username) ? null : body.Username.Trim();

            var player = PlayerDB.CreateAccountWithId(
                body.PlayerId,
                username,
                Platforms.Standalone,
                $"director-created-{Guid.NewGuid()}",
                body.IsJunior,
                out string? error);

            if (player == null)
                return BadRequest(new { error = error ?? "Failed to create account." });

            return Ok(new { success = true, playerId = player.PlayerId, username = player.Player?.Username });
        }

        [HttpPost("/vannet/api/director/announcement")]
        public async Task<IActionResult> DirectorSetAnnouncement()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            DirectorAnnouncementRequest body;
            try
            {
                var options = new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter() },
                    PropertyNameCaseInsensitive = true
                };
                body = await JsonSerializer.DeserializeAsync<DirectorAnnouncementRequest>(Request.Body, options);
            }
            catch (JsonException ex)
            {
                return BadRequest(new { error = "Invalid request body.", detail = ex.Message });
            }

            if (body?.Clear == true)
            {
                await AnnoucementDB.DeleteAllAnnouncements(sendWs: true);
                return Ok(new { success = true, cleared = true });
            }

            if (string.IsNullOrWhiteSpace(body?.Title) && string.IsNullOrWhiteSpace(body?.Body))
                return BadRequest(new { error = "Title or body required." });

            var announcement = new AnnoucementDBClasses.DirectorAnnouncementRequest
            {
                AnnouncementType = body.AnnouncementType,
                Body = body.Body ?? "",
                ImageName = body.ImageName ?? "",
                LinkName = body.LinkName ?? "",
                LinkType = body.LinkType,
                LinkUri = body.LinkUri ?? "",
                Title = body.Title ?? ""
            };

            bool created = await AnnoucementDB.CreateAnnouncement(announcement, sendWs: true);
            if (!created)
                return BadRequest(new { error = "Failed to create announcement." });

            return Ok(new { success = true });
        }

        [HttpGet("/vannet/api/director/announcements")]
        public IActionResult DirectorGetAnnouncements()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");
            return Ok(AnnoucementDB.GetAllAnnouncements());
        }

        [HttpPost("/vannet/api/director/announcement/delete/{announcementId:long}")]
        public async Task<IActionResult> DirectorDeleteAnnouncement(long announcementId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            bool deleted = AnnoucementDB.DeleteAnnouncement(announcementId);
            if (!deleted)
                return NotFound(new { error = "Announcement not found." });

            await NotificationsController.SendAnnoucementDelete(announcementId);
            return Ok(new { success = true, announcementId });
        }

        [HttpGet("/vannet/api/director/community-banner")]
        public IActionResult DirectorGetCommunityBanner()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "communityboard.json");
            if (!System.IO.File.Exists(path))
                return Ok(new { message = "", moreInfoUrl = "" });

            var ann = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(path))?["CurrentAnnouncement"];
            return Ok(new
            {
                message = ann?["Message"]?.GetValue<string>() ?? "",
                moreInfoUrl = ann?["MoreInfoUrl"]?.GetValue<string>() ?? ""
            });
        }

        [HttpPost("/vannet/api/director/community-banner")]
        public async Task<IActionResult> DirectorSetCommunityBanner([FromBody] DirectorCommunityBannerRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "communityboard.json");
            if (!System.IO.File.Exists(path))
                return NotFound(new { error = "communityboard.json not found." });

            if (System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(path)) is not System.Text.Json.Nodes.JsonObject root)
                return StatusCode(500, new { error = "Malformed communityboard.json." });

            root["CurrentAnnouncement"] = new System.Text.Json.Nodes.JsonObject
            {
                ["Message"] = body?.Message ?? "",
                ["MoreInfoUrl"] = body?.MoreInfoUrl ?? ""
            };

            string json = root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(path, json);

            var cbPayload = new { Id = "CommunityBoardUpdate", Msg = System.Text.Json.JsonSerializer.Deserialize<object>(json) };
            await NotificationsController.SendToAll(System.Text.Json.JsonSerializer.Serialize(cbPayload));

            return Ok(new { success = true });
        }

        [HttpGet("/vannet/api/director/git/diff")]
        public async Task<IActionResult> DirectorGitDiff()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            const string repoPath = "/home/container/VanaNet";
            if (!Directory.Exists(repoPath))
                return NotFound(new { error = "Repo path not found.", path = repoPath });

            try
            {
                async Task<(int ExitCode, string StdOut, string StdErr)> RunGit(string args)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = args,
                        WorkingDirectory = repoPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process == null)
                        throw new InvalidOperationException("Failed to start git process.");

                    string stdOut = await process.StandardOutput.ReadToEndAsync();
                    string stdErr = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    return (process.ExitCode, stdOut, stdErr);
                }

                var repoTop = await RunGit("rev-parse --show-toplevel");
                var branch = await RunGit("rev-parse --abbrev-ref HEAD");
                var status = await RunGit("status --porcelain");
                var diff = await RunGit("diff HEAD");

                var outBuilder = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(diff.StdOut))
                {
                    outBuilder.Append(diff.StdOut.TrimEnd());
                }
                else
                {
                    outBuilder.AppendLine("No diff output from 'git diff HEAD'.");
                    outBuilder.AppendLine("This usually means there are no unstaged tracked file changes in that repo.");
                }

                outBuilder.AppendLine();
                outBuilder.AppendLine("[debug]");
                outBuilder.AppendLine($"configuredRepoPath: {repoPath}");
                outBuilder.AppendLine($"gitTopLevel: {(string.IsNullOrWhiteSpace(repoTop.StdOut) ? "(none)" : repoTop.StdOut.Trim())}");
                outBuilder.AppendLine($"branch: {(string.IsNullOrWhiteSpace(branch.StdOut) ? "(unknown)" : branch.StdOut.Trim())}");

                if (string.IsNullOrWhiteSpace(status.StdOut))
                    outBuilder.AppendLine("statusPorcelain: (clean)");
                else
                    outBuilder.AppendLine($"statusPorcelain:\n{status.StdOut.TrimEnd()}");

                if (!string.IsNullOrWhiteSpace(diff.StdErr))
                    outBuilder.AppendLine($"diffStderr:\n{diff.StdErr.TrimEnd()}");

                if (repoTop.ExitCode != 0 && !string.IsNullOrWhiteSpace(repoTop.StdErr))
                    outBuilder.AppendLine($"revParseTopLevelStderr:\n{repoTop.StdErr.TrimEnd()}");

                if (branch.ExitCode != 0 && !string.IsNullOrWhiteSpace(branch.StdErr))
                    outBuilder.AppendLine($"revParseBranchStderr:\n{branch.StdErr.TrimEnd()}");

                if (status.ExitCode != 0 && !string.IsNullOrWhiteSpace(status.StdErr))
                    outBuilder.AppendLine($"statusStderr:\n{status.StdErr.TrimEnd()}");

                bool allSucceeded = repoTop.ExitCode == 0 && branch.ExitCode == 0 && status.ExitCode == 0 && diff.ExitCode == 0;

                return Ok(new
                {
                    success = allSucceeded,
                    exitCode = diff.ExitCode,
                    output = outBuilder.ToString()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to run git diff.", detail = ex.Message });
            }
        }

        [HttpGet("/vannet/api/director/git/status")]
        public async Task<IActionResult> DirectorGitStatus()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            const string repoPath = "/home/container/VanaNet";
            if (!Directory.Exists(repoPath))
                return NotFound(new { error = "Repo path not found.", path = repoPath });

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "log --branches --not --remotes -p @{u}..",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                    return StatusCode(500, new { error = "Failed to start git process." });

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                string output = string.IsNullOrWhiteSpace(stderr)
                    ? stdout
                    : string.IsNullOrWhiteSpace(stdout)
                        ? stderr
                        : stdout + "\n\n[stderr]\n" + stderr;

                if (string.IsNullOrWhiteSpace(output))
                    output = "No status output.";

                return Ok(new
                {
                    success = process.ExitCode == 0,
                    exitCode = process.ExitCode,
                    output = output
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to run git status.", detail = ex.Message });
            }
        }

        [HttpGet("/vannet/api/director/git/push")]
        public async Task<IActionResult> DirectorGitPush()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            const string repoPath = "/home/container/VanaNet";
            if (!Directory.Exists(repoPath))
                return NotFound(new { error = "Repo path not found.", path = repoPath });

            var psi = new ProcessStartInfo
            {
                FileName = "bash",
				Arguments = $"-c \"cd {repoPath} && git add -A && git commit -m 'Director Commit' && git push https://ada:Avoacado1!@ada.drewcotech.com/vanadium/vanadium HEAD:master\"",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                    return StatusCode(500, new { error = "Failed to start git process." });

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                string output = string.IsNullOrWhiteSpace(stderr)
                    ? stdout
                    : string.IsNullOrWhiteSpace(stdout)
                        ? stderr
                        : stdout + "\n\n[stderr]\n" + stderr;

                if (string.IsNullOrWhiteSpace(output))
                    output = "No push output.";

                return Ok(new
                {
                    success = process.ExitCode == 0,
                    exitCode = process.ExitCode,
                    output = output
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to run git push.", detail = ex.Message });
            }
        }

        private static string RRPlusPath => Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "RRPlus.txt");

        public static HashSet<long> ReadRRPlusIds()
        {
            var ids = new HashSet<long>();
            if (!System.IO.File.Exists(RRPlusPath)) return ids;
            foreach (var line in System.IO.File.ReadAllLines(RRPlusPath))
                if (long.TryParse(line.Trim(), out var id))
                    ids.Add(id);
            return ids;
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/rrplus")]
        public async Task<IActionResult> DirectorSetRRPlus(long playerId, [FromBody] DirectorRRPlusRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            bool enabled = body?.Enabled ?? true;
            var ids = ReadRRPlusIds();
            if (enabled) ids.Add(playerId);
            else ids.Remove(playerId);

            Directory.CreateDirectory(Path.GetDirectoryName(RRPlusPath)!);
            System.IO.File.WriteAllLines(RRPlusPath, ids.Select(i => i.ToString()));

            await NotificationsController.RefreshAccount(playerId);

            return Ok(new { success = true, playerId, recRoomPlus = enabled });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/set-bio")]
        public async Task<IActionResult> DirectorSetBio(long playerId, [FromBody] DirectorBioRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            player.Player.Bio = body?.Bio ?? "";
            PlayerDB.Players.Update(player);

            var updated = PlayerDB.GetAccountMe(playerId);
            await NotiController.SendAccountUpdate(playerId, updated);
            await NotificationsController.RefreshAccount(playerId);
            
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/set-password")]
        public async Task<IActionResult> DirectorSetPassword(long playerId, [FromBody] DirectorPasswordRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            PlayerDB.PasswordManager.ChangePassword(player.PlayerId, body?.Password ?? "");

            var updated = PlayerDB.GetAccountMe(playerId);
            await NotiController.SendAccountUpdate(playerId, updated);
            await NotificationsController.RefreshAccount(playerId);
            
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/upload-pfp")]
        public async Task<IActionResult> DirectorUploadPlayerPfp(long playerId, [FromForm] IFormFile file)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null) return NotFound("");

            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided." });

            string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
                return BadRequest(new { error = "Only JPG and PNG files are allowed." });

            string fileName = $"pfp_{playerId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
            string savePath = Path.Combine(Environment.CurrentDirectory, "Data", "Images", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

            using (var stream = new FileStream(savePath, FileMode.Create))
                await file.CopyToAsync(stream);

            var db = ImageMetadataDB.GetDb();
            var imageCol = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            imageCol.Insert(new ImageMetadataDB.FullSavedImage
            {
                PlayerId = (ulong)playerId,
                ImageName = fileName
            });

            player.Player.ProfileImage = fileName;
            PlayerDB.Players.Update(player);

            var updated = PlayerDB.GetAccountMe(playerId);
            await NotificationsController.RefreshAccount(playerId);

            return Ok(new { success = true, imageName = fileName });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/upload-image")]
        public async Task<IActionResult> DirectorUploadRoomImage(long roomId, [FromForm] IFormFile file)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null) return NotFound("");

            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided." });

            string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
                return BadRequest(new { error = "Only JPG and PNG files are allowed." });

            string fileName = $"room_{roomId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
            string savePath = Path.Combine(Environment.CurrentDirectory, "Data", "Images", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

            using (var stream = new FileStream(savePath, FileMode.Create))
                await file.CopyToAsync(stream);

            room.ImageName = fileName;
            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);

            return Ok(new { success = true, imageName = fileName });
        }

        [HttpPost("/vannet/api/director/players/shove-all")]
        public async Task<IActionResult> DirectorShoveAll([FromBody] DirectorShoveAllRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.GetRoom(body.RoomId);
            if (room == null) return NotFound(new { error = "Room not found." });

            var onlinePlayers = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
                .ToList();

            int count = 0;
            foreach (var fp in onlinePlayers)
            {
                var heartbeat = Sessions.CreateRoom(fp.PlayerId, body.RoomId, body.SubRoomId, joinMode: 0, additionalPlayersAutoFollow: false);
                if (!string.IsNullOrWhiteSpace(body.InstanceId) && heartbeat.roomInstance != null)
                    heartbeat.roomInstance.photonRoomId = body.InstanceId;

                fp.Player.PlayerExtra ??= new PlayerExtra();
                fp.Player.PlayerExtra.Heartbeat = heartbeat;
                PlayerDB.Players.Update(fp);
                await NotiController.SendPresenceUpdate(fp.PlayerId, heartbeat);
                count++;
            }

            return Ok(new { success = true, playersShoved = count });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/set-pfp")]
        public async Task<IActionResult> DirectorSetPfp(long playerId, [FromBody] DirectorSetPfpRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (string.IsNullOrWhiteSpace(body?.ImageName))
                return BadRequest(new { error = "ImageName required." });

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            player.Player.ProfileImage = body.ImageName;
            PlayerDB.Players.Update(player);

            var updated = PlayerDB.GetAccountMe(playerId);
            await NotificationsController.RefreshAccount(playerId);

            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/reset-pfp")]
        public async Task<IActionResult> DirectorResetPfp(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            player.Player.ProfileImage = "DefaultPFP.png";
            PlayerDB.Players.Update(player);

            var updated = PlayerDB.GetAccountMe(playerId);
            await NotificationsController.RefreshAccount(playerId);

            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/set-level")]
		public async Task<IActionResult> DirectorSetLevel(long playerId, [FromBody] DirectorSetLevelRequest body)
		{
			if (!ValidateDirectorToken(out _)) return NotFound("");
			var player = PlayerDB.Players.FindById(playerId);
			if (player == null)
				return NotFound("");
			int oldLevel = player.Player.Level;
			int oldXP = player.Player.XP;
			int newXP = body.Xp.HasValue ? body.Xp.Value : PlayerDB.GetXPForLevel(body.Level);
			player.Player.Level = body.Level;
			player.Player.XP = newXP;
			PlayerDB.Players.Update(player);
			var json = JsonSerializer.Serialize(new
			{
				Id = NotificationsController.EventTypes.PlayerProgressionLevelUpdate,
				Msg = new
				{
					PlayerId = playerId,
					Level = body.Level,
					XP = newXP
				}
			});
			await NotificationsController.SendToAll(json);
			return Ok(new { success = true, oldLevel, newLevel = body.Level, oldXP, newXP });
		}

        [HttpPost("/vannet/api/director/players/{playerId:long}/set-junior")] // removed message because it unessecary lowkey
        public async Task<IActionResult> DirectorSetJunior(long playerId, [FromBody] DirectorSetJuniorRequest body)
        {
            if (!ValidateDirectorToken(out var session)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            player.Player ??= new Player();
            player.Player.PlayerExtra ??= new PlayerExtra();
            bool wasJunior = player.Player.IsJunior == true;
            bool isJunior = body?.IsJunior ?? false;
            player.Player.IsJunior = isJunior;

            string? message = null;
            if (isJunior)
            {
                string? reason = string.IsNullOrWhiteSpace(body?.Reason) ? null : body.Reason.Trim();
                player.Player.PlayerExtra.JuniorStatus = new JuniorStatusData
                {
                    Reason = reason,
                    SetBy = session?.DiscordUserId,
                    SetAt = DateTime.UtcNow
                };

                if (!wasJunior)
                {
                    string newName = NameGen.GetRandomName();
                    player.Player.Username = newName;
                    player.Player.DisplayName = newName;
                    player.Player.Bio = "";
                }

                message = reason != null
                    ? $"Your communication privileges have been restricted: {reason}. Contact a moderator to appeal."
                    : "Your communication privileges have been restricted by a moderator. Contact a moderator to appeal.";
            }
            else
            {
                player.Player.PlayerExtra.JuniorStatus = null;
                if (wasJunior)
                    message = "Your communication privileges have been restored.";
            }

            PlayerDB.Players.Update(player);
            await NotificationsController.RefreshAccount(playerId);

            if (message != null)
            {
                // await SendCoachPopup(playerId, message);
                await NotificationsController.CreateAndSendWSMessageRecieved(
                    playerId, 1, MessageType.TextMessage, message);
            }

            return Ok(new { success = true, playerId, isJunior });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/coach-dm")]
        public async Task<IActionResult> DirectorCoachDm(long playerId, [FromBody] DirectorCoachDmRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (string.IsNullOrWhiteSpace(body?.Message))
                return BadRequest(new { error = "Message required." });

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            bool sent = await NotificationsController.CreateAndSendWSMessageRecieved(
                playerId, 1, MessageType.TextMessage, body.Message);

            return Ok(new { success = sent, playerId });
        }

        [HttpGet("/vannet/api/director/players/{playerId:long}/toxmod")]
        public async Task<IActionResult> DirectorGetToxModStatus(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            await ToxModService.EnsureCurrent(player);
            return Ok(ToxModService.BuildStatus(player));
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/toxmod/ban")]
        public async Task<IActionResult> DirectorToxModBan(long playerId, [FromBody] DirectorToxModBanRequest body)
        {
            if (!ValidateDirectorToken(out var session)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            await ToxModService.IssueVoiceBan(player, body?.Category, body?.Note, body?.DurationSeconds, session?.DiscordUserId);

            if (body?.Loud == true)
            {
                string wsEvent = Newtonsoft.Json.JsonConvert.SerializeObject(
                    Vanadium.Classes.WebSocket.WebsocketEvents.CreateWSEventString("ModerationQuitGame", new { }));
                await NotificationsController.SendToPlayer(playerId, wsEvent);
            }

            return Ok(new { success = true, status = ToxModService.BuildStatus(player) });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/toxmod/unban")]
        public async Task<IActionResult> DirectorToxModUnban(long playerId, [FromBody] DirectorToxModUnbanRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            await ToxModService.LiftVoiceBan(player, body?.ResetStrikes ?? false);
            return Ok(new { success = true, status = ToxModService.BuildStatus(player) });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/set-platform-id")]
        public IActionResult DirectorSetPlatformId(long playerId, [FromBody] DirectorSetPlatformIdRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (!ulong.TryParse(body?.PlatformId, out ulong newPid))
                return BadRequest(new { error = "Invalid platform ID." });

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            if (player.PlatformIds == null || player.PlatformIds.Count == 0)
                return BadRequest(new { error = "No platform IDs on this player." });

            string old = player.PlatformIds[0].PlatformId;
            player.PlatformIds[0].PlatformId = body.PlatformId;
            PlayerDB.Players.Update(player);

            return Ok(new { success = true, oldPlatformId = old, newPlatformId = body.PlatformId });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/heartbeat-clear")]
        public async Task<IActionResult> DirectorHeartbeatClear(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

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

            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/shove")]
        public async Task<IActionResult> DirectorShove(long playerId, [FromBody] DirectorShoveRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound(new { error = "Player not found." });

            var room = RoomDB.GetRoom(body.RoomId);
            if (room == null)
                return NotFound(new { error = "Room not found." });

            var heartbeat = Sessions.CreateRoom(playerId, body.RoomId, body.SubRoomId, joinMode: 0, additionalPlayersAutoFollow: false);

            if (!string.IsNullOrWhiteSpace(body.InstanceId) && heartbeat.roomInstance != null)
                heartbeat.roomInstance.photonRoomId = body.InstanceId;

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Heartbeat = heartbeat;
            PlayerDB.Players.Update(player);

            await NotiController.SendPresenceUpdate(playerId, heartbeat);

            return Ok(new { success = true, roomId = body.RoomId });
        }

		[HttpPost("/vannet/api/director/players/{playerId:long}/force-bring")]
		public async Task<IActionResult> DirectorForceBring(long playerId, [FromBody] DirectorForceBringRequest body)
		{
		    if (!ValidateDirectorToken(out _)) return NotFound("");

		    if (string.IsNullOrWhiteSpace(body?.Username))
		        return BadRequest(new { error = "Input field for username" });

		    var player = PlayerDB.Players.FindById(playerId);
		    if (player == null)
		        return NotFound("");

		    var target = PlayerDB.Players.FindAll()
		        .FirstOrDefault(p => string.Equals(p.Player?.Username, body.Username, StringComparison.OrdinalIgnoreCase));

		    if (target == null)
		        return NotFound(new { error = "No user found with this username!" });

		    var targetHb = target.Player?.PlayerExtra?.Heartbeat;
		    if (targetHb?.roomInstance == null)
		        return BadRequest(new { error = "This user isnt online" });

		    var heartbeat = Sessions.CreateRoom(playerId, targetHb.roomInstance.roomId, targetHb.roomInstance.subRoomId, joinMode: 0, additionalPlayersAutoFollow: false);

		    if (heartbeat.roomInstance != null)
		    {
		        heartbeat.roomInstance.photonRoomId = targetHb.roomInstance.photonRoomId;
		        heartbeat.roomInstance.photonRegion = targetHb.roomInstance.photonRegion;
		        heartbeat.roomInstance.roomInstanceId = targetHb.roomInstance.roomInstanceId;
		    }

		    player.Player.PlayerExtra ??= new PlayerExtra();
		    player.Player.PlayerExtra.Heartbeat = heartbeat;
		    PlayerDB.Players.Update(player);

            await NotificationsController.RefreshHeartbeat(playerId, heartbeat);

		    return Ok(new { success = true, roomId = targetHb.roomInstance.roomId });
		}

        [HttpPost("/vannet/api/director/players/{playerId:long}/transfer-rooms")]
        public async Task<IActionResult> DirectorTransferRooms(long playerId, [FromBody] DirectorTransferRoomsRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var oldPlayer = PlayerDB.Players.FindById(playerId);
            var newPlayer = PlayerDB.Players.FindById(body.NewPlayerId);

            if (oldPlayer == null)
                return NotFound(new { error = "Source player not found." });
            if (newPlayer == null)
                return NotFound(new { error = "Target player not found." });

            var rooms = RoomDB.Rooms.Find(r => r.CreatorAccountId == playerId).ToList();
            foreach (var room in rooms)
            {
                room.CreatorAccountId = body.NewPlayerId;
                room.Roles ??= new List<Roles>();
                room.Roles.RemoveAll(r => r.AccountId == playerId && r.Role == Role.Creator);
                room.Roles.RemoveAll(r => r.AccountId == body.NewPlayerId && r.Role == Role.Creator);
                room.Roles.Add(new Roles { AccountId = body.NewPlayerId, Role = Role.Creator, InvitedRole = Role.Creator, LastChangedByAccountId = body.NewPlayerId });
                if (body.KeepCoOwner)
                {
                    room.Roles.RemoveAll(r => r.AccountId == playerId);
                    room.Roles.Add(new Roles { AccountId = playerId, Role = Role.CoOwner, InvitedRole = Role.CoOwner, LastChangedByAccountId = body.NewPlayerId });
                }
                if (room.SubRooms != null)
                    foreach (var sub in room.SubRooms)
                    {
                        sub.CreatorAccountId = body.NewPlayerId;
                        sub.SavedByAccountId = body.NewPlayerId;
                    }
                RoomDB.Rooms.Update(room);
            }

            var onlinePlayers = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
                .Select(p => p.PlayerId)
                .ToList();
            foreach (var pid in onlinePlayers)
                foreach (var r in rooms)
                    await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(r.RoomId));

            return Ok(new { success = true, roomsTransferred = rooms.Count });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/repair")]
        public async Task<IActionResult> DirectorRepair(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            var now = DateTime.UtcNow;
            if (player.Player.CreatedAt == default || player.Player.CreatedAt.Year == 1) player.Player.CreatedAt = now;
            if (player.Player.LastLoginAt == default || player.Player.LastLoginAt.Year == 1) player.Player.LastLoginAt = now;
            player.Player.IsJunior ??= false;
            if (string.IsNullOrWhiteSpace(player.Player.ProfileImage)) player.Player.ProfileImage = "DefaultPFP.png";
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
                    if (dorm != null) player.Player.PlayerExtra.DormRoomId = dorm.RoomId;
                }
            }

            PlayerDB.Players.Update(player);
            var repaired = PlayerDB.GetAccountMe(playerId);
            await NotificationsController.RefreshAccount(playerId);
            await NotiController.SendPresenceUpdate(playerId, player.Player.PlayerExtra.Heartbeat);

            return Ok(new { success = true, dormRoomId = player.Player.PlayerExtra.DormRoomId });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/delete-all-images")]
        public IActionResult DirectorDeleteAllImages(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            var db = ImageMetadataDB.GetDb();
            var imageCol = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var images = imageCol.Query().Where(x => x.PlayerId == (ulong)playerId).ToList();

            int deletedFiles = 0;
            foreach (var image in images)
            {
                try
                {
                    string path = Path.Combine("Data", "Images", image.ImageName);
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                        deletedFiles++;
                    }
                }
                catch { }
            }

            ImageMetadataDB.DeleteImagesFromPlayerId((ulong)playerId);
            return Ok(new { success = true, deletedRecords = images.Count, deletedFiles });
        }
        
        [HttpPost("/vannet/api/director/players/{playerId:long}/delete-all-rooms")]
        public IActionResult DirectorDeleteAllRooms(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            var rooms = RoomDB.Rooms.Find(r => r.CreatorAccountId == playerId).ToList();

            int deletedSubroomSaves = 0;
            int deletedRooms = 0;

            foreach (var room in rooms)
            {
                try
                {
                    RoomDB.SubRoomSaves.DeleteMany(s => s.RoomId == room.RoomId);
                    deletedSubroomSaves++;

                    if (RoomDB.Rooms.Delete(room.RoomId))
                    {
                        deletedRooms++;
                    }
                }
                catch { }
            }

            return Ok(new { 
                success = true, 
                deletedRecords = rooms.Count,
                deletedFiles = deletedRooms,
                deletedSaves = deletedSubroomSaves 
            });
        }

        [HttpDelete("/vannet/api/director/players/{playerId:long}/delete")]
        public IActionResult DirectorDeletePlayer(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            PlayerDB.Players.Delete(playerId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/roles")]
        public async Task<IActionResult> DirectorPlayerRoles(long playerId, [FromBody] DirectorRoleRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (string.IsNullOrWhiteSpace(body?.Role) || string.IsNullOrWhiteSpace(body.Action))
                return BadRequest(new { error = "Role and action required." });

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            if (!Enum.TryParse<PlayerRoles>(body.Role, out var role))
                return BadRequest(new { error = "Invalid role." });

            player.PlayerRoles ??= new List<PlayerRoles>();

            if (body.Action == "add" && !player.PlayerRoles.Contains(role))
                player.PlayerRoles.Add(role);
            else if (body.Action == "remove")
                player.PlayerRoles.Remove(role);

            PlayerDB.Players.Update(player);
            var updated = PlayerDB.GetAccountMe(playerId);
            await NotiController.SendAccountUpdate(playerId, updated);

            return Ok(new { success = true, roles = player.PlayerRoles.Select(r => r.ToString()).ToList() });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/influencer")]
        public IActionResult DirectorPlayerInfluencer(long playerId, [FromBody] DirectorInfluencerRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            bool isAdd = body?.Action == "add";
            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Influencer ??= new Influencer();
            player.Player.PlayerExtra.Influencer.IsInfluencer = isAdd;
            PlayerDB.Players.Update(player);

            return Ok(new { success = true, isInfluencer = isAdd });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/ban")]
        public async Task<IActionResult> DirectorBanPlayer(long playerId, [FromBody] DirectorBanRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");
			if (playerId == 1305)
            	return Ok(new { success = true });
            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");
                
            var mbd = new ModerationBlockDetails
            {
                IsBan = true,
                ReportCategory = ReportCategory.Moderator,
                Duration = body?.Duration ??  2147483647,
                Message = body?.Reason ?? "Banned by admin",
                ModerationSetUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.ModerationBlockDetails = mbd;
            player.Player.PlayerExtra.Heartbeat = new Heartbeat
            {
                playerId = playerId,
                isOnline = false,
                roomInstance = null,
                errorCode = 0
            };

            PlayerDB.Players.Update(player);
            await NotiController.SendPresenceUpdate(playerId, player.Player.PlayerExtra.Heartbeat);
            await NotiController.SendAccountUpdate(playerId, PlayerDB.GetAccountMe(playerId));
            await NotificationsController.SendBanned(playerId, mbd);

            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/unban")]
        public async Task<IActionResult> DirectorUnbanPlayer(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");
			if (playerId == 1305)
            	return Ok(new { success = true });
            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.ModerationBlockDetails = new ModerationBlockDetails { IsBan = false };
            
            var mbd = new ModerationBlockDetails
            {
                IsBan = false,
                ReportCategory = ReportCategory.Moderator,
                Duration = 0,
                Message = "Unbanned",
                ModerationSetUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            
            PlayerDB.Players.Update(player);
            await NotiController.SendAccountUpdate(playerId, PlayerDB.GetAccountMe(playerId));
            await NotificationsController.SendBanned(playerId, mbd);

            return Ok(new { success = true });
        }

        /// <summary>
        /// Machines matching a hwid or an account, with everyone linked to them.
        /// <para>
        /// This is the screen a moderator is meant to read before banning anything. A machine
        /// id is shared by everyone who has played on that computer, so the linked account
        /// list is not a detail - it is the decision. Eleven accounts behind one hwid is a
        /// different call from one, and the panel puts that in front of them first.
        /// </para>
        /// </summary>
        [HttpGet("/vannet/api/director/anticheat/machines")]
        public IActionResult DirectorMachineLookup([FromQuery] string? hwid, [FromQuery] long? playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var records = new List<HwidDBClasses.HwidRecord>();

            if (!string.IsNullOrWhiteSpace(hwid))
            {
                var single = HwidDB.GetRecord(hwid.Trim());
                if (single != null)
                    records.Add(single);
            }
            else if (playerId != null)
            {
                records.AddRange(HwidDB.RecordsForPlayer(playerId.Value));
            }
            else
            {
                return BadRequest(new { error = "Provide either hwid or playerId." });
            }

            return Ok(records.Select(ProjectMachine));
        }

        /// <summary>Every machine under an active hardware ban. The review queue.</summary>
        [HttpGet("/vannet/api/director/anticheat/machines/banned")]
        public IActionResult DirectorBannedMachines()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            return Ok(HwidDB.AllBanned().Select(ProjectMachine));
        }

        /// <summary>
        /// Bans a machine, or every machine an account has been seen on, and the accounts
        /// linked to them. The Discord identity behind the Director session is recorded as
        /// the issuer: a hardware ban with no name attached is one nobody can review later.
        /// </summary>
        [HttpPost("/vannet/api/director/anticheat/machines/ban")]
        public async Task<IActionResult> DirectorBanMachine([FromBody] DirectorHwidBanRequest body)
        {
            if (!ValidateDirectorToken(out var session)) return NotFound("");

            if (body == null || string.IsNullOrWhiteSpace(body.Reason))
                return BadRequest(new { error = "A reason is required." });

            if (body.DurationDays is <= 0)
                return BadRequest(new { error = "DurationDays must be positive, or blank for permanent." });

            DateTime? expiresAt = body.DurationDays == null
                ? null
                : DateTime.UtcNow.AddDays(body.DurationDays.Value);

            string issuedBy = $"director:{session?.DiscordUserId ?? "unknown"}";

            List<string> hwids;
            List<long> accounts;

            if (!string.IsNullOrWhiteSpace(body.Hwid))
            {
                var banned = HwidDB.BanHwid(body.Hwid.Trim(), body.Reason, issuedBy, expiresAt);
                if (banned == null)
                    return NotFound(new { error = "That machine has never checked in." });

                hwids = new List<string> { body.Hwid.Trim() };
                accounts = banned;
            }
            else if (body.PlayerId != null)
            {
                (hwids, accounts) = HwidDB.BanMachinesOfPlayer(body.PlayerId.Value, body.Reason, issuedBy, expiresAt);

                if (hwids.Count == 0)
                    return NotFound(new { error = "No machines on record for that player." });
            }
            else
            {
                return BadRequest(new { error = "Provide either Hwid or PlayerId." });
            }

            // HwidDB applies the account bans; pushing them out is the Director job, so a
            // player caught by a machine ban drops now rather than at their next login.
            foreach (var id in accounts)
                await PushBanToClient(id);

            return Ok(new { success = true, hwids, bannedAccounts = accounts, expiresAt });
        }

        /// <summary>
        /// Lifts a hardware ban. The accounts banned alongside it stay banned on purpose:
        /// some were banned for their own behaviour before the machine ever came up, and
        /// sweeping those back in would undo moderation nobody asked to undo. The panel says
        /// so, and those accounts are unbanned individually from the Players screen.
        /// </summary>
        [HttpPost("/vannet/api/director/anticheat/machines/unban")]
        public IActionResult DirectorUnbanMachine([FromBody] DirectorHwidUnbanRequest body)
        {
            if (!ValidateDirectorToken(out var session)) return NotFound("");

            if (string.IsNullOrWhiteSpace(body?.Hwid))
                return BadRequest(new { error = "Hwid is required." });

            string liftedBy = $"director:{session?.DiscordUserId ?? "unknown"}";

            if (!HwidDB.UnbanHwid(body.Hwid.Trim(), liftedBy))
                return NotFound(new { error = "No active hardware ban on that machine." });

            return Ok(new { success = true });
        }

        /// <summary>
        /// Drops an already-banned account offline and tells its client why. Mirrors what
        /// DirectorBanPlayer does after writing the ban, so a machine ban and a manual ban
        /// land identically from the client side.
        /// </summary>
        private async Task PushBanToClient(long playerId)
        {
            var player = PlayerDB.Players.FindById(playerId);
            var mbd = player?.Player?.PlayerExtra?.ModerationBlockDetails;

            if (player == null || mbd == null)
                return;

            player.Player.PlayerExtra.Heartbeat = new Heartbeat
            {
                playerId = playerId,
                isOnline = false,
                roomInstance = null,
                errorCode = 0
            };

            PlayerDB.Players.Update(player);

            await NotiController.SendPresenceUpdate(playerId, player.Player.PlayerExtra.Heartbeat);
            await NotiController.SendAccountUpdate(playerId, PlayerDB.GetAccountMe(playerId));
            await NotificationsController.SendBanned(playerId, mbd);
        }

        /// <summary>
        /// Shapes one machine for the panel. Resolves linked accounts to names and ban state
        /// here rather than making the browser fan out one request per player.
        /// </summary>
        private static object ProjectMachine(HwidDBClasses.HwidRecord record)
        {
            var now = DateTime.UtcNow;

            return new
            {
                hwid = record.Hwid,
                firstSeen = record.FirstSeen,
                lastSeen = record.LastSeen,
                banned = record.BanIsActive(now),
                banReason = record.BanReason,
                bannedBy = record.BannedBy,
                bannedAt = record.BannedAt,
                banExpiresAt = record.BanExpiresAt,
                players = (record.PlayerIds ?? new List<long>()).Select(id =>
                {
                    var p = PlayerDB.Players.FindById(id);
                    return new
                    {
                        playerId = id,
                        username = p?.Player?.Username,
                        profileImage = p?.Player?.ProfileImage,
                        banned = p?.Player?.PlayerExtra?.ModerationBlockDetails?.IsBan == true
                    };
                }).ToList()
            };
        }

		[HttpPost("/vannet/api/director/players/close-all")] // does not apply to devs
		public async Task<IActionResult> DirectorCrashAll()
		{
    		if (!ValidateDirectorToken(out _)) return NotFound("");

    		var onlinePlayers = PlayerDB.Players.FindAll()
        		.Where(p =>
            		p.Player?.PlayerExtra?.Heartbeat?.isOnline == true &&
            		(p.PlayerRoles == null || !p.PlayerRoles.Contains(PlayerRoles.Developer)))
        		.ToList();

    		int count = 0;
    		foreach (var fp in onlinePlayers)
    		{
        		string wsEvent = Newtonsoft.Json.JsonConvert.SerializeObject(
            		Vanadium.Classes.WebSocket.WebsocketEvents.CreateWSEventString("ModerationQuitGame", new { }));
            	await NotificationsController.SendToPlayer(fp.PlayerId, wsEvent);
        		count++;
    		}

    		return Ok(new { success = true, playersCrashed = count });
		}

        [HttpPost("/vannet/api/director/players/{playerId:long}/close-game")]
        public async Task<IActionResult> DirectorCloseGame(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            string wsEvent = Newtonsoft.Json.JsonConvert.SerializeObject(
                Vanadium.Classes.WebSocket.WebsocketEvents.CreateWSEventString("ModerationQuitGame", new { }));// damn, this from the ancient days from sscs
            await NotificationsController.SendToPlayer(playerId, wsEvent);

            return Ok(new { success = true });
        }
        
        [HttpPost("/vannet/api/director/players/{playerId:long}/logout")]
		public async Task<IActionResult> DirectorLogoutPlayer(long playerId)
		{
		    if (!ValidateDirectorToken(out _)) return NotFound("");
		
		    var player = PlayerDB.Players.FindById(playerId);
		    if (player == null)
		        return NotFound("");
		
		    string wsEvent = Newtonsoft.Json.JsonConvert.SerializeObject(
		        Vanadium.Classes.WebSocket.WebsocketEvents.CreateLogoutResponse());
		    await NotificationsController.SendToPlayer(playerId, wsEvent);

		    return Ok(new { success = true });
		}

        [HttpPost("/vannet/api/director/players/{playerId:long}/send-ws-hex")]
        public async Task<IActionResult> DirectorSendPlayerWsHex(long playerId, [FromBody] DirectorSendWsHexRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            var hex = body?.Hex ?? string.Empty;
            hex = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray());
            hex = hex.Replace("0x", "", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(hex))
                return BadRequest(new { error = "Hex payload is required." });

            if (hex.Length % 2 != 0)
                return BadRequest(new { error = "Hex payload must have an even number of characters." });

            if (hex.Any(c => !Uri.IsHexDigit(c)))
                return BadRequest(new { error = "Hex payload contains invalid characters." });

            byte[] payload;
            try
            {
                payload = Convert.FromHexString(hex);
            }
            catch
            {
                return BadRequest(new { error = "Hex payload could not be parsed." });
            }

            if (body?.Exact == true)
            {
                await NotificationsController.SendRawToPlayer(playerId, payload);
            }
            else
            {
                await NotificationsController.SendToPlayer(playerId, Encoding.UTF8.GetString(payload));
            }

            return Ok(new { success = true, bytes = payload.Length, exact = body?.Exact == true });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/send-ws-json")]
        public async Task<IActionResult> DirectorSendPlayerWsJson(long playerId, [FromBody] DirectorSendWsJsonRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            var json = body?.Json ?? string.Empty;

            if (string.IsNullOrWhiteSpace(json))
                return BadRequest(new { error = "JSON payload is required." });

            try
            {
                using var _ = JsonDocument.Parse(json);
            }
            catch
            {
                return BadRequest(new { error = "Payload is not valid JSON." });
            }

            await NotificationsController.SendToPlayer(playerId, json);

            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/send-coach-message")]
        public async Task<IActionResult> DirectorPlayerCoachMessage(long playerId, [FromBody] DirectorPlayerCoachMessageRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            await SendCoachPopup(playerId, body?.Message ?? "");

            return Ok(new { success = true });
        }

        internal static async Task SendCoachPopup(long playerId, string message)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                Id = "2",
                Msg = new
                {
                    FromPlayerId = 1,
                    Id = new Random().Next(1, 0x7ffffff),
                    SentTime = DateTime.UtcNow,
                    Type = 100,
                    Data = message,
                    RoomId = 1
                }
            });
            await NotificationsController.SendToPlayer(playerId, json);
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/set-tokens")]
        public async Task<IActionResult> DirectorSetTokens(long playerId, [FromBody] DirectorSetTokensRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Currencies ??= new List<PlayerCurrency>();

            var balanceType = (BalanceType)(body?.BalanceType ?? 0);
            var existing = player.Player.PlayerExtra.Currencies
                .FirstOrDefault(c => c.CurrencyType == CurrencyType.RecCenterTokens && c.BalanceType == balanceType);

            int old = 0;
            if (existing != null)
            {
                old = existing.Balance;
                existing.Balance = body.Amount;
            }
            else
            {
                player.Player.PlayerExtra.Currencies.Add(new PlayerCurrency
                {
                    CurrencyType = CurrencyType.RecCenterTokens,
                    BalanceType = balanceType,
                    Balance = body.Amount
                });
            }

            PlayerDB.Players.Update(player);
            await NotiController.SendAccountUpdate(playerId, PlayerDB.GetAccountMe(playerId));

            return Ok(new { success = true, oldBalance = old, newBalance = body.Amount });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/give-skin")]
        public IActionResult DirectorGiveSkin(long playerId, [FromBody] DirectorGiveSkinRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player == null)
                return NotFound("");

            string equipPath = Path.Join(Program.dataDir, "APIS", "Items", "Equipment.json");
            if (!System.IO.File.Exists(equipPath))
                return NotFound(new { error = "Equipment.json not found." });

            var allItems = System.Text.Json.JsonSerializer.Deserialize<List<OwnedEquipmentItem>>(
                System.IO.File.ReadAllText(equipPath),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var target = allItems?.FirstOrDefault(i =>
                string.Equals(i.FriendlyName, body?.Skin, StringComparison.OrdinalIgnoreCase));

            if (target == null)
                return NotFound(new { error = "Skin not found." });

            player.Player.PlayerExtra ??= new PlayerExtra();
            if (player != null && player.Player != null && player.Player.PlayerExtra != null && player.Player.PlayerExtra.OwnedEquipment == null)
            {
                player.Player.PlayerExtra.OwnedEquipment = new List<OwnedEquipmentItem>();
            }

            bool owns = player.Player.PlayerExtra.OwnedEquipment
                .Any(e => string.Equals(e.ModificationGuid, target.ModificationGuid, StringComparison.OrdinalIgnoreCase));

            if (body.Action == "add")
            {
                if (owns)
                    return Conflict(new { error = "Player already owns this skin." });
                player.Player.PlayerExtra.OwnedEquipment.Add(new OwnedEquipmentItem
                {
                    PrefabName = target.PrefabName,
                    ModificationGuid = target.ModificationGuid,
                    FriendlyName = target.FriendlyName,
                    PlatformMask = target.PlatformMask,
                    Tooltip = target.Tooltip,
                    Rarity = target.Rarity,
                    Favorited = false
                });
            }
            else
            {
                if (!owns)
                    return NotFound(new { error = "Player does not own this skin." });
                player.Player.PlayerExtra.OwnedEquipment.RemoveAll(e =>
                    string.Equals(e.ModificationGuid, target.ModificationGuid, StringComparison.OrdinalIgnoreCase));
            }

            PlayerDB.Players.Update(player);
            return Ok(new { success = true });
        }

        [HttpGet("/vannet/api/director/skins")]
        public IActionResult DirectorGetSkins()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            string path = Path.Join(Program.dataDir, "APIS", "Items", "Equipment.json");
            if (!System.IO.File.Exists(path))
                return Ok(new List<object>());
            var items = System.Text.Json.JsonSerializer.Deserialize<List<OwnedEquipmentItem>>(
                System.IO.File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return Ok(items ?? new List<OwnedEquipmentItem>());
        }

        [HttpGet("/vannet/api/director/rooms/hot")]
        public IActionResult DirectorHotRooms([FromQuery] int skip = 0, [FromQuery] int take = 50)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var rooms = RoomDB.Rooms.FindAll()
                .Where(r => !r.IsDorm)
                .OrderByDescending(r => r.Stats.CheerCount)
                .Skip(skip)
                .Take(take)
                .Select(r => BuildRoomSummary(r))
                .ToList();

            return Ok(new { Results = rooms, TotalResults = rooms.Count });
        }

        [HttpGet("/vannet/api/director/rooms/search")]
        public IActionResult DirectorSearchRooms([FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int take = 50)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            IEnumerable<Room> query = RoomDB.Rooms.FindAll().Where(r => !r.IsDorm);

            if (!string.IsNullOrWhiteSpace(q))
            {
                string lower = q.ToLowerInvariant();
                bool isId = long.TryParse(q, out long searchId);
                query = query.Where(r =>
                    (isId && r.RoomId == searchId) ||
                    (r.Name != null && r.Name.ToLower().Contains(lower)) ||
                    (r.Name != null && r.Name.ToLower().Contains(lower)));
            }

            var results = query
                .OrderByDescending(r => r.Stats.VisitCount)
                .Skip(skip)
                .Take(take)
                .Select(r => BuildRoomSummary(r))
                .ToList();

            return Ok(new { Results = results, TotalResults = results.Count });
        }

        [HttpGet("/vannet/api/director/rooms/{roomId:long}")]
        public IActionResult DirectorGetRoom(long roomId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound("");

            var creator = PlayerDB.Players.FindById(room.CreatorAccountId);

            return Ok(new
            {
                roomId = room.RoomId,
                name = room.Name,
                description = room.Description,
                imageName = room.ImageName,
                creatorAccountId = room.CreatorAccountId,
                creatorUsername = creator?.Player?.Username,
                stats = room.Stats,
                maxPlayers = room.MaxPlayers,
                ageRating = room.AgeRating,
                createdAt = room.CreatedAt,
                publishedAt = room.PublishedAt,
                tags = room.Tags,
                subRooms = room.SubRooms,
                roles = room.Roles,
                supportsMobile = room.SupportsMobile,
                supportsQuest2 = room.SupportsQuest2,
                supportsWalkVR = room.SupportsWalkVR,
                supportsTeleportVR = room.SupportsTeleportVR,
                supportsScreens = room.SupportsScreens,
                supportsJuniors = room.SupportsJuniors,
                accessibility = room.Accessibility,
                state = room.State,
                cloningAllowed = room.CloningAllowed,
                isRRO = room.IsRRO,
                isDorm = room.IsDorm,
                loadScreens = room.LoadScreens,
                promoImages = room.PromoImages,
                warningMask = room.WarningMask
            });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-name")]
        public async Task<IActionResult> DirectorSetRoomName(long roomId, [FromBody] DirectorRoomNameRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (string.IsNullOrWhiteSpace(body?.Name))
                return BadRequest(new { error = "Name required." });

            var room = RoomDB.SetRoomName(roomId, body.Name);
            if (room == null)
                return NotFound("");

            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-description")]
        public async Task<IActionResult> DirectorSetRoomDescription(long roomId, [FromBody] DirectorRoomDescRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.SetRoomDescription(roomId, body?.Description ?? "");
            if (room == null)
                return NotFound("");

            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-image")]
        public async Task<IActionResult> DirectorSetRoomImage(long roomId, [FromBody] DirectorRoomImageRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (string.IsNullOrWhiteSpace(body?.ImageName))
                return BadRequest(new { error = "ImageName required." });

            var room = RoomDB.SetRoomImageName(roomId, body.ImageName);
            if (room == null)
                return NotFound("");

            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/reset-image")]
        public async Task<IActionResult> DirectorResetRoomImage(long roomId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound("");

            room.ImageName = "DefaultRoomImage.jpg";
            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-accessibility")]
        public async Task<IActionResult> DirectorSetRoomAccessibility(long roomId, [FromBody] DirectorRoomAccessibilityRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.SetRoomAccessibility(roomId, (RoomAccessibility)body.Accessibility);
            if (room == null)
                return NotFound("");

            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-cloning")]
        public async Task<IActionResult> DirectorSetRoomCloning(long roomId, [FromBody] DirectorRoomCloningRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.SetRoomCloning(roomId, body.Allowed);
            if (room == null)
                return NotFound("");

            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-restrictions")]
        public async Task<IActionResult> DirectorSetRoomRestrictions(long roomId, [FromBody] DirectorRoomRestrictionsRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound("");

            room.SupportsScreens = body.SupportsScreens;
            room.SupportsWalkVR = body.SupportsWalkVR;
            room.SupportsTeleportVR = body.SupportsTeleportVR;
            room.SupportsJuniors = body.SupportsJuniors;
            room.SupportsMobile = body.SupportsMobile;
            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-age-rating")]
        public async Task<IActionResult> DirectorSetRoomAgeRating(long roomId, [FromBody] DirectorRoomAgeRatingRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound("");

            room.AgeRating = body.AgeRating;
            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-max-players")]
        public async Task<IActionResult> DirectorSetRoomMaxPlayers(long roomId, [FromBody] DirectorRoomMaxPlayersRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (body.MaxPlayers < 1)
                return BadRequest(new { error = "MaxPlayers must be at least 1." });

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound("");

            room.MaxPlayers = body.MaxPlayers;
            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/change-owner")]
        public async Task<IActionResult> DirectorChangeRoomOwner(long roomId, [FromBody] DirectorChangeOwnerRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            var newOwner = PlayerDB.Players.FindById(body.NewOwnerId);

            if (room == null) return NotFound(new { error = "Room not found." });
            if (newOwner == null) return NotFound(new { error = "New owner not found." });

            room.CreatorAccountId = body.NewOwnerId;
            room.Roles ??= new List<Roles>();
            room.Roles.RemoveAll(r => r.Role == Role.Creator);
            room.Roles.Add(new Roles { AccountId = body.NewOwnerId, Role = Role.Creator, InvitedRole = Role.Creator, LastChangedByAccountId = body.NewOwnerId });

            if (room.SubRooms != null)
                foreach (var sub in room.SubRooms)
                {
                    sub.CreatorAccountId = body.NewOwnerId;
                    sub.SavedByAccountId = body.NewOwnerId;
                }

            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-role")]
        public async Task<IActionResult> DirectorSetRoomRole(long roomId, [FromBody] DirectorSetRoomRoleRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound("");

            room.Roles ??= new List<Roles>();
            room.Roles.RemoveAll(r => r.AccountId == body.PlayerId);

            var newRole = (Role)body.Role;
            if (newRole != Role.None)
                room.Roles.Add(new Roles { AccountId = body.PlayerId, Role = newRole, InvitedRole = Role.None, LastChangedByAccountId = 2 });

            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);

            if (newRole == Role.CoOwner || newRole == Role.TemporaryCoOwner)
            {
                var coachJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    Id = "2",
                    Msg = new
                    {
                        FromPlayerId = body.PlayerId,
                        Id = Random.Shared.Next(1, 0x7ffffff),
                        SentTime = DateTime.UtcNow,
                        Type = Vanadium.Classes.DBs.DBClasses.PlayerDBClasses.MessageType.CoachMessage,
                        Data = $"You were given Co-Owner inside ^{room.Name}",
                        RoomId = roomId,
                        PlayerEventId = (long?)null
                    }
                });
                await NotificationsController.SendToPlayer(body.PlayerId, coachJson);
            }

            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/toggle-beta")]
        public async Task<IActionResult> DirectorToggleBeta(long roomId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound("");

            room.Tags ??= new List<Tags>();
            var betaTag = room.Tags.FirstOrDefault(t => t.Tag.Equals("beta", StringComparison.OrdinalIgnoreCase) && (int)t.Type == 1);

            bool added;
            if (betaTag != null)
            {
                room.Tags.Remove(betaTag);
                added = false;
            }
            else
            {
                room.Tags.Add(new Tags { Tag = "beta", Type = (TagType)1 });
                added = true;
            }

            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true, betaEnabled = added });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/tags")]
        public async Task<IActionResult> DirectorRoomTags(long roomId, [FromBody] DirectorRoomTagsRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound("");

            room.Tags ??= new List<Tags>();

            foreach (var tag in body.Tags ?? new List<string>())
            {
                string t = tag.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(t)) continue;

                if (body.Action == "add")
                {
                    if (!room.Tags.Any(x => x.Tag.Equals(t, StringComparison.OrdinalIgnoreCase)))
                        room.Tags.Add(new Tags { Tag = t, Type = (TagType)(body.Type ?? 0) });
                }
                else
                {
                    room.Tags.RemoveAll(x => x.Tag.Equals(t, StringComparison.OrdinalIgnoreCase));
                }
            }

            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true, tags = room.Tags.Select(t => new { tag = t.Tag, type = (int)t.Type }).ToList() });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/change-datablob")]
        public async Task<IActionResult> DirectorChangeDatablob(long roomId, [FromBody] DirectorChangeDatablobRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound(new { error = "Room not found." });

            var subRoom = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == body.SubRoomId);
            if (subRoom == null)
                return NotFound(new { error = "SubRoom not found." });

            string datablob = Path.GetFileName(body.Datablob);
            string cdnFolder = Path.Combine(Environment.CurrentDirectory, "Data", "cdn");
            //Directory.CreateDirectory(cdnFolder);
            string localPath = Path.Combine(cdnFolder, datablob);

            string downloadStatus = "Already cached";
            if (!System.IO.File.Exists(localPath))
            {
                try
                {
                    using var http = new HttpClient();
                    http.DefaultRequestHeaders.Add("User-Agent", "BestHTTP");
                    http.DefaultRequestHeaders.Add("Accept", "*/*");
                    http.DefaultRequestHeaders.Add("Referer", "https://rec.net/");
                    var response = await http.GetAsync($"https://cdn.rec.net/room/{datablob}");
                    if (response.IsSuccessStatusCode)
                    {
                        await System.IO.File.WriteAllBytesAsync(localPath, await response.Content.ReadAsByteArrayAsync());
                        downloadStatus = "Downloaded from RecNet CDN";
                    }
                    else downloadStatus = $"Failed HTTP {(int)response.StatusCode}";
                }
                catch (Exception ex) { downloadStatus = "Download error: " + ex.Message; }
            }

            var newSave = new currentSave
            {
                SubRoomDataSaveId = RoomDB.GetNextSubRoomSaveId(),
                RoomId = roomId,
                SubRoomId = body.SubRoomId,
                DataBlob = datablob,
                Description = $"Imported via Director Panel",
                SavedByAccountId = 1,
                SavedOnPlatform = 0,
                SavedOnDeviceClass = 1,
                CreatedAt = DateTime.UtcNow
            };
            RoomDB.SubRoomSaves.Insert(newSave);

            subRoom.DataBlob = datablob;
            subRoom.CurrentSave = newSave;
            subRoom.Accessibility = RoomAccessibility.Public;
            RoomDB.Rooms.Update(room);

            await NotifyRoomUpdate(roomId);

            return Ok(new { success = true, saveId = newSave.SubRoomDataSaveId, downloadStatus });
        }

        [HttpGet("/vannet/api/director/rooms/{roomId:long}/instances")]
        public IActionResult DirectorGetRoomInstances(long roomId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound("");

            var live = PlayerDB.Players.FindAll()
                .Where(p =>
                    p.Player?.PlayerExtra?.Heartbeat?.isOnline == true &&
                    p.Player.PlayerExtra.Heartbeat.roomInstance != null &&
                    p.Player.PlayerExtra.Heartbeat.roomInstance.roomId == roomId)
                .Select(p => p.Player!.PlayerExtra!.Heartbeat!.roomInstance!)
                .ToList();

            var subRooms = (room.SubRooms ?? new List<SubRooms>()).Select(sub =>
            {
                var mine = live.Where(ri => ri.subRoomId == sub.SubRoomId).ToList();
                return new
                {
                    subRoomId = sub.SubRoomId,
                    publicInstances = mine.Where(ri => !ri.isPrivate).Select(ri => ri.roomInstanceId).Distinct().Count(),
                    publicPlayers = mine.Count(ri => !ri.isPrivate),
                    privateInstances = mine.Where(ri => ri.isPrivate).Select(ri => ri.roomInstanceId).Distinct().Count(),
                    privatePlayers = mine.Count(ri => ri.isPrivate)
                };
            }).ToList();

            return Ok(new { roomId, subRooms });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/refresh-instances")]
        public async Task<IActionResult> DirectorRefreshInstances(long roomId, [FromBody] DirectorRefreshInstancesRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(new { error = "Room not found." });

            var subRoom = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == body.SubRoomId);
            if (subRoom == null)
                return NotFound(new { error = "SubRoom not found." });

            var (instancesRefreshed, playersMoved) = await Sessions.RefreshPublicInstances(roomId, body.SubRoomId);

            return Ok(new { success = true, instancesRefreshed, playersMoved });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-instances-privacy")]
        public async Task<IActionResult> DirectorSetInstancesPrivacy(long roomId, [FromBody] DirectorSetInstancesPrivacyRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(new { error = "Room not found." });

            var subRoom = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == body.SubRoomId);
            if (subRoom == null)
                return NotFound(new { error = "SubRoom not found." });

            var instancesChanged = await Sessions.SetInstancesPrivacy(roomId, body.SubRoomId, body.IsPrivate);

            return Ok(new { success = true, instancesChanged, isPrivate = body.IsPrivate });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/upload-datablob")]
		public async Task<IActionResult> DirectorUploadDatablob(long roomId, [FromForm] long subRoomId, [FromForm] IFormFile file)
		{
				if (!ValidateDirectorToken(out _)) return NotFound("");

				var room = RoomDB.Rooms.FindById(roomId);
				if (room == null)
						return NotFound(new { error = "Room not found." });

				var subRoom = room.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
				if (subRoom == null)
						return NotFound(new { error = "SubRoom not found." });

				if (file == null || file.Length == 0)
						return BadRequest(new { error = "No file provided." });

				string fileName = Path.GetFileName(file.FileName);
				string cdnFolder = Path.Combine(Environment.CurrentDirectory, "Data", "cdn", "room");
				Directory.CreateDirectory(cdnFolder);
				string savePath = Path.Combine(cdnFolder, fileName);

				using (var stream = new FileStream(savePath, FileMode.Create))
						await file.CopyToAsync(stream);

				var newSave = new currentSave
				{
						SubRoomDataSaveId = RoomDB.GetNextSubRoomSaveId(),
						RoomId = roomId,
						SubRoomId = subRoomId,
						DataBlob = fileName,
						Description = "Imported by Coach!",
						SavedByAccountId = 1,
						SavedOnPlatform = 0,
						SavedOnDeviceClass = 1,
						CreatedAt = DateTime.UtcNow
				};

				RoomDB.SubRoomSaves.Insert(newSave);

				subRoom.DataBlob = fileName;
				subRoom.CurrentSave = newSave;
				subRoom.Accessibility = RoomAccessibility.Public;
				RoomDB.Rooms.Update(room);

				await NotifyRoomUpdate(roomId);

				return Ok(new { success = true, saveId = newSave.SubRoomDataSaveId, fileName });
		}

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-room-type")]
        public async Task<IActionResult> DirectorSetRoomType(long roomId, [FromBody] DirectorRoomCloningRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound("");

            if (body.Allowed)
                room.UgcVersion = 2;
            else
                room.UgcVersion = 1;
                
            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/{roomId:long}/set-modern-persistence")]
        public async Task<IActionResult> DirectorSetModernPersistence(long roomId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound("");

            room.PersistenceVersion = 203;
            room.UgcSubVersion = 367;
            room.MinUgcSubVersion = 367;
            for (int i = 0; i < room.SubRooms.Count; i++)
            {
                var subRoom = room.SubRooms[i].CurrentSave;
                if (subRoom == null) continue;
                subRoom.PersistenceVersion = 203;
                subRoom.OMVersion = 164;
                subRoom.UgcSubVersion = 367;
            }
            RoomDB.Rooms.Update(room);
            await NotifyRoomUpdate(roomId);
            return Ok(new { success = true });
        }

        [HttpDelete("/vannet/api/director/rooms/{roomId:long}/delete")]
        public async Task<IActionResult> DirectorDeleteRoom(long roomId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound("");

            RoomDB.DeleteRoom(roomId);

            var onlinePlayers = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
                .Select(p => p.PlayerId)
                .ToList();
            foreach (var pid in onlinePlayers)
                await NotiController.SendRoomUpdate(pid, new { roomId, deleted = true });

            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/rooms/import-epicquest")] // experimental 2
        public async Task<IActionResult> DirectorImportRecconnedRoom([FromQuery] string? name)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { error = "The name is required" });

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "BestHTTP");
            HttpResponseMessage meowResp;
            try
            {
                meowResp = await http.GetAsync($"https://recconned.gabethefirst.com/roomserver/rooms?name={Uri.EscapeDataString(name)}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"error at recconned.gabethefirst.com: {ex.Message}" });
            }

            if (!meowResp.IsSuccessStatusCode)
                return StatusCode((int)meowResp.StatusCode, new { error = $"recconned.gabethefirst.com returned {(int)meowResp.StatusCode}" });
            JsonElement meow;
            try
            {
                meow = JsonSerializer.Deserialize<JsonElement>(await meowResp.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"error parsing epicquest response {ex.Message}" });
            }

            string meowName = meow.TryGetProperty("Name", out var nProp) ? nProp.GetString() ?? name : name;

            string resolvedName = meowName;
            if (RoomDB.GetRoomByName(resolvedName) != null)
            {
                const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
                var rand = new Random();
                char[] seg = new char[4];
                string RandomSeg() { for (int i = 0; i < 4; i++) seg[i] = chars[rand.Next(chars.Length)]; return new string(seg); }
                resolvedName = $"{RandomSeg()}-{RandomSeg()}-{RandomSeg()}-{RandomSeg()}";
            }

            string imageServerPath = Path.Combine(Environment.CurrentDirectory, "Data", "Images");
            string cdnRoomPath = Path.Combine(Environment.CurrentDirectory, "Data", "cdn", "room");
            Directory.CreateDirectory(imageServerPath);
            Directory.CreateDirectory(cdnRoomPath);

            string localImageName = "DefaultRoomImage.jpg";
            if (meow.TryGetProperty("ImageName", out var imgProp) && !string.IsNullOrWhiteSpace(imgProp.GetString()))
            {
                string remoteImageName = imgProp.GetString()!;
                string localImageDest = Path.Combine(imageServerPath, remoteImageName);
                if (!System.IO.File.Exists(localImageDest))
                {
                    try
                    {
                        using var imgClient = new HttpClient();
                        imgClient.DefaultRequestHeaders.Add("User-Agent", "BestHTTP");
                        var imgResp = await imgClient.GetAsync($"https://eq-img.gabethefirst.com/{remoteImageName}");
                        if (imgResp.IsSuccessStatusCode)
                            await System.IO.File.WriteAllBytesAsync(localImageDest, await imgResp.Content.ReadAsByteArrayAsync());
                    }
                    catch { }
                }
                if (System.IO.File.Exists(localImageDest))
                    localImageName = remoteImageName;
            }

            WarningMaskType warningMask = default;
            if (meow.TryGetProperty("WarningMask", out var wmProp))
            {
                try { warningMask = (WarningMaskType)wmProp.GetInt32(); } catch { }
            }

            var newRoom = new Room
            {
                Name = resolvedName,
                Description = (meow.TryGetProperty("Description", out var descProp) ? descProp.GetString() ?? "" : "") + " (Imported from EpicQuest)",
                ImageName = localImageName,
                CreatorAccountId = 2,
                Accessibility = RoomAccessibility.Public,
                CloningAllowed = true,
                State = RoomState.Active,
                IsDorm = false,
                IsRRO = false,
                IsDeveloperOwned = meow.TryGetProperty("IsDeveloperOwned", out var idoProp) && idoProp.GetBoolean(),
                ToxmodEnabled = false,
                AutoLocalizeRoom = meow.TryGetProperty("AutoLocalizeRoom", out var alProp) && alProp.GetBoolean(),
                LoadScreenLocked = meow.TryGetProperty("LoadScreenLocked", out var lslProp) && lslProp.GetBoolean(),
                MaxPlayerCalculationMode = meow.TryGetProperty("MaxPlayerCalculationMode", out var mpcmProp) ? mpcmProp.GetInt32() : 1,
                MaxPlayers = meow.TryGetProperty("MaxPlayers", out var mpProp) ? mpProp.GetInt32() : 20,
                MinLevel = meow.TryGetProperty("MinLevel", out var mlProp) ? mlProp.GetInt32() : 0,
                PersistenceVersion = meow.TryGetProperty("PersistenceVersion", out var pvProp) ? pvProp.GetInt32() : 2,
                UgcVersion = 1,
                DisableMicAutoMute = meow.TryGetProperty("DisableMicAutoMute", out var dmamProp) && dmamProp.GetBoolean(),
                DisableRoomComments = meow.TryGetProperty("DisableRoomComments", out var drcProp) && drcProp.GetBoolean(),
                EncryptVoiceChat = meow.TryGetProperty("EncryptVoiceChat", out var evcProp) && evcProp.GetBoolean(),
                SupportsJuniors = meow.TryGetProperty("SupportsJuniors", out var sjProp) && sjProp.GetBoolean(),
                SupportsLevelVoting = meow.TryGetProperty("SupportsLevelVoting", out var slvProp) && slvProp.GetBoolean(),
                SupportsMobile = meow.TryGetProperty("SupportsMobile", out var smProp) && smProp.GetBoolean(),
                SupportsQuest2 = meow.TryGetProperty("SupportsQuest2", out var sq2Prop) && sq2Prop.GetBoolean(),
                SupportsScreens = meow.TryGetProperty("SupportsScreens", out var ssProp) && ssProp.GetBoolean(),
                SupportsTeleportVR = meow.TryGetProperty("SupportsTeleportVR", out var stvrProp) && stvrProp.GetBoolean(),
                SupportsVRLow = meow.TryGetProperty("SupportsVRLow", out var svrlProp) && svrlProp.GetBoolean(),
                SupportsWalkVR = meow.TryGetProperty("SupportsWalkVR", out var swvrProp) && swvrProp.GetBoolean(),
                WarningMask = warningMask,
                CustomWarning = meow.TryGetProperty("CustomWarning", out var cwProp) ? cwProp.GetString() : null,
                CreatedAt = DateTime.UtcNow,
                PublishedAt = DateTime.UtcNow,
                Stats = new Stats { CheerCount = 0, FavoriteCount = 0, VisitorCount = 0, VisitCount = 0 },
                Roles = new List<Roles>
                {
                    new Roles { AccountId = 2, Role = Role.Creator, InvitedRole = Role.None }
                },
                Tags = new List<Tags>(),
                PromoImages = new List<string>(),
                PromoExternalContent = new List<PromoExternalContent>(),
                LoadScreens = new List<LoadScreens>(),
                SubRooms = new List<SubRooms>()
            };

            if (meow.TryGetProperty("Tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tagsProp.EnumerateArray())
                {
                    int tagType = 0;
                    if (t.TryGetProperty("Type", out var ttProp)) tagType = ttProp.GetInt32();
                    if (tagType != 0) continue;
                    string? tagVal = t.TryGetProperty("Tag", out var tvProp) ? tvProp.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(tagVal))
                        newRoom.Tags.Add(new Tags { Tag = tagVal.ToLowerInvariant(), Type = TagType.General });
                }
            }

            newRoom.Tags.Add(new Tags { Tag = "imported", Type = (TagType)2 });

            long newRoomId = RoomDB.AddRoomSync(newRoom, shouldAssignNewIds: true);
            var insertedRoom = RoomDB.GetRoom(newRoomId);
            if (insertedRoom == null)
                return StatusCode(500, new { error = "made room but couldnt get data" });

            var importLog = new List<object>();

            if (meow.TryGetProperty("SubRooms", out var subRoomsProp) && subRoomsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var sr in subRoomsProp.EnumerateArray())
                {
                    string? srDataBlob = sr.TryGetProperty("DataBlob", out var srDbProp) ? srDbProp.GetString() : null;
                    string? srName = sr.TryGetProperty("Name", out var srNameProp) ? srNameProp.GetString() : null;
                    int srAccessibility = sr.TryGetProperty("Accessibility", out var srAccProp) ? srAccProp.GetInt32() : 0;
                    bool srIsSandbox = sr.TryGetProperty("IsSandbox", out var srIsSbProp) && srIsSbProp.GetBoolean();
                    int srMaxPlayers = sr.TryGetProperty("MaxPlayers", out var srMpProp) ? srMpProp.GetInt32() : 20;
                    string srUnitySceneId = sr.TryGetProperty("UnitySceneId", out var srUsProp) ? srUsProp.GetString() ?? "a75f7547-79eb-47c6-8986-6767abcb4f92" : "a75f7547-79eb-47c6-8986-6767abcb4f92";

                    string? localDataBlob = null;
                    if (!string.IsNullOrWhiteSpace(srDataBlob))
                    {
                        string blobFileName = srDataBlob;
                        string blobDest = Path.Combine(cdnRoomPath, blobFileName);
                        if (!System.IO.File.Exists(blobDest))
                        {
                            try
                            {
                                using var blobClient = new HttpClient();
                                blobClient.DefaultRequestHeaders.Add("User-Agent", "BestHTTP");
                                var blobResp = await blobClient.GetAsync($"https://eq-cdn.gabethefirst.com/room/{blobFileName}");
                                if (blobResp.IsSuccessStatusCode)
                                {
                                    await System.IO.File.WriteAllBytesAsync(blobDest, await blobResp.Content.ReadAsByteArrayAsync());
                                    localDataBlob = blobFileName;
                                }
                                else
                                {
                                    importLog.Add(new { subRoom = srName, status = "blob_download_failed", code = (int)blobResp.StatusCode });
                                }
                            }
                            catch (Exception ex)
                            {
                                importLog.Add(new { subRoom = srName, status = "blob_download_error", error = ex.Message });
                            }
                        }
                        else
                        {
                            localDataBlob = blobFileName;
                        }
                    }

                    long newSubRoomId = RoomDB.GetNextSubRoomId();

                    var newSub = new SubRooms
                    {
                        SubRoomId = newSubRoomId,
                        RoomId = newRoomId,
                        Name = srName ?? "SubRoom",
                        Accessibility = (RoomAccessibility)srAccessibility,
                        IsSandbox = srIsSandbox,
                        MaxPlayers = srMaxPlayers,
                        UnitySceneId = srUnitySceneId,
                        DataBlob = localDataBlob,
                        SavedByAccountId = 1806,
                        CreatorAccountId = 1806,
                        ShouldAutoStageSaves = false,
                        LastModeratedSaveModerationState = 0
                    };

                    currentSave? newSave = null;
                    if (localDataBlob != null)
                    {
                        newSave = new currentSave
                        {
                            SubRoomDataSaveId = RoomDB.GetNextSubRoomSaveId(),
                            RoomId = newRoomId,
                            SubRoomId = newSubRoomId,
                            DataBlob = localDataBlob,
                            Description = $"Imported from EpicQuest Recconned",
                            SavedByAccountId = 1806,
                            SavedOnPlatform = 0,
                            SavedOnDeviceClass = 1,
                            PersistenceVersion = 0,
                            UgcSubVersion = 0,
                            OMVersion = 0,
                            Tags = new List<string>(),
                            ModerationState = 0,
                            CreatedAt = DateTime.UtcNow
                        };
                        RoomDB.SubRoomSaves.Insert(newSave);
                        newSub.CurrentSave = newSave;
                    }

                    insertedRoom.SubRooms.Add(newSub);
                    importLog.Add(new { subRoom = srName, subRoomId = newSubRoomId, status = "imported", blob = localDataBlob });
                }

                RoomDB.Rooms.Update(insertedRoom);
            }

            var onlinePlayers = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
                .Select(p => p.PlayerId)
                .ToList();
            foreach (var pid in onlinePlayers)
                await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(newRoomId));

            return Ok(new { success = true, roomId = newRoomId, name = resolvedName, subRoomsImported = importLog.Count, log = importLog });
        }

        [HttpPost("/vannet/api/director/rooms/import-meownet")] // experimental
        public async Task<IActionResult> DirectorImportMeowiiRoom([FromQuery] string? name)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { error = "The name is required" });

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "BestHTTP");
            http.DefaultRequestHeaders.Add("key", "c0f5eff84baf6fc808f98a8a9813748053deb3b0ce1978d08ab275e54e08dcda");

            HttpResponseMessage meowResp;
            try
            {
                meowResp = await http.GetAsync($"https://live.meowii.app/rooms?name={Uri.EscapeDataString(name)}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"error at meowii.app: {ex.Message}" });
            }

            if (!meowResp.IsSuccessStatusCode)
                return StatusCode((int)meowResp.StatusCode, new { error = $"meowii.app returned {(int)meowResp.StatusCode}" });

            JsonElement meow;
            try
            {
                meow = JsonSerializer.Deserialize<JsonElement>(await meowResp.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"error parsing meowii response {ex.Message}" });
            }

            string meowName = meow.TryGetProperty("Name", out var nProp) ? nProp.GetString() ?? name : name;

            string resolvedName = meowName;
            if (RoomDB.GetRoomByName(resolvedName) != null)
            {
                const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
                var rand = new Random();
                char[] seg = new char[4];
                string RandomSeg() { for (int i = 0; i < 4; i++) seg[i] = chars[rand.Next(chars.Length)]; return new string(seg); }
                resolvedName = $"{RandomSeg()}-{RandomSeg()}-{RandomSeg()}-{RandomSeg()}";
            }

            string imageServerPath = Path.Combine(Environment.CurrentDirectory, "Data", "Images");
            string cdnRoomPath = Path.Combine(Environment.CurrentDirectory, "Data", "cdn", "room");
            Directory.CreateDirectory(imageServerPath);
            Directory.CreateDirectory(cdnRoomPath);

            string localImageName = "DefaultRoomImage.jpg";
            if (meow.TryGetProperty("ImageName", out var imgProp) && !string.IsNullOrWhiteSpace(imgProp.GetString()))
            {
                string remoteImageName = imgProp.GetString()!;
                string localImageDest = Path.Combine(imageServerPath, remoteImageName);
                if (!System.IO.File.Exists(localImageDest))
                {
                    try
                    {
                        using var imgClient = new HttpClient();
                        imgClient.DefaultRequestHeaders.Add("User-Agent", "BestHTTP");
                        imgClient.DefaultRequestHeaders.Add("key", "c0f5eff84baf6fc808f98a8a9813748053deb3b0ce1978d08ab275e54e08dcda");
                        var imgResp = await imgClient.GetAsync($"https://cdn.cookedasset.com/{remoteImageName}");
                        if (imgResp.IsSuccessStatusCode)
                            await System.IO.File.WriteAllBytesAsync(localImageDest, await imgResp.Content.ReadAsByteArrayAsync());
                    }
                    catch { }
                }
                if (System.IO.File.Exists(localImageDest))
                    localImageName = remoteImageName;
            }

            WarningMaskType warningMask = default;
            if (meow.TryGetProperty("WarningMask", out var wmProp))
            {
                try { warningMask = (WarningMaskType)wmProp.GetInt32(); } catch { }
            }

            var newRoom = new Room
            {
                Name = resolvedName,
                Description = (meow.TryGetProperty("Description", out var descProp) ? descProp.GetString() ?? "" : "") + " (Imported from MeowNet)",
                ImageName = localImageName,
                CreatorAccountId = 1806,
                Accessibility = RoomAccessibility.Public,
                CloningAllowed = true,
                State = RoomState.Active,
                IsDorm = false,
                IsRRO = false,
                IsDeveloperOwned = meow.TryGetProperty("IsDeveloperOwned", out var idoProp) && idoProp.GetBoolean(),
                ToxmodEnabled = false,
                AutoLocalizeRoom = meow.TryGetProperty("AutoLocalizeRoom", out var alProp) && alProp.GetBoolean(),
                LoadScreenLocked = meow.TryGetProperty("LoadScreenLocked", out var lslProp) && lslProp.GetBoolean(),
                MaxPlayerCalculationMode = meow.TryGetProperty("MaxPlayerCalculationMode", out var mpcmProp) ? mpcmProp.GetInt32() : 1,
                MaxPlayers = meow.TryGetProperty("MaxPlayers", out var mpProp) ? mpProp.GetInt32() : 20,
                MinLevel = meow.TryGetProperty("MinLevel", out var mlProp) ? mlProp.GetInt32() : 0,
                PersistenceVersion = meow.TryGetProperty("PersistenceVersion", out var pvProp) ? pvProp.GetInt32() : 2,
                UgcVersion = meow.TryGetProperty("UgcVersion", out var uvProp) ? uvProp.GetInt32() : 12,
                DisableMicAutoMute = meow.TryGetProperty("DisableMicAutoMute", out var dmamProp) && dmamProp.GetBoolean(),
                DisableRoomComments = meow.TryGetProperty("DisableRoomComments", out var drcProp) && drcProp.GetBoolean(),
                EncryptVoiceChat = meow.TryGetProperty("EncryptVoiceChat", out var evcProp) && evcProp.GetBoolean(),
                SupportsJuniors = meow.TryGetProperty("SupportsJuniors", out var sjProp) && sjProp.GetBoolean(),
                SupportsLevelVoting = meow.TryGetProperty("SupportsLevelVoting", out var slvProp) && slvProp.GetBoolean(),
                SupportsMobile = meow.TryGetProperty("SupportsMobile", out var smProp) && smProp.GetBoolean(),
                SupportsQuest2 = meow.TryGetProperty("SupportsQuest2", out var sq2Prop) && sq2Prop.GetBoolean(),
                SupportsScreens = meow.TryGetProperty("SupportsScreens", out var ssProp) && ssProp.GetBoolean(),
                SupportsTeleportVR = meow.TryGetProperty("SupportsTeleportVR", out var stvrProp) && stvrProp.GetBoolean(),
                SupportsVRLow = meow.TryGetProperty("SupportsVRLow", out var svrlProp) && svrlProp.GetBoolean(),
                SupportsWalkVR = meow.TryGetProperty("SupportsWalkVR", out var swvrProp) && swvrProp.GetBoolean(),
                WarningMask = warningMask,
                CustomWarning = meow.TryGetProperty("CustomWarning", out var cwProp) ? cwProp.GetString() : null,
                CreatedAt = DateTime.UtcNow,
                PublishedAt = DateTime.UtcNow,
                Stats = new Stats { CheerCount = 0, FavoriteCount = 0, VisitorCount = 0, VisitCount = 0 },
                Roles = new List<Roles>
                {
                    new Roles { AccountId = 1806, Role = Role.Creator, InvitedRole = Role.None }
                },
                Tags = new List<Tags>(),
                PromoImages = new List<string>(),
                PromoExternalContent = new List<PromoExternalContent>(),
                LoadScreens = new List<LoadScreens>(),
                SubRooms = new List<SubRooms>()
            };

            if (meow.TryGetProperty("Tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tagsProp.EnumerateArray())
                {
                    int tagType = 0;
                    if (t.TryGetProperty("Type", out var ttProp)) tagType = ttProp.GetInt32();
                    if (tagType != 0) continue;
                    string? tagVal = t.TryGetProperty("Tag", out var tvProp) ? tvProp.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(tagVal))
                        newRoom.Tags.Add(new Tags { Tag = tagVal.ToLowerInvariant(), Type = TagType.General });
                }
            }

            newRoom.Tags.Add(new Tags { Tag = "imported", Type = (TagType)2 });

            long newRoomId = RoomDB.AddRoomSync(newRoom, shouldAssignNewIds: true);
            var insertedRoom = RoomDB.GetRoom(newRoomId);
            if (insertedRoom == null)
                return StatusCode(500, new { error = "made room but couldnt get data" });

            var importLog = new List<object>();

            if (meow.TryGetProperty("SubRooms", out var subRoomsProp) && subRoomsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var sr in subRoomsProp.EnumerateArray())
                {
                    string? srDataBlob = sr.TryGetProperty("DataBlob", out var srDbProp) ? srDbProp.GetString() : null;
                    string? srName = sr.TryGetProperty("Name", out var srNameProp) ? srNameProp.GetString() : null;
                    int srAccessibility = sr.TryGetProperty("Accessibility", out var srAccProp) ? srAccProp.GetInt32() : 0;
                    bool srIsSandbox = sr.TryGetProperty("IsSandbox", out var srIsSbProp) && srIsSbProp.GetBoolean();
                    int srMaxPlayers = sr.TryGetProperty("MaxPlayers", out var srMpProp) ? srMpProp.GetInt32() : 20;
                    string srUnitySceneId = sr.TryGetProperty("UnitySceneId", out var srUsProp) ? srUsProp.GetString() ?? "a75f7547-79eb-47c6-8986-6767abcb4f92" : "a75f7547-79eb-47c6-8986-6767abcb4f92";

                    string? localDataBlob = null;
                    if (!string.IsNullOrWhiteSpace(srDataBlob))
                    {
                        string blobFileName = srDataBlob;
                        string blobDest = Path.Combine(cdnRoomPath, blobFileName);
                        if (!System.IO.File.Exists(blobDest))
                        {
                            try
                            {
                                using var blobClient = new HttpClient();
                                blobClient.DefaultRequestHeaders.Add("User-Agent", "BestHTTP");
                                blobClient.DefaultRequestHeaders.Add("key", "c0f5eff84baf6fc808f98a8a9813748053deb3b0ce1978d08ab275e54e08dcda");
                                var blobResp = await blobClient.GetAsync($"https://cdn.cookedasset.com/room/{blobFileName}");
                                if (blobResp.IsSuccessStatusCode)
                                {
                                    await System.IO.File.WriteAllBytesAsync(blobDest, await blobResp.Content.ReadAsByteArrayAsync());
                                    localDataBlob = blobFileName;
                                }
                                else
                                {
                                    importLog.Add(new { subRoom = srName, status = "blob_download_failed", code = (int)blobResp.StatusCode });
                                }
                            }
                            catch (Exception ex)
                            {
                                importLog.Add(new { subRoom = srName, status = "blob_download_error", error = ex.Message });
                            }
                        }
                        else
                        {
                            localDataBlob = blobFileName;
                        }
                    }

                    long newSubRoomId = RoomDB.GetNextSubRoomId();

                    var newSub = new SubRooms
                    {
                        SubRoomId = newSubRoomId,
                        RoomId = newRoomId,
                        Name = srName ?? "SubRoom",
                        Accessibility = (RoomAccessibility)srAccessibility,
                        IsSandbox = srIsSandbox,
                        MaxPlayers = srMaxPlayers,
                        UnitySceneId = srUnitySceneId,
                        DataBlob = localDataBlob,
                        SavedByAccountId = 1806,
                        CreatorAccountId = 1806,
                        ShouldAutoStageSaves = false,
                        LastModeratedSaveModerationState = 0
                    };

                    currentSave? newSave = null;
                    if (localDataBlob != null)
                    {
                        newSave = new currentSave
                        {
                            SubRoomDataSaveId = RoomDB.GetNextSubRoomSaveId(),
                            RoomId = newRoomId,
                            SubRoomId = newSubRoomId,
                            DataBlob = localDataBlob,
                            Description = $"Imported from meowii.app",
                            SavedByAccountId = 1806,
                            SavedOnPlatform = 0,
                            SavedOnDeviceClass = 1,
                            PersistenceVersion = 0,
                            UgcSubVersion = 0,
                            OMVersion = 0,
                            Tags = new List<string>(),
                            ModerationState = 0,
                            CreatedAt = DateTime.UtcNow
                        };
                        RoomDB.SubRoomSaves.Insert(newSave);
                        newSub.CurrentSave = newSave;
                    }

                    insertedRoom.SubRooms.Add(newSub);
                    importLog.Add(new { subRoom = srName, subRoomId = newSubRoomId, status = "imported", blob = localDataBlob });
                }

                RoomDB.Rooms.Update(insertedRoom);
            }

            var onlinePlayers = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
                .Select(p => p.PlayerId)
                .ToList();
            foreach (var pid in onlinePlayers)
                await NotiController.SendRoomUpdate(pid, RoomDB.GetRoom(newRoomId));

            return Ok(new { success = true, roomId = newRoomId, name = resolvedName, subRoomsImported = importLog.Count, log = importLog });
        }

        [HttpPost("/vannet/api/director/players/gift-all-tokens")]
		public async Task<IActionResult> DirectorGiftAllTokens([FromBody] DirectorGiftAllTokensRequest body)
		{
				if (!ValidateDirectorToken(out _)) return NotFound("");
				if (body == null || body.Amount <= 0)
						return BadRequest(new { error = "Amount must be anything above 0." });

				var allPlayers = PlayerDB.Players.FindAll().ToList();
				int count = 0;

				foreach (var fp in allPlayers)
				{
						var gift = GiftsDB.CreateGift(
								toPlayerId: fp.PlayerId,
								fromPlayerId: 1,
								currency: body.Amount,
								currencyType: 2,
								balanceType: -2,
								giftContext: GiftType.GameRewards_Tokens,
								giftRarity: 50,
								message: string.IsNullOrWhiteSpace(body.Message) ? "A gift for you <3" : body.Message,
								platform: -1,
								platformsToSpawnOn: -1
						);

						await NotiController.SendEvent(fp.PlayerId, "31", GiftsDB.MapToDTO(gift));
						count++;
				}

				return Ok(new { success = true, playersGifted = count });
		}

        [HttpGet("/vannet/api/director/images/player/{playerId}")]
        public IActionResult DirectorGetPlayerImages(long playerId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var db = ImageMetadataDB.GetDb();
            var col = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var images = col.Query().Where(x => x.PlayerId == (ulong)playerId).ToList()
                .Select(x => new
                {
                    savedImageId = x.Id,
                    playerId = x.PlayerId,
                    imageName = x.ImageName,
                    accessibility = (int)x.Accessibility,
                    accessibilityLocked = x.AccessibilityLocked,
                    cheerCount = x.CheerCount,
                    commentCount = x.CommentCount,
                    createdAt = x.CreatedAt,
                    description = x.Description,
                    type = (int)x.Type,
                    devLocked = x.DevLocked
                }).ToList();
            return Ok(images);
        }

        [HttpGet("/vannet/api/director/images/global")]
        public IActionResult DirectorGetGlobalImages()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var images = ImageMetadataDB.GetGlobalImages();
            return Ok(images);
        }

        [HttpGet("/vannet/api/director/images/{imageId:int}")]
        public IActionResult DirectorGetImageById(int imageId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var db = ImageMetadataDB.GetDb();
            var col = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var img = col.FindById(imageId);
            if (img == null) return NotFound(new { error = "Image not found." });

            return Ok(img);
        }

        [HttpPost("/vannet/api/director/images/{imageId:int}/set-accessibility")]
        public IActionResult DirectorSetImageAccessibility(int imageId, [FromBody] DirectorImageAccessibilityRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var db = ImageMetadataDB.GetDb();
            var col = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var img = col.FindById(imageId);
            if (img == null) return NotFound(new { error = "Image not found." });

            img.Accessibility = (ImageMetadataDB.ImageAccessibility)body.Accessibility;
            col.Update(img);

            return Ok(new { success = true, accessibility = img.Accessibility });
        }

        [HttpPost("/vannet/api/director/images/{imageId:int}/set-accessibility-lock")]
        public IActionResult DirectorSetImageAccessibilityLock(int imageId, [FromBody] DirectorImageLockRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var db = ImageMetadataDB.GetDb();
            var col = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var img = col.FindById(imageId);
            if (img == null) return NotFound(new { error = "Image not found." });

            img.AccessibilityLocked = body.Locked;
            col.Update(img);

            return Ok(new { success = true, accessibilityLocked = img.AccessibilityLocked });
        }

        [HttpPost("/vannet/api/director/images/{imageId:int}/set-description")]
        public IActionResult DirectorSetImageDescription(int imageId, [FromBody] DirectorImageDescriptionRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var db = ImageMetadataDB.GetDb();
            var col = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var img = col.FindById(imageId);
            if (img == null) return NotFound(new { error = "Image not found." });

            img.Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim();
            col.Update(img);

            return Ok(new { success = true, description = img.Description });
        }

        [HttpPost("/vannet/api/director/images/{imageId:int}/reset-cheers")]
        public IActionResult DirectorResetImageCheers(int imageId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            /*var db = ImageMetadataDB.GetDb();
            var imgCol = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var cheerCol = db.GetCollection<ImageMetadataDB.Cheer>("cheers");

            var img = imgCol.FindById(imageId);
            if (img == null) return NotFound(new { error = "Image not found." });

            cheerCol.DeleteMany(c => c.SavedImageId == imageId);
            img.CheerCount = 0;
            imgCol.Update(img);*/

            return NotFound(new { error = "This has been disabled" });
        }

        [HttpPost("/vannet/api/director/images/{imageId:int}/delete")]
        public IActionResult DirectorDeleteImage(int imageId)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var db = ImageMetadataDB.GetDb();
            var imgCol = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var cheerCol = db.GetCollection<ImageMetadataDB.Cheer>("cheers");

            var img = imgCol.FindById(imageId);
            if (img == null) return NotFound(new { error = "Image not found." });

            var filePath = Path.Combine("Data", "Images", img.PlayerId.ToString(), img.ImageName);
            bool fileDeleted = false;
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
                fileDeleted = true;
            }

            cheerCol.DeleteMany(c => c.SavedImageId == imageId);
            imgCol.Delete(imageId);

            return Ok(new { success = true, fileDeleted });
        }

        [HttpPost("/vannet/api/director/images/{imageId:int}/set-dev-lock")]
        public IActionResult DirectorSetImageDevLock(int imageId, [FromBody] DirectorImageDevLockRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var db = ImageMetadataDB.GetDb();
            var col = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var img = col.FindById(imageId);
            if (img == null) return NotFound(new { error = "Image not found." });

            img.DevLocked = body.DevLocked;
            col.Update(img);

            return Ok(new { success = true, devLocked = img.DevLocked });
        }

        [HttpPost("/vannet/api/director/images/{imageId:int}/transfer")]
        public IActionResult DirectorTransferImage(int imageId, [FromBody] DirectorImageTransferRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var db = ImageMetadataDB.GetDb();
            var col = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var img = col.FindById(imageId);
            if (img == null) return NotFound(new { error = "Image not found." });

            var newOwner = PlayerDB.Players.FindById(body.NewPlayerId);
            if (newOwner == null) return NotFound(new { error = "Target player not found." });

            img.PlayerId = (ulong)body.NewPlayerId;
            col.Update(img);

            return Ok(new { success = true, imageId, newPlayerId = body.NewPlayerId });
        }

        public class DirectorImageAccessibilityRequest { public int Accessibility { get; set; } }
        public class DirectorImageLockRequest { public bool Locked { get; set; } }
        public class DirectorImageDescriptionRequest { public string? Description { get; set; } }
        public class DirectorImageDevLockRequest { public bool DevLocked { get; set; } }
        public class DirectorImageTransferRequest { public long NewPlayerId { get; set; } }

        [HttpGet("/vannet/apis/api/images/v3/feed/global")]
        public IActionResult GetGlobalImagesPublic([FromQuery] int skip = 0, [FromQuery] int take = 10, [FromQuery] string? since = null)
        {
            DateTime? parsedSince = null;

            if (!string.IsNullOrWhiteSpace(since))
            {
                if (!DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var sinceDto))
                    return BadRequest();

                parsedSince = sinceDto.UtcDateTime.AddDays(-1);
            }

            return Ok(ImageMetadataDB.GetGlobalImagesPublic(skip, take, parsedSince));
        }

        [HttpGet("/vannet/api/director/db/info")]
        public IActionResult DirectorDbInfo()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            return Ok(new
            {
                players = PlayerDB.Players.Count(),
                rooms = RoomDB.Rooms.Count(),
                saves = RoomDB.SubRoomSaves.Count(),
                inventions = InventionDB.Inventions.Count()
            });
        }

        [HttpPost("/vannet/api/director/db/execute")]
        public async Task<IActionResult> DirectorDbExecute([FromBody] DirectorDbExecRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            if (string.IsNullOrWhiteSpace(body?.Expression))
                return BadRequest(new { error = "Expression required." });

            string expression = body.Expression.Trim();
            object? result = null;
            string? error = null;

            try
            {
                var setRoomMatch = System.Text.RegularExpressions.Regex.Match(expression,
                    @"^RoomDB\.Rooms\.Set\((\d+),\s*(\w+),\s*(.*)\)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                var setPlayerMatch = System.Text.RegularExpressions.Regex.Match(expression,
                    @"^PlayerDB\.Players\.Set\((\d+),\s*(\w+),\s*(.*)\)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (setRoomMatch.Success)
                {
                    long rid = long.Parse(setRoomMatch.Groups[1].Value);
                    string field = setRoomMatch.Groups[2].Value;
                    string rawValue = setRoomMatch.Groups[3].Value.Trim().Trim('"');
                    var room = RoomDB.Rooms.FindOne(r => r.RoomId == rid);
                    if (room == null) { error = $"Room {rid} not found."; }
                    else
                    {
                        var prop = typeof(Room).GetProperty(field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                        if (prop == null) { error = $"Room has no property '{field}'."; }
                        else
                        {
                            object? converted = prop.PropertyType.IsEnum
                                ? Enum.Parse(prop.PropertyType, rawValue, true)
                                : Convert.ChangeType(rawValue, prop.PropertyType);
                            string oldVal = prop.GetValue(room)?.ToString() ?? "null";
                            prop.SetValue(room, converted);
                            RoomDB.Rooms.Update(room);
                            await NotifyRoomUpdate(rid);
                            result = new { updated = true, roomId = rid, field, oldValue = oldVal, newValue = rawValue };
                        }
                    }
                }
                else if (setPlayerMatch.Success)
                {
                    long pid = long.Parse(setPlayerMatch.Groups[1].Value);
                    string field = setPlayerMatch.Groups[2].Value;
                    string rawValue = setPlayerMatch.Groups[3].Value.Trim().Trim('"');
                    var player = PlayerDB.Players.FindById(pid);
                    if (player == null) { error = $"Player {pid} not found."; }
                    else
                    {
                        var prop = typeof(Player).GetProperty(field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                        if (prop == null) { error = $"Player has no property '{field}'."; }
                        else
                        {
                            object? converted = prop.PropertyType.IsEnum
                                ? Enum.Parse(prop.PropertyType, rawValue, true)
                                : Convert.ChangeType(rawValue, prop.PropertyType);
                            string oldVal = prop.GetValue(player.Player)?.ToString() ?? "null";
                            prop.SetValue(player.Player, converted);
                            PlayerDB.Players.Update(player);
                            await NotiController.SendAccountUpdate(pid, PlayerDB.GetAccountMe(pid));
                            result = new { updated = true, playerId = pid, field, oldValue = oldVal, newValue = rawValue };
                        }
                    }
                }
                else if (expression.StartsWith("RoomDB.Rooms.Count()", StringComparison.OrdinalIgnoreCase))
                    result = RoomDB.Rooms.Count();
                else if (expression.StartsWith("RoomDB.Rooms.FindAll()", StringComparison.OrdinalIgnoreCase))
                    result = RoomDB.Rooms.FindAll().Select(r => new { roomId = r.RoomId, name = r.Name }).ToList();
                else if (expression.StartsWith("RoomDB.SubRoomSaves.Count()", StringComparison.OrdinalIgnoreCase))
                    result = RoomDB.SubRoomSaves.Count();
                else if (expression.StartsWith("PlayerDB.Players.Count()", StringComparison.OrdinalIgnoreCase))
                    result = PlayerDB.Players.Count();
                else if (expression.StartsWith("PlayerDB.Players.FindAll()", StringComparison.OrdinalIgnoreCase))
                    result = PlayerDB.Players.FindAll().Select(p => new { playerId = p.PlayerId, username = p.Player?.Username, displayName = p.Player?.DisplayName }).ToList();
                else if (expression.StartsWith("reset artixsu dorm room", StringComparison.OrdinalIgnoreCase))
                {
                    var player = PlayerDB.Players.FindById(577);
                    if (player?.Player?.PlayerExtra != null)
                    {
                        player.Player.PlayerExtra.DormRoomId = 4298;
                        PlayerDB.Players.Update(player);
                        result = true;
                    }
                    else
                    {
                        result = false;
                    }
                }
                else if (expression.StartsWith("GetLogins With Platform ID: ", StringComparison.OrdinalIgnoreCase))
                {
                    var platformIdStr = expression.Substring("GetLogins With Platform ID: ".Length);
                    if (long.TryParse(platformIdStr, out long platformId))
                    {
                        result = PlayerDB.Players.FindAll()
                            .Where(p => p.PlatformIds != null &&
                                        p.PlatformIds.Any(pid => pid.Platform == 0 && pid.PlatformId == platformIdStr))
                            .OrderByDescending(x => x.Player.LastLoginAt)
                            .ToList();
                    }
                    else
                    {
                        error = "Invalid platform ID.";
                    }
                }
                else if (expression.StartsWith("PlayerDB.ResetAllHeartbeats()", StringComparison.OrdinalIgnoreCase))
                {
                    PlayerDB.ResetAllHeartbeats();
                    result = true;
                }
                else if (expression.StartsWith("LeaderboardDB.ClearRoomLeaderboard(4974)", StringComparison.OrdinalIgnoreCase))
                    result = LeaderboardDB.ClearRoomLeaderboard(4974);
                else if (expression.StartsWith("Add everyone to group chat 61", StringComparison.OrdinalIgnoreCase))
                {
                    var chat = ChatDB.GetThread(61);
                    foreach (var player in PlayerDB.Players.FindAll())
                    {
                        if (player.Player != null)
                        {
                            Console.WriteLine($"[Director] Adding player {player.PlayerId} ({player.Player.Username}) to chat thread 61.");
                            if (!chat.PlayerIds.Any(m => m == player.PlayerId))
                            {
                                ChatDB.AddMemberToThread(61, 1, player.PlayerId);
                                Console.Write("Done");
                            }
                        }
                    }
                    result = true;
                }
                else if (expression.StartsWith("Clear group chat 61", StringComparison.OrdinalIgnoreCase))
                {
                    var thread = ChatDB.GetThread(61);
                    thread.Messages.Clear();
                    ChatDB.ChatDBFile.GetCollection<ChatDBClasses.ChatThread>("threads").Update(thread);
                    result = true;
                }
                else if (expression.StartsWith("Erase all player ids from group chat 61", StringComparison.OrdinalIgnoreCase))
                {
                    var thread = ChatDB.GetThread(61);
                    thread.PlayerIds.Clear();
                    ChatDB.ChatDBFile.GetCollection<ChatDBClasses.ChatThread>("threads").Update(thread);
                    result = true;
                }
                else if (expression.StartsWith("Delete 49 data", StringComparison.OrdinalIgnoreCase))
                {
                    PlayerDB.SetPlayerData(49, 181, "");
                    PlayerDB.SetPlayerData(49, 4974, "");
                    result = true;
                }
                else if (expression.StartsWith("Delete 786 data", StringComparison.OrdinalIgnoreCase))
                {
                    PlayerDB.SetPlayerData(786, 181, "");
                    PlayerDB.SetPlayerData(786, 4974, "");
                    result = true;
                }
                else if (expression.StartsWith("publish my invention", StringComparison.OrdinalIgnoreCase))
                {
                    var invention = InventionDB.Inventions.FindOne(i => i.InventionId == 5012352472715840770);
                    invention.Accessibility = RoomAccessibility.Public;
                    invention.GeneralPermission = 20;
                    invention.IsAGInvention = true;
                    invention.IsCertifiedInvention = true;
                    invention.IsPublished = true;
                    invention.IsFeatured = true;
                    invention.IsRecRoomApproved = true;
                    InventionDB.Inventions.Update(invention);
                    result = true;
                }
                else if (expression.StartsWith("GetInventionBlobNameByInventionName:", StringComparison.OrdinalIgnoreCase))
                {
                    expression = expression.Substring("GetInventionBlobNameByInventionName:".Length).Trim();
                    var invention = InventionDB.Inventions.FindOne(i => i.Name.Equals(expression, StringComparison.OrdinalIgnoreCase));
                    if (invention != null)
                        result = invention.CurrentVersion.BlobName;
                    else
                        result = false;
                }
                else if (expression.StartsWith("Get 2340 avatar", StringComparison.OrdinalIgnoreCase))
                {
                    var player = PlayerDB.Players.FindById(2672);
                    player.Player.PlayerExtra.Avatar = PlayerDB.Players.FindById(2340)?.Player.PlayerExtra.Avatar;
                    PlayerDB.Players.Update(player);
                    result = player.Player.PlayerExtra?.Avatar;
                }
                else if (expression.StartsWith("playersettings 2300", StringComparison.OrdinalIgnoreCase))
                    result = PlayerDB.Players.FindById(1904800648)?.Player?.PlayerExtra?.Settings;
                else if (expression.StartsWith("Backfill HAS_COMPLETED_ORIENTATION for unfinished accounts", StringComparison.OrdinalIgnoreCase))
                {
                    var updatedPlayerIds = new List<long>();
                    foreach (var player in PlayerDB.Players.FindAll().ToList())
                    {
                        var settings = PlayerDB.SettingsPlayers.FindById(player.PlayerId)?.Player?.PlayerExtra?.Settings
                            ?? player.Player?.PlayerExtra?.Settings;
                        if (settings == null) continue;

                        var hasFinished = settings.FirstOrDefault(s => s.Key == "Recroom.AccountCreation.HasFinished");
                        Console.WriteLine($"[Director] Player {player.PlayerId} ({player.Player?.Username}) has HAS_COMPLETED_ORIENTATION: {hasFinished?.Value ?? "null"}");
                        if (player.PlayerId > 1186)
                        {
                            Console.Write($"[Director] Backfilling HAS_COMPLETED_ORIENTATION for player {player.PlayerId} ({player.Player?.Username})");
                            PlayerDB.SetPlayerSetting("HAS_COMPLETED_ORIENTATION", "True", player.PlayerId);
                        PlayerDB.SetPlayerSetting("OrientationCompletionTime", "2025-07-09T21%3A52%3A18.8129095Z", player.PlayerId);
                        updatedPlayerIds.Add(player.PlayerId);
                        Console.WriteLine(" - Done");
                        }
                    }
                    result = new { updatedCount = updatedPlayerIds.Count, playerIds = updatedPlayerIds };
                }
                else if (expression.StartsWith("backfill new", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var player in PlayerDB.Players.FindAll().ToList())
                    {
                        if (player.Player != null)
                        {
                            var id = player.PlayerId;
                            Console.WriteLine($"[Director] Setting player {id} ({player.Player.Username}) setting.");
                            PlayerDB.SetPlayerSetting("Recroom.AccountCreation.HasFinished", "False", id);
                            PlayerDB.SetPlayerSetting("Recroom.AccountCreation.HasCreatedPassword", "False", id);
                            PlayerDB.SetPlayerSetting("HAS_COMPLETED_ORIENTATION", "True", id);
                            PlayerDB.SetPlayerSetting("OrientationCompletionTime", "2025-07-09T21%3A52%3A18.8129095Z", id);
                            if (player.Player.Username.Length >= 4 && char.IsDigit(player.Player.Username[player.Player.Username.Length - 4]) && 
                                char.IsDigit(player.Player.Username[player.Player.Username.Length - 3]) && 
                                char.IsDigit(player.Player.Username[player.Player.Username.Length - 2]) && 
                                char.IsDigit(player.Player.Username[player.Player.Username.Length - 1]))
                                PlayerDB.SetPlayerSetting("Recroom.AccountCreation.HasChosenUsername", "False", id);
                            else
                                PlayerDB.SetPlayerSetting("Recroom.AccountCreation.HasChosenUsername", "True", id);
                            Console.WriteLine($"[Director] Player {id} ({player.Player.Username}) settings updated.");
                        }
                    }
                    result = true;
                }
                else if (expression.StartsWith("list players 100000+ tokens", StringComparison.OrdinalIgnoreCase))
                {
                    var richPlayers = new List<object>();
                    foreach (var player in PlayerDB.Players.FindAll().ToList())
                    {
                        var tokenBalance = player.Player?.PlayerExtra?.Currencies?
                            .FirstOrDefault(c => c.CurrencyType == CurrencyType.RecCenterTokens && c.BalanceType == BalanceType.NonPurchasedDefault)?.Balance ?? 0;

                        if (tokenBalance >= 100000)
                        {
                            richPlayers.Add(new
                            {
                                PlayerId = player.PlayerId,
                                Username = player.Player?.Username,
                                Tokens = tokenBalance
                            });
                        }
                    }
                    result = richPlayers;
                }
                else
                {
                    var ridMatch = System.Text.RegularExpressions.Regex.Match(expression, @"RoomDB\.Rooms\.FindOne.*?(\d+)");
                    var pidMatch = System.Text.RegularExpressions.Regex.Match(expression, @"PlayerDB\.Players\.FindOne.*?(\d+)");
                    if (ridMatch.Success && long.TryParse(ridMatch.Groups[1].Value, out long rid))
                        result = RoomDB.GetRoom(rid);
                    else if (pidMatch.Success && long.TryParse(pidMatch.Groups[1].Value, out long pid))
                        result = PlayerDB.Players.FindById(pid);
                    else
                        error = "Unrecognised expression pattern.";
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error != null)
                return BadRequest(new { error });

            return Ok(new { result });
        }

        [HttpPost("/vannet/api/director/rooms/clear")]
        public async Task<IActionResult> DirectorClearRooms()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            //await RoomDB.ClearRooms();
            //await RoomDB.ImportRooms(Path.Join(Environment.CurrentDirectory, "Data", "Imports", "ImportRooms.json"));
            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/db/reset")]
        public async Task<IActionResult> DirectorDbReset()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            //await RoomDB.ClearRooms();
            //PlayerDB.Players.DeleteAll();
            //InventionDB.Inventions.DeleteAll();
            //FriendsDB.FriendsDBFile.GetCollection<FriendsDBClasses.FriendEntry>("Friends").DeleteAll();
            //EventDB.Events.DeleteAll();
            //await RoomDB.ImportRooms(Path.Join(Environment.CurrentDirectory, "Data", "Imports", "ImportRooms.json"));
            return Ok(new { success = true });
        }
        
        [HttpPost("/vannet/api/director/coach/send-msg")]
        public async Task<IActionResult> SendCoachMessage([FromBody] DirectorCoachMessageRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");
            await NotiController.SendCoahMessage(body?.Message ?? "");
            return Ok(new { success = true });
        }

        private static readonly object MaintenanceConfigLock = new(); // i might not have a brain gentlemen but i have an IDEA

        [HttpPost("/vannet/api/director/maintenance/schedule")]
        public async Task<IActionResult> DirectorScheduleMaintenance([FromBody] DirectorMaintenanceRequest body)
        {
            if (!ValidateDirectorToken(out var session)) return NotFound("");

            var minutes = Math.Clamp(body?.Minutes ?? 10, 0, 1440);
            var path = Path.Join(Program.dataDir, "APIS", "ConfigV2.json");

            try
            {
                lock (MaintenanceConfigLock)
                {
                    if (!System.IO.File.Exists(path))
                        return NotFound(new { error = "ConfigV2.json not found." });

                    var root = JsonNode.Parse(System.IO.File.ReadAllText(path))?.AsObject();
                    if (root == null)
                        return StatusCode(500, new { error = "ConfigV2.json is malformed." });

                    root.Remove("ServerMaintainence");
                    root["ServerMaintenance"] = new JsonObject { ["StartsInMinutes"] = minutes };

                    System.IO.File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Director] Failed to write maintenance config: {ex.Message}");
                return StatusCode(500, new { error = "Failed to update config." });
            }

            var push = JsonSerializer.Serialize(new { Id = ((int)Vanadium.Classes.WebSocket.WebsocketEvents.ResponseResults.ServerMaintenance).ToString(), Msg = new { StartsInMinutes = minutes } });
            await NotificationsController.SendToAll(push);

            Console.WriteLine($"[Director] Maintenance set to {minutes}m by {session?.DiscordUserId ?? "unknown"}");
            return Ok(new { success = true, minutes });
        }

        [HttpGet("/vannet/api/director/system-prompt")]
        public IActionResult DirectorGetSystemPrompt()
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "systemprompt.txt");
            if (!System.IO.File.Exists(path))
                return Ok(new { message = "" });

            return Ok(new
            {
                message = System.IO.File.ReadAllText(path)
            });
        }

        [HttpPost("/vannet/api/director/system-prompt")]
        public async Task<IActionResult> DirectorSetSystemPrompt([FromBody] string body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            string path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "systemprompt.txt");
            if (!System.IO.File.Exists(path))
                return NotFound(new { error = "systemprompt.txt not found." });

            System.IO.File.WriteAllText(path, body ?? "");

            return Ok(new { success = true });
        }

        [HttpPost("/vannet/api/director/players/{playerId:long}/raw-mod-details")]
        public async Task<IActionResult> DirectorSetRawModDetails(long playerId, [FromBody] DirectorRawModDetailsRequest body)
        {
            if (!ValidateDirectorToken(out _)) return NotFound("");

            var player = PlayerDB.Players.FindById(playerId);
            if (player?.Player == null) return NotFound(new { error = "Player not found." });

            var mbd = new ModerationBlockDetails
            {
                ReportCategory = body.ReportCategory,
                Duration = body.Duration,
                GameSessionId = body.GameSessionId,
                IsHostKick = body.IsHostKick,
                Message = body.Message,
                PlayerIdReporter = body.PlayerIdReporter,
                IsBan = body.IsBan,
                IsVoiceModAutoban = body.IsVoiceModAutoban,
                IsDeviceBan = body.IsDeviceBan,
                IsWarning = body.IsWarning,
                VoteKickReason = body.VoteKickReason,
                TimeoutStartedAt = body.TimeoutStartedAt,
                AssociatedAccountUsername = body.AssociatedAccountUsername
            };

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.ModerationBlockDetails = mbd;

            if (body.BootToDorm)
            {
                player.Player.PlayerExtra.Heartbeat = new Heartbeat
                {
                    playerId = playerId,
                    isOnline = false,
                    roomInstance = null,
                    errorCode = 0
                };
                PlayerDB.Players.Update(player);
                await NotiController.SendPresenceUpdate(playerId, player.Player.PlayerExtra.Heartbeat);
                await NotiController.SendAccountUpdate(playerId, PlayerDB.GetAccountMe(playerId));
                await NotificationsController.SendBanned(playerId, mbd);
            }
            else
            {
                PlayerDB.Players.Update(player);
            }

            return Ok(new { success = true });
        }

        [HttpGet("/vannet/api/players/v1/online")]
		public async Task<IActionResult> PlayersOnline()
        {
            var onlinePlayers = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
                .Select(p => new
                {
                    accountId = p.PlayerId,
                    username = p.Player?.Username,
                    displayName = p.Player?.DisplayName,
                    roomName = p.Player?.PlayerExtra?.Heartbeat?.roomInstance?.Name
                })
                .ToList();
            return Ok(new { count = onlinePlayers.Count, players = onlinePlayers });
        }

        private async Task NotifyRoomUpdate(long roomId)
        {
            var updated = RoomDB.GetRoom(roomId);
            var onlinePlayers = PlayerDB.Players.FindAll()
                .Where(p => p.Player?.PlayerExtra?.Heartbeat?.isOnline == true)
                .Select(p => p.PlayerId)
                .ToList();
            foreach (var pid in onlinePlayers)
                await NotiController.SendRoomUpdate(pid, updated);
        }

        private static object BuildPlayerSummary(FullPlayer p) => new
        {
            accountId = p.PlayerId,
            username = p.Player?.Username,
            displayName = p.Player?.DisplayName,
            profileImage = p.Player?.ProfileImage,
            level = p.Player?.Level ?? 1,
            createdAt = p.Player?.CreatedAt,
            isOnline = p.Player?.PlayerExtra?.Heartbeat?.isOnline ?? false
        };

        private static object BuildPlayerFull(FullPlayer p) => new
        {
            accountId = p.PlayerId,
            username = p.Player?.Username,
            displayName = p.Player?.DisplayName,
            profileImage = p.Player?.ProfileImage,
            level = p.Player?.Level ?? 1,
            xp = p.Player?.XP ?? 0,
            bio = p.Player?.Bio,
            createdAt = p.Player?.CreatedAt,
            lastLoginAt = p.Player?.LastLoginAt,
            isJunior = p.Player?.IsJunior,
            roles = p.PlayerRoles?.Select(r => r.ToString()).ToList() ?? new List<string>(),
            heartbeat = p.Player?.PlayerExtra?.Heartbeat,
            dormRoomId = p.Player?.PlayerExtra?.DormRoomId,
            moderationBlockDetails = p.Player?.PlayerExtra?.ModerationBlockDetails,
            toxMod = p.Player?.PlayerExtra?.ToxMod,
            juniorStatus = p.Player?.PlayerExtra?.JuniorStatus,
            currencies = p.Player?.PlayerExtra?.Currencies,
            ownedEquipment = p.Player?.PlayerExtra?.OwnedEquipment,
            influencer = p.Player?.PlayerExtra?.Influencer,
            platformIds = p.PlatformIds,
            nameLocked = p.Player?.NameLocked ?? false,
            recRoomPlus = ReadRRPlusIds().Contains(p.PlayerId),
            discordUserId = p.Player?.DiscordUserId,
            discordUsername = p.Player?.DiscordUsername,
            discordLinked = p.Player?.DiscordLinked ?? false,
            discordLinkedAt = p.Player?.DiscordLinkedAt
        };

        private static object BuildRoomSummary(Room r) => new
        {
            roomId = r.RoomId,
            name = r.Name,
            imageName = r.ImageName,
            creatorAccountId = r.CreatorAccountId,
            stats = r.Stats,
            accessibility = r.Accessibility,
            state = r.State,
            tags = r.Tags,
            maxPlayers = r.MaxPlayers,
            ageRating = r.AgeRating,
            createdAt = r.CreatedAt
        };

        public class DirectorCoachMessageRequest { public string? Message { get; set; } }
        public class DirectorPlayerCoachMessageRequest { public string? Message { get; set; } }
        public class DirectorSendWsHexRequest
        {
            public string? Hex { get; set; }
            public bool Exact { get; set; }
        }
        public class DirectorSendWsJsonRequest { public string? Json { get; set; } }
        public class DirectorShoveAllRequest { public long RoomId { get; set; } public long SubRoomId { get; set; } public string? InstanceId { get; set; } }
        public class DirectorDiscordCallbackRequest { public string? Code { get; set; } }
        public class DirectorRenameRequest { public string? Username { get; set; } }
        public class DirectorCreatePlayerRequest { public long PlayerId { get; set; } public string? Username { get; set; } public bool IsJunior { get; set; } = false; }
        public class DirectorNameLockRequest { public bool Locked { get; set; } = true; }
        public class DirectorAnnouncementRequest { public string? Title { get; set; } public string? Body { get; set; } public string? ImageName { get; set; } public string? LinkName { get; set; } public string? LinkUri { get; set; } public AnnoucementDBClasses.LinkType LinkType { get; set; } public AnnoucementDBClasses.AnnouncementType AnnouncementType { get; set; } public bool Clear { get; set; } = false; }
        public class DirectorCommunityBannerRequest { public string? Message { get; set; } public string? MoreInfoUrl { get; set; } }
        public class DirectorRRPlusRequest { public bool Enabled { get; set; } = true; }
        public class DirectorBioRequest { public string? Bio { get; set; } }
        public class DirectorPasswordRequest { public string? Password { get; set; } }
        public class DirectorSetPfpRequest { public string? ImageName { get; set; } }
        public class DirectorSetLevelRequest { public int Level { get; set; } public int? Xp { get; set; } }
        public class DirectorSetJuniorRequest { public bool IsJunior { get; set; } = false; public string? Reason { get; set; } }
        public class DirectorCoachDmRequest { public string? Message { get; set; } }
        public class DirectorSetPlatformIdRequest { public string? PlatformId { get; set; } }
        public class DirectorShoveRequest { public long RoomId { get; set; } public long? SubRoomId { get; set; } public string? InstanceId { get; set; } }
        public class DirectorTransferRoomsRequest { public long NewPlayerId { get; set; } public bool KeepCoOwner { get; set; } = false; }
        public class DirectorRoleRequest { public string? Role { get; set; } public string? Action { get; set; } }
        public class DirectorInfluencerRequest { public string? Action { get; set; } }
        public class DirectorBanRequest { public string? Reason { get; set; } public int Duration { get; set; } = 99999; }
        public class DirectorHwidBanRequest { public string? Hwid { get; set; } public long? PlayerId { get; set; } public string? Reason { get; set; } public int? DurationDays { get; set; } }
        public class DirectorHwidUnbanRequest { public string? Hwid { get; set; } }
        public class DirectorSetTokensRequest { public int Amount { get; set; } public int BalanceType { get; set; } = 0; }
        public class DirectorGiveSkinRequest { public string? Skin { get; set; } public string? Action { get; set; } }
        public class DirectorRoomNameRequest { public string? Name { get; set; } }
        public class DirectorRoomDescRequest { public string? Description { get; set; } }
        public class DirectorRoomImageRequest { public string? ImageName { get; set; } }
        public class DirectorRoomAccessibilityRequest { public int Accessibility { get; set; } }
        public class DirectorRoomCloningRequest { public bool Allowed { get; set; } }
        public class DirectorRoomRestrictionsRequest { public bool SupportsScreens { get; set; } public bool SupportsWalkVR { get; set; } public bool SupportsTeleportVR { get; set; } public bool SupportsJuniors { get; set; } public bool SupportsMobile { get; set; } }
        public class DirectorRoomAgeRatingRequest { public int AgeRating { get; set; } }
        public class DirectorRoomMaxPlayersRequest { public int MaxPlayers { get; set; } }
        public class DirectorChangeOwnerRequest { public long NewOwnerId { get; set; } }
        public class DirectorSetRoomRoleRequest { public long PlayerId { get; set; } public int Role { get; set; } }
        public class DirectorRoomTagsRequest { public List<string>? Tags { get; set; } public string? Action { get; set; } public int? Type { get; set; } }
        public class DirectorChangeDatablobRequest { public long SubRoomId { get; set; } public string? Datablob { get; set; } }
        public class DirectorRefreshInstancesRequest
        {
            // the panel can hand us the id as a string when safeParse quotes large ids
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
            public long SubRoomId { get; set; }
        }
        public class DirectorSetInstancesPrivacyRequest
        {
            // the panel can hand us the id as a string when safeParse quotes large ids
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
            public long SubRoomId { get; set; }
            public bool IsPrivate { get; set; }
        }
        public class DirectorDbExecRequest { public string? Expression { get; set; } }
        public class DirectorMaintenanceRequest { public int Minutes { get; set; } = 10; }
        public class DirectorToxModBanRequest { public string? Category { get; set; } public string? Note { get; set; } public long? DurationSeconds { get; set; } public bool Loud { get; set; } = false; }
        public class DirectorToxModUnbanRequest { public bool ResetStrikes { get; set; } = false; }
        public class DirectorGiftAllTokensRequest { public int Amount { get; set; } public string? Message { get; set; } }
        public class DirectorForceBringRequest { public string? Username { get; set; } }
        public class DirectorRawModDetailsRequest
        {
            public ReportCategory ReportCategory { get; set; } = ReportCategory.Moderator;
            public int Duration { get; set; } = 0;
            public long GameSessionId { get; set; } = 0;
            public bool? IsHostKick { get; set; } = false;
            public string? Message { get; set; }
            public ulong? PlayerIdReporter { get; set; }
            public bool? IsBan { get; set; } = false;
            public bool? IsVoiceModAutoban { get; set; } = false;
            public bool? IsDeviceBan { get; set; } = false;
            public bool? IsWarning { get; set; } = false;
            public string? VoteKickReason { get; set; }
            public DateTime? TimeoutStartedAt { get; set; }
            public string? AssociatedAccountUsername { get; set; }
            public bool BootToDorm { get; set; } = false;
        }

    public class DirectorAuditFilter : IAsyncActionFilter
    {
        private const string WebhookUrl = "https://discord.com/api/webhooks/1545617304208023625/rRR-9CQjtLZtWpnOoHtCrIKfelX1EPn8AI0I4OopG6e-TN0MBl8e8QWmrWpiXDqOK--O";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var request = context.HttpContext.Request;

            if (!request.Path.Value?.StartsWith("/vannet/api/director/") == true || request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) || request.Path.Value!.Equals("/vannet/api/director/discord-callback", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            string? discordUserId = null;
            string? token = request.Headers["X-Director-Token"].FirstOrDefault()
                ?? request.Cookies["director_session"];

            if (!string.IsNullOrWhiteSpace(token))
            {
                lock (WebsiteController.SessionLock)
                {
                    if (WebsiteController.DirectorSessions.TryGetValue(token, out var s) && s.ExpiresAt > DateTime.UtcNow)
                        discordUserId = s.DiscordUserId;
                }
            }

            string requestData = ReadRequestDataAsync(context);

            var executed = await next();

            string responseData = ReadResponseData(executed.Result);
            string action = request.Path.Value ?? "unknown";
            if (request.QueryString.HasValue)
                action += request.QueryString.Value;

            _ = SendEmbedAsync(discordUserId, action, requestData, responseData);
        }

        private static string ReadRequestDataAsync(ActionExecutingContext context)
        {
            try
            {
                var request = context.HttpContext.Request;
                var parts = new List<string>();

                if (request.Query.Count > 0)
                    parts.AddRange(request.Query.Keys.Select(k => $"{k}: {request.Query[k]}"));

                foreach (var arg in context.ActionArguments.Values)
                {
                    if (arg == null) continue;
                    try
                    {
                        var json = JsonSerializer.Serialize(arg, new JsonSerializerOptions { WriteIndented = false });
                        var doc = JsonDocument.Parse(json);
                        foreach (var prop in doc.RootElement.EnumerateObject())
                            parts.Add($"{prop.Name}: {prop.Value}");
                    }
                    catch { parts.Add(arg.ToString() ?? ""); }
                }

                return parts.Count > 0 ? string.Join("\n", parts) : "*(no body)*";
            }
            catch { }
            return "*(no body)*";
        }

        private static string ReadResponseData(IActionResult? result)
        {
            try
            {
                if (result is ObjectResult obj && obj.Value != null)
                {
                    string json = JsonSerializer.Serialize(obj.Value, new JsonSerializerOptions { WriteIndented = false });
                    if (json.Length > 800) json = json[..800] + "…";
                    try
                    {
                        var doc = JsonDocument.Parse(json);
                        var sb = new StringBuilder();
                        foreach (var prop in doc.RootElement.EnumerateObject())
                            sb.Append($"{prop.Name}: {prop.Value}  ");
                        return sb.ToString().TrimEnd();
                    }
                    catch { return json; }
                }
                if (result is StatusCodeResult sc)
                    return $"status: {sc.StatusCode}";
            }
            catch { }
            return "*(no response)*";
        }

        private static async Task SendEmbedAsync(string? discordUserId, string action, string requestData, string responseData)
        {
            try
            {
                using var http = new HttpClient();

                var embed = new
                {
                    color = 0x1a1a1a,
                    description = $"<@{discordUserId ?? "unknown"}> executed action: **{action}**",
                    fields = new[]
                    {
                        new { name = "Request", value = string.IsNullOrWhiteSpace(requestData) ? "*(no body)*" : $"```{requestData}```", inline = false },
                        new { name = "Response", value = string.IsNullOrWhiteSpace(responseData) ? "*(no response)*" : $"```{responseData}```", inline = false }
                    }
                };

                var payload = JsonSerializer.Serialize(new { embeds = new[] { embed } });
                await http.PostAsync(WebhookUrl, new StringContent(payload, Encoding.UTF8, "application/json"));
            }
            catch { }
        }
    }
}

}
