using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration.UserSecrets;
using Newtonsoft.Json;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Classes.Rooms;
using static Vanadium.Classes.ServerConfig;
using Vanadium.Utils;
using Vanadium.Utils.NotiController;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml;
using static ImageMetadataDB;
using static Vanadium.Classes.DBs.DBClasses.EventDBClasses;
using static Vanadium.Classes.DBs.DBClasses.FriendsDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    public class ImageController : ControllerBase
    {


        internal async Task SendImageHileWebhook(
            long accountId,
            string? message,
            string? imageName = null,
            string? headersPretty = null)
        {

            var embed = new Dictionary<string, object?>
            {
                ["title"] = $"ID = {accountId}",
                ["color"] = 3066993,
                ["fields"] = new object[]
                {
                    new { name = "Headers", value = headersPretty ?? Request.Headers.ToString(), inline = false },
                    new { name = "IP", value = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown", inline = false },
                    new { name = "Image (in /Data/BadImages look at your own risk)", value = imageName ?? "No image provided", inline = true }
                },
                ["footer"] = new { text = message ?? "No message provided" }
            };

            var payload = new
            {
                embeds = new[] { embed }
            };

            try
            {
                var content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(payload),
                    Encoding.UTF8, "application/json");
                await APIController._webhookClient.PostAsync(
                    "https://discord.com/api/webhooks/1544865393091547267/1ntP_8icrIw2O0OiLEt1Rk05lWANxOJksouFVQIcKLu9q9dFIBqSSxPBIkGn6KZz9dbp",
                    content);
            }
            catch { }
        }

        string lastContentType = "";

        public static string GetHeadersPretty(HttpRequest request)
        {
            var sb = new StringBuilder();
            foreach (var header in request.Headers)
            {
                sb.AppendLine($"{header.Key}: {header.Value}");
            }
            return sb.ToString();
        }

        [HttpPost("/api/images/v4/uploadsaved")]
        public async Task<IActionResult> UploadSaved()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var player = PlayerDB.GetCurrentPlayer(id.Value);

            bool isDeveloper = player?.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false;

            string divertPaths = "";

            var userAgent = Request.Headers.UserAgent.ToString();
            if (!userAgent.Contains("BestHTTP", StringComparison.OrdinalIgnoreCase))
                divertPaths = "Non-BestHTTP";

            var contentType = Request.Headers.ContentType.ToString();
            if (!contentType.Contains("BestHTTP", StringComparison.OrdinalIgnoreCase) && !isDeveloper)
                divertPaths = "Non-image content type";
            
            if (contentType == lastContentType)
                divertPaths = "Repeated content type";
            lastContentType = contentType;

            if (!ServerConfig.PostingEnabled && !isDeveloper)
                return StatusCode(403, new { success = false, error = "Images.UserIsAcessibilityLocked" });

            if (!isDeveloper && !PlayerDB.TryIncrementDailyImageUpload(id.Value))
                return StatusCode(403, new { success = false, error = "Images.DailyImagesUploadReached" });

            if (PlayerDB.IsBanned(id.Value))
                divertPaths = "Banned player uploaded image";

            var form = await Request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return BadRequest();

            byte[] fileBytes;
            using (var input = file.OpenReadStream())
            using (var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(input))
            using (var output = new MemoryStream())
            {
                await image.SaveAsync(output, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                fileBytes = output.ToArray();
            }

            bool tripped = string.IsNullOrWhiteSpace(divertPaths) ? false : true;

            var fileName = ImageMetadataDB.SaveImageFile(fileBytes, player.PlayerId, tripped);
            if (!form.TryGetValue("imgMeta", out var imgMetaValue) || string.IsNullOrWhiteSpace(imgMetaValue))
                return BadRequest();
            if (tripped)
            {
                await SendImageHileWebhook(player.PlayerId, divertPaths, fileName, GetHeadersPretty(Request));
                return Ok(new { ImageName = fileName });
            }

            ImageMetadataDB.ImageMetadata? metadata;
            try
            {
                metadata = System.Text.Json.JsonSerializer.Deserialize<ImageMetadataDB.ImageMetadata>(
                    imgMetaValue!,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return BadRequest();
            }

            if (metadata == null)
                return BadRequest();

            var newImage = new ImageMetadataDB.FullSavedImage
            {
                RoomId = metadata.roomId,
                PlayerId = (ulong)id.Value,
                ImageName = fileName,
                Description = metadata.description,
                Accessibility = metadata.accessibility,
                TaggedPlayerIds = metadata.playerIds ?? new List<ulong>(),
                CreatedAt = DateTime.UtcNow,
                Type = metadata.savedImageType
            };

            ImageMetadataDB.InsertImage(newImage);

            string playerName = PlayerDB.GetCurrentPlayer((long)id.Value)?.Player?.DisplayName
                ?? $"User {id.Value}";
            string playerId = $"{newImage.PlayerId}";
            string roomName = RoomDB.GetRoom(metadata.roomId)?.Name
                ?? $"Room {metadata.roomId}";

            if (metadata.accessibility == ImageMetadataDB.ImageAccessibility.Public)
            {
                try
                {
                    using var http = new HttpClient();
                    var imageUrl = $"https://reloxa.xyz/imageserver/{fileName}";
                    var uploaderProfilePic = $"https://reloxa.xyz/imageserver/{PlayerDB.GetCurrentPlayer((long)newImage.PlayerId)?.Player?.ProfileImage ?? "DefaultPFP.png"}?cropSquare=true";

                    bool roomIsPrivate = false;
                    string roomDisplayName;
                    var room = RoomDB.GetRoom(metadata.roomId);
                    if (room == null || room.Accessibility != RoomDBClasses.RoomAccessibility.Public)
                    {
                        roomIsPrivate = true;
                        roomDisplayName = "[PRIVATE ROOM]";
                    }
                    else
                    {
                        roomDisplayName = $"^{room.Name}";
                    }

                    var payload = new
                    {
                        content = "",
                        tts = false,
                        embeds = new[]
                        {
                new
                {
                    author = new
                    {
                        name = $"{playerName} uploaded a photo!",
                        icon_url = uploaderProfilePic
                    },
                    title = $"Room - {roomDisplayName}",
                    image = new { url = imageUrl },
                    fields = Array.Empty<object>()
                }
            },
                        components = Array.Empty<object>(),
                        flags = 0
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    await http.PostAsync(ServerConfig.ImagesWebhook, content);
                }
                catch { }
            }

            return Ok(new { ImageName = fileName });
        }
        
        [HttpPost("/api/images/v1/modifydescription")]
		public async Task<IActionResult> ModifyImageDescription([FromBody] ModifyDescriptionRequest request)
		{
		    var playerId = AuthStuff.GetPlayerId(Request);
		    if (playerId == null)
		        return Unauthorized("");

		    if (!ImageMetadataDB.ModifyDescription(request.ImageName, playerId.Value, request.Description))
		        return Ok(new { success = false });

		    return Ok(new { success = true });
		}
        
        [HttpGet("/api/photos/v1/top/today")] // rec.net uses this, not the game
        public async Task<IActionResult> RecNetGetTodaysTopPhotos()
        {
        	return Ok(new // temp
            {
                RoomId = 1,
                PlayerId = 1,
                ImageName = "RecCenter.jpg",
                Description = "Nothing to see here yet. This is temporary.",
                Accessibility = 1,
                TaggedPlayerIds = new List<ulong>(),
                CreatedAt = DateTime.UtcNow,
                Type = "ShareCamera"
            });
        }

        [HttpGet("/api/images/v1/slideshow")]
        public async Task<IActionResult> GetSlideshow()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");

            var images = ImageMetadataDB.GetGlobalImages();

            var result = images.Select(img =>
            {
                var player = PlayerDB.GetCurrentPlayer((long)img.PlayerId);
                var room = RoomDB.GetRoom(img.RoomId);

                return new
                {
                    ImageName = img.ImageName,
                    PlayerId = img.PlayerId,
                    RoomName = room?.Name ?? "Unknown Room!",
                    SavedImageId = img.Id,
                    Username = player?.Player?.Username ?? $"User {img.PlayerId}"
                };
            }).ToList();

            return Ok(new
            {
                Images = result,
                ValidTill = DateTime.UtcNow.AddDays(1).ToString("O")
            });
        }

        [HttpGet("/api/images/v2/named")]
        public async Task<IActionResult> ApiImagesV2Named()
        {
            return Ok(Get_named_image());
        }

        public class NamedImage
        {
            public string? FriendlyImageName { get; set; }
            public string? ImageName { get; set; }
            public string? StartTime { get; set; }
            public string? EndTime { get; set; }
        }

        public static List<NamedImage> Get_named_image()
        {
            const string start = "0001-01-01T00:00:00";
            const string end = "9999-12-31T23:59:59.9999999";
            const string img = "Gribbly_67.png";

            return
            [
                new() { FriendlyImageName = "DormRoomBucket", ImageName = "room_127_1786398699787.png", StartTime = start, EndTime = end },
                new() { FriendlyImageName = "CreativePuzzles", ImageName = "waeboi_thelegaldocuments.jpg", StartTime = start, EndTime = end },
                new() { FriendlyImageName = "FeaturedLoft", ImageName = "cat.jpg", StartTime = start, EndTime = end },
                new() { FriendlyImageName = "TheaterWall", ImageName = "Vanadium.jpg", StartTime = start, EndTime = end },
                /*new() { FriendlyImageName = "Cafe", ImageName = img, StartTime = start, EndTime = end },
                new() { FriendlyImageName = "Loft", ImageName = img, StartTime = start, EndTime = end },
                new() { FriendlyImageName = "UpperLanding", ImageName = img, StartTime = start, EndTime = end },
                new() { FriendlyImageName = "PVPwall", ImageName = img, StartTime = start, EndTime = end },
                new() { FriendlyImageName = "BackStairs", ImageName = img, StartTime = start, EndTime = end },
                new() { FriendlyImageName = "GymWall", ImageName = img, StartTime = start, EndTime = end },
                new() { FriendlyImageName = "FrontStairs", ImageName = img, StartTime = start, EndTime = end }*/
            ];
        }
        
        [HttpPost("/api/images/v1/{imageId}/report")]
		public async Task<IActionResult> ReportImage(int imageId)
		{
				var callerId = AuthStuff.GetPlayerId(Request);
				if (callerId == null)
						return StatusCode(403);

				const string reportWebhookUrl = "https://discord.com/api/webhooks/1524345788195410012/0KzQEOsJ_Bpe_WJ1ZFg1z3aWFmBwCDhrdnr54vrb8UDm5S2NhI670ixuziVc3e11maue";

				var image = ImageMetadataDB.GetDb()
						.GetCollection<ImageMetadataDB.FullSavedImage>("images")
						.FindById(imageId);

				string imageLink = image != null
						? $"https://reloxa.xyz/imageserver/{image.ImageName}"
						: $"https://reloxa.xyz/imageserver/unknown";

				string webhookContent = $"**🟠 Image Reported!**  \nImage Id: ``{imageId}``  \nComing From: {callerId.Value} \n{imageLink}";

				try
				{
						using var httpClient = new HttpClient();
						var payload = System.Text.Json.JsonSerializer.Serialize(new { content = webhookContent });
						await httpClient.PostAsync(reportWebhookUrl, new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
				}
				catch { }

				return Ok(new { success = true });
		}

        [HttpPost("/api/images/v1/deletesaved")]
        public async Task<IActionResult> DeleteSavedImage([FromBody] DeleteSavedImageRequest request)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            if (string.IsNullOrWhiteSpace(request?.ImageName))
                return BadRequest(new { success = false });

            var db = ImageMetadataDB.GetDb();
            var col = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var image = col.FindOne(x => x.ImageName == request.ImageName);

            if (image == null)
                return NotFound("");

            if ((long)image.PlayerId != playerId.Value)
                return StatusCode(403, new { success = false });

            col.Delete(image.Id);

            var cheerCol = db.GetCollection<ImageMetadataDB.Cheer>("cheers");
            cheerCol.DeleteMany(c => c.SavedImageId == image.Id);

            await NotiController.SendEvent(playerId.Value, "ImageDeleted", new
            {
                ImageName = request.ImageName,
                SavedImageId = image.Id
            });

            return Ok(new { success = true });
        }
        
   		[HttpPost("/api/images/v{version}/modifyaccessibility")]
        public async Task<IActionResult> ModifyImageAccessibility([FromBody] ModifyAccessibilityRequest request)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            if (string.IsNullOrWhiteSpace(request?.ImageName))
                return BadRequest(new { success = false });

            var db = ImageMetadataDB.GetDb();
            var col = db.GetCollection<ImageMetadataDB.FullSavedImage>("images");
            var image = col.FindOne(x => x.ImageName == request.ImageName);

            if (image == null)
                return NotFound("");

            if ((long)image.PlayerId != playerId.Value)
                return StatusCode(403, new { success = false });

            if (image.AccessibilityLocked)
                return StatusCode(403, new { success = false });

            image.Accessibility = request.Accessibility;
            col.Update(image);

            await NotiController.SendEvent(playerId.Value, "ImageAccessibilityChanged", new
            {
                ImageName = request.ImageName,
                SavedImageId = image.Id,
                Accessibility = (int)request.Accessibility
            });

            return Ok(new { success = true });
        }

        [HttpGet("/api/images/v5/bulk")]
        public async Task<IActionResult> GetImagesByBulkIds()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return StatusCode(403);

            var idsRaw = Request.Query["ids"];

            List<int> idList = idsRaw
                .SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(x => x.Trim())
                .Where(x => int.TryParse(x, out _))
                .Select(int.Parse)
                .ToList();

            if (idList.Count == 0)
            {
                return new ContentResult
                {
                    Content = "",
                    ContentType = "application/json",
                    StatusCode = 400
                };
            }

            var images = ImageMetadataDB.GetImagesByIds(idList);

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(images),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("/api/images/v5/player/{playerId}")]
        public async Task<IActionResult> GetPlayerImages(ulong playerId, [FromQuery] int sort = 0)
        {
            var me = AuthStuff.GetPlayerId(Request);
            if (me == null)
                return Unauthorized("");

            var images = ImageMetadataDB.GetPlayerImages((ulong)me.Value, playerId)
                .Where(x => x.SavedImageType == ImageMetadataDB.SavedImageTypeEnum.ShareCamera ||
                            x.SavedImageType == ImageMetadataDB.SavedImageTypeEnum.RoomThumbnail ||
                            x.SavedImageType == ImageMetadataDB.SavedImageTypeEnum.ProfileThumbnail)
                .ToList();

            var sorted = sort switch
            {
                2 => images.OrderByDescending(x => x.CheerCount),
                3 => images.OrderByDescending(x => x.CommentCount),
                _ => images.OrderByDescending(x => x.CreatedAt)
            };

            return Ok(sorted.ToList());
        }

        [Route("/api/images/v5/cheered/bulk")]
        public async Task<IActionResult> ImagesCheered([FromQuery] int[] id)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return Unauthorized("");

            long playerId = account.Value;

            var results = id.Select(imageId => new
            {
                SavedImageId = imageId,
                IsCheered = ImageMetadataDB.HasUserCheered(imageId, (ulong)playerId)
            }).ToList();

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(results),
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpPost("/api/images/v1/cheer")]
        public async Task<IActionResult> CheerImage([FromBody] CheerRequest request)
        {
            var account = AuthStuff.GetPlayerId(Request);
            if (account == null)
                return Unauthorized("");

            long playerId = account.Value;

            bool isCheered = ImageMetadataDB.ToggleCheer(request.SavedImageId, (ulong)playerId, request.Cheer);
            int cheerCount = ImageMetadataDB.GetCheerCount(request.SavedImageId);

            ImageMetadataDB.UpdateImageCheerCount(request.SavedImageId, cheerCount);

            return new ContentResult
            {
                Content = "",
                ContentType = "application/json",
                StatusCode = 200
            };
        }

        [HttpGet("/api/images/v4/room/{id:long}")]
        public async Task<IActionResult> GetRoomImages(
            long id,
            int sort = 0,
            int filter = 0,
            int take = 50,
            int skip = 0)
        {
            var pid = AuthStuff.GetPlayerId(Request);
            if (pid == null)
                return Unauthorized("");

            var images = ImageMetadataDB.GetRoomImages((ulong)pid.Value, id, sort, filter, take, skip);
			return Ok(images);
        }

        [HttpGet("/api/images/v6")]
        public async Task<IActionResult> GetImageV6([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest();

            return Ok(ImageMetadataDB.GetImageByName(name));
        }

        [HttpGet("imageserver/{*img_path}")]
        public async Task<IActionResult> ImgServer(
            string img_path,
            [FromQuery] int width = 0,
            [FromQuery] int height = 0,
            [FromQuery] string? sig = null)
        {
            img_path = Uri.UnescapeDataString(img_path ?? "").TrimStart('/');

            if (sig != null && sig != "p1")
                return BadRequest();

            bool cropSquare = HttpContext.Request.Query.ContainsKey("cropsquare") &&
                (HttpContext.Request.Query["cropsquare"].ToString().ToLower() == "true" ||
                 HttpContext.Request.Query["cropsquare"].ToString() == "1");

            if (img_path.EndsWith(".ig"))
            {
                cropSquare = true;
                img_path = img_path.Substring(0, img_path.Length - 3);
            }

            HttpContext.Response.Headers.Append("Content-Disposition", $"inline; filename=\"{Path.GetFileName(img_path)}\"");
            HttpContext.Response.Headers.Append("Access-Control-Allow-Origin", "*");
            HttpContext.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Authorization, Cache-Control");
            HttpContext.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            Response.Headers["Cache-Control"] = "public, max-age=14400";

            var etag = $"\"{img_path.GetHashCode()}\"";
            HttpContext.Response.Headers.Append("ETag", etag);

            string imagesPath = Path.Join(Program.dataDir, "Images");
            string cdnImagesPath = Path.GetFullPath(Path.Join(Program.dataDir, "..", "Data", "cdn", "images"));

            string? foundLocalPath = null;
            var searchPaths = new List<string>();

            if (Directory.Exists(imagesPath))
                searchPaths.Add(imagesPath);

            if (Directory.Exists(cdnImagesPath))
                searchPaths.Add(cdnImagesPath);

            foreach (var basePath in searchPaths)
            {
                string directPath = Path.Join(basePath, img_path);
                if (System.IO.File.Exists(directPath))
                {
                    foundLocalPath = directPath;
                    break;
                }

                var subDirectories = Directory.EnumerateDirectories(basePath, "*", SearchOption.AllDirectories);
                foreach (var dir in subDirectories)
                {
                    string testPath = Path.Join(dir, img_path);
                    if (System.IO.File.Exists(testPath))
                    {
                        foundLocalPath = testPath;
                        break;
                    }
                }

                if (foundLocalPath != null)
                    break;
            }

            if (foundLocalPath != null)
            {
                try
                {
                    var imageBytes = await System.IO.File.ReadAllBytesAsync(foundLocalPath);
                    imageBytes = await ProcessImageAsync(imageBytes, cropSquare, width, height);

                    AppendSignatureHeader(imageBytes, sig, img_path);
                    return File(imageBytes, GetMimeType(img_path));
                }
                catch
                {
                    var fallbackBytes = await System.IO.File.ReadAllBytesAsync(foundLocalPath);
                    AppendSignatureHeader(fallbackBytes, sig, img_path);
                    return File(fallbackBytes, "image/jpg");
                }
            }

            string recNetLocalPath = Path.Join(imagesPath, "RecNet", img_path);

            /*try
            {
                using HttpClient client = new();
                byte[] data = await client.GetByteArrayAsync($"https://recroomcdn.blob.core.windows.net/img/{img_path}");

                Directory.CreateDirectory(Path.GetDirectoryName(recNetLocalPath)!);
                await System.IO.File.WriteAllBytesAsync(recNetLocalPath, data);

                try
                {
                    var processed = await ProcessImageAsync(data, cropSquare, width, height);
                    AppendSignatureHeader(processed, sig, img_path);
                    return File(processed, GetMimeType(img_path));
                }
                catch
                {
                    AppendSignatureHeader(data, sig, img_path);
                    return File(data, GetMimeType(img_path));
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"[Radie.app Fetch Failed] {ex.Message}");
            }*/

            string? defaultFallbackPath = null;
            foreach (var basePath in searchPaths)
            {
                string directPath = Path.Join(basePath, "NotFound.png");
                if (System.IO.File.Exists(directPath))
                {
                    defaultFallbackPath = directPath;
                    break;
                }

                var subDirectories = Directory.EnumerateDirectories(basePath, "*", SearchOption.AllDirectories);
                foreach (var dir in subDirectories)
                {
                    string testPath = Path.Join(dir, "NotFound.png");
                    if (System.IO.File.Exists(testPath))
                    {
                        defaultFallbackPath = testPath;
                        break;
                    }
                }

                if (defaultFallbackPath != null)
                    break;
            }

            if (defaultFallbackPath != null)
            {
                try
                {
                    var fallbackBytes = await System.IO.File.ReadAllBytesAsync(defaultFallbackPath);
                    try
                    {
                        fallbackBytes = await ProcessImageAsync(fallbackBytes, cropSquare, width, height);
                    }
                    catch { }

                    AppendSignatureHeader(fallbackBytes, sig, img_path);
                    return File(fallbackBytes, "image/png");
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"[critical fallback failed] Could not read NotFound.png file: {fallbackEx.Message}");
                }
            }

            return NotFound("");
        }

        private void AppendSignatureHeader(byte[] payload, string? sig, string filePath)
        {
            if (string.IsNullOrEmpty(sig))
                return;

            string headerValue = ImageSignatureValidation.SignPayload(payload, sig);
            HttpContext.Response.Headers.Append("Content-Signature", headerValue);
        }

        private static string GetMimeType(string filePath)
        {
            var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(filePath, out var contentType))
            {
                contentType = "image/jpg";
            }
            return contentType;
        }

        public class DeleteSavedImageRequest
        {
            public string ImageName { get; set; } = string.Empty;
        }

        public class ModifyAccessibilityRequest
        {
            public string ImageName { get; set; } = string.Empty;
            public ImageMetadataDB.ImageAccessibility Accessibility { get; set; }
        }

        private static async Task<byte[]> ProcessImageAsync(byte[] imageBytes, bool cropSquare, int width, int height)
        {
            using var ms = new MemoryStream(imageBytes);
            using var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(ms);

            if (cropSquare)
            {
                int size = Math.Min(image.Width, image.Height);
                int x = (image.Width - size) / 2;
                int y = (image.Height - size) / 2;
                image.Mutate(ctx => ctx.Crop(new Rectangle(x, y, size, size)));

                int targetSize = width > 0 ? width : height;
                if (targetSize > 0)
                {
                    image.Mutate(ctx => ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(targetSize, targetSize),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3
                    }));
                }
            }
            else if (width > 0 || height > 0)
            {
                int resizeWidth = width;
                int resizeHeight = height;

                if (width > 0 && height == 0)
                    resizeHeight = (int)((double)image.Height / image.Width * width);
                else if (height > 0 && width == 0)
                    resizeWidth = (int)((double)image.Width / image.Height * height);

                image.Mutate(ctx => ctx.Resize(resizeWidth, resizeHeight));
            }

            using var output = new MemoryStream();
            await image.SaveAsync(output, new PngEncoder());
            return output.ToArray();
        }
    }
}