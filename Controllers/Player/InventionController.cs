using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using static Vanadium.Classes.DBs.DBClasses.InventionDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    public class InventionController : ControllerBase
    {
        [HttpGet("api/inventions/v1/room")]
        public IActionResult RoomsInventions([FromQuery] long? id)
            => Ok(ServerConfig.Bracket);

        [HttpGet("/api/inventions/v1/tagfilters")]
        public IActionResult inventiontagfilters()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            string path = Path.Join(Program.dataDir, "APIS", "jsons", "InventionTagFilters.json");

            if (System.IO.File.Exists(path))
            {
                return Content(System.IO.File.ReadAllText(path), "application/json");
            }
            else
            {
                var json = new
                {
                    PinnedFilters = new[] { "art", "gadget", "sound", "dormskin", "rro" },
                    PopularFilters = new[] { "art", "gadget", "sound", "dormskin" },
                    TrendingFilters = (string[]?)null
                };

                return Ok(json);
            }
        }

        [HttpGet("/api/inventions/v2/mine")]
        public IActionResult Mine()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            return Ok(InventionDB.GetInventionsByPlayer((long)id));
        }

		[HttpPost("/api/inventions/v7/save")]
        [HttpPost("/api/inventions/v6/save")]
        public IActionResult SaveInventionV6([FromBody] SaveInventionRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            if (request == null || string.IsNullOrWhiteSpace(request.name)) return BadRequest();

            var result = InventionDB.SaveInvention((long)id, request);
            if (result == null) return BadRequest();

            var (invention, version) = result.Value;
            return Ok(new { Status = 0, Invention = InventionDB.MapInventionFull(invention), InventionVersion = InventionDB.MapVersion(version) });
        }

        [HttpPost("/api/inventions/v9/save")]
        public IActionResult SaveInventionV9([FromBody] SaveInventionRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            if (request == null || string.IsNullOrWhiteSpace(request.name)) return BadRequest();

            var result = InventionDB.SaveInvention((long)id, request);
            if (result == null) return BadRequest();

            var (invention, version) = result.Value;
            return Ok(new
            {
                value = new
                {
                    Status = 0,
                    Invention = InventionDB.MapInventionFull(invention),
                    InventionVersion = InventionDB.MapVersion(version),
                    TagsResponse = new { Result = 0, Tags = new List<object>() }
                },
                success = true
            });
        }

        [HttpGet("/api/inventions/v{ver}/publish")]
        public IActionResult PublishInventionScary([FromQuery] long inventionId, [FromQuery] int permissionLevel, [FromQuery] int accessibility, [FromQuery] int price)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var result = InventionDB.PublishInvention(inventionId, (long)id.Value, permissionLevel, accessibility, price);
            if (result == null) return BadRequest();

            return Ok(InventionDB.MapInventionFull(result));
        }

        [HttpGet("/api/inventions/v1/unpublish")]
        public IActionResult UnpublishInvention([FromQuery] long inventionId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var result = InventionDB.UnpublishInvention(inventionId, (long)id.Value);
            if (result == null) return BadRequest();

            return Ok(InventionDB.MapInventionFull(result));
        }

        [HttpPatch("/api/inventions/v4/addversion")]
        public IActionResult AddVersion([FromBody] AddVersionRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            if (request == null) return Ok(new { Status = -1 });

            var invention = InventionDB.GetInvention(request.inventionId);
            if (invention == null || invention.CreatorPlayerId != (long)id) return Ok(new { Status = -1 });
            if (string.IsNullOrEmpty(request.inventionDataFilename)) return Ok(new { Status = -1 });

            var result = InventionDB.AddVersion(invention, request);
            if (result == null) return Ok(new { Status = -1 });

            var (inv, version) = result.Value;
            return Ok(new { Status = 0, Invention = InventionDB.MapInventionToData(inv), InventionVersion = InventionDB.MapVersion(version) });
        }

        [HttpGet("/api/inventions/v1/versions")]
        public IActionResult GetVersions([FromQuery] long inventionId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            if (InventionDB.GetInvention(inventionId) == null) return NotFound("");
            return Ok(InventionDB.GetVersionsForInvention(inventionId));
        }

        [HttpGet("/api/inventions/v2/batch")]
        public IActionResult GetInventionsBatchGet([FromQuery(Name = "id")] List<long>? queryIds)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            var ids = queryIds ?? new List<long>();
            if (ids.Count == 0) return Ok(new List<object>());
            return Ok(InventionDB.GetInventionsBatch(ids).Select(inv => InventionDB.MapInventionFull(inv)).ToList());
        }

        [HttpPost("/api/inventions/v2/batch")]
        public IActionResult GetInventionsBatchPost([FromBody] InventionBatchRequest? body, [FromQuery(Name = "id")] List<long>? queryIds)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");

            var ids = (body != null && body.InventionIds.Count > 0) ? body.InventionIds
                    : (queryIds != null && queryIds.Count > 0) ? queryIds
                    : new List<long>();

            if (ids.Count == 0) return Ok(new List<object>());
            return Ok(InventionDB.GetInventionsBatch(ids).Select(inv => InventionDB.MapInventionFull(inv)).ToList());
        }

        [HttpGet("/api/inventions/v1")]
        [HttpGet("/api/inventions/v2/{inventionId:long}")]
        public IActionResult GetInvention([FromQuery] long inventionId, [FromRoute] long? inventionId2)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            var inv = InventionDB.GetInvention(inventionId > 0 ? inventionId : inventionId2 ?? 0);
            if (inv == null) return NotFound("");
            return Ok(InventionDB.MapInventionFull(inv));
        }

        [HttpGet("/api/inventions/v2/{inventionId:long}/versions")]
        public IActionResult GetVersionsV2([FromRoute] long inventionId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            if (InventionDB.GetInvention(inventionId) == null) return NotFound("");
            return Ok(InventionDB.GetVersionsForInvention(inventionId));
        }

        [HttpGet("/api/inventions/v2/{inventionId:long}/permission/me")]
        public IActionResult GetNetPermissionForMe([FromRoute] long inventionId)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return Unauthorized("");
            var invention = InventionDB.GetInvention(inventionId);
            if (invention == null) return NotFound("");
            bool isOwner = invention.CreatorPlayerId == playerId || playerId == 2;
            return Ok(new { Permission = isOwner ? InventionPermissions.Unlimited : InventionPermissions.Unassigned, IsOwner = isOwner });
        }

        [HttpGet("/api/inventions/v2/creator/{accountId:long}")]
        public IActionResult GetInventionsByCreator([FromRoute] long accountId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            return Ok(InventionDB.GetInventionsByCreator(accountId));
        }

        [HttpGet("/api/inventions/v2/room/{roomId:long}")]
        public IActionResult GetRoomInventions([FromRoute] long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            return Ok(InventionDB.GetInventionsByRoom(roomId));
        }

        [HttpGet("/api/inventions/v1/details")]
        public IActionResult GetInventionDetails([FromQuery] long inventionId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            var invention = InventionDB.GetInvention(inventionId);
            if (invention == null) return NotFound("");
            return Ok(new DetailsResponse
            {
                Tags = (invention.Tags ?? new List<InventionTag>())
                    .Select(t => new TagData { Tag = t.Tag, Type = t.Type }).ToList()
            });
        }

        [HttpGet("/api/inventions/v1/fulllineageowner")]
        public IActionResult GetFullLineageOwner([FromQuery] long id)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return Unauthorized("");

            bool isDeveloper = PlayerDB.Players.FindById((long)playerId)?.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false;

            var invention = InventionDB.GetInvention(id);
            if (invention == null)
            {
                if (isDeveloper) return Ok(true);
                return Ok(new { success = false, error_id = "Inventions.FulllineageDoesNotExist", error = "*Invention Shit*\n This invention does not exist!" });
            }
            return Ok(invention.CreatorPlayerId == playerId || playerId == 2);
        }

        [HttpGet("/api/inventions/v1/version")]
        public IActionResult GetInventionVersion([FromQuery] long inventionId, [FromQuery] int version)
        {
            var invention = InventionDB.GetInvention(inventionId);
            if (invention == null) return NotFound("");

            var invVersion = InventionDB.GetVersion(inventionId, version)
                ?? invention.CurrentVersion
                ?? InventionDB.GetLatestVersion(inventionId);

            if (invVersion == null) return NotFound("");

            return Ok(new
            {
                InventionAccessibility = (int)invention.Accessibility,
                InventionPrice = invention.Price,
                InventionGeneralPermission = invention.GeneralPermission,
                InventionCreatorPermission = invention.CreatorPermission,
                invVersion.BlobName,
                BlobHash = invVersion.BlobHash ?? string.Empty,
                HasBetaContent = false,
                HasDevContent = false,
                HasImageFileReference = !string.IsNullOrWhiteSpace(invention.ImageName),
                UgcAccessibility = 0,
                ReferencedUnityAssetIds = Array.Empty<object>(),
                ReferencedInventions = invention.ReferencedInventions ?? new List<long>(),
                invVersion.InventionId,
                invVersion.ReplicationId,
                invVersion.VersionNumber,
                invVersion.InstantiationCost,
                invVersion.LightsCost,
                invVersion.ChipsCost,
                invVersion.CloudVariablesCost,
                invVersion.AICost,
                invVersion.inkCost,
                CreatedAt = invVersion.CreatedAt.ToString("O"),
                invention.IsPublished,
                invention.AllowTrial,
                invention.Name,
                invention.Description,
                invention.ImageName,
                Tags = invention.Tags ?? new List<InventionTag>()
            });
        }

        [HttpGet("/api/inventions/v1/featured")]
        public async Task<IActionResult> FeaturedInventions()
        {
            return Ok(InventionDB.GetTopTodayInventions(true));
        }

        [HttpGet("/api/inventions/v1/toptoday")]
        public async Task<IActionResult> TopTodayInventions()
        {
            return Ok(InventionDB.GetTopTodayInventions(false));
        }

        [HttpGet("/api/inventions/v1/update")]
        public IActionResult UpdateInvention(
            [FromQuery] long inventionId,
            [FromQuery] string? name,
            [FromQuery] string? description,
            [FromQuery] string? imageName,
            [FromQuery] int? price,
            [FromQuery] bool? isPublished,
            [FromQuery] bool? allowTrial,
            [FromQuery] bool? hideFromPlayer,
            [FromQuery] int? generalPermission,
            [FromQuery] int? creatorPermission)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return Unauthorized("");

            var invention = InventionDB.UpdateInvention(inventionId, (long)playerId,
                name, description, imageName, price, isPublished, allowTrial,
                hideFromPlayer, generalPermission, creatorPermission);

            if (invention == null) return NotFound("");
            return Ok(InventionDB.MapInventionToData(invention));
        }

        [HttpPost("/api/inventions/v1/report")]
        public async Task<IActionResult> ReportaInvention([FromBody] ReportInvention request)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return Unauthorized("");
            if (request == null) return BadRequest();

            var invention = InventionDB.GetInvention(request.InventionId);
            var player = PlayerDB.Players.FindById((long)playerId);
            var username = player?.Player?.Username ?? "Unknown";
            var categoryName = Enum.IsDefined(typeof(InventionReportCategory), request.ReportCategory)
                    ? Enum.GetName(typeof(InventionReportCategory), request.ReportCategory)!
                    : request.ReportCategory.ToString();

            var inventionName = invention?.Name ?? $"Unknown ({request.InventionId})";
            var imageUrl = !string.IsNullOrWhiteSpace(invention?.ImageName)
                    ? $"https://reloxa.xyz/imageserver/{invention.ImageName}"
                    : null;

            var lines = new List<string>
                {
                        "# **🔵 New Invention Report!**",
                        $"Reported Invention: {inventionName} `{request.InventionId}`"
                };

            if (!string.IsNullOrWhiteSpace(request.Details))
                lines.Add($"Reason: {request.Details}");

            lines.Add($"Invention Enum Category: \"{categoryName}\" `{(int)request.ReportCategory}`");
            lines.Add($"Coming From: @{username} `{playerId}`");

            if (imageUrl != null)
                lines.Add(imageUrl);

            var payload = new { content = string.Join("\n", lines) };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);

            using var http = new HttpClient();
            await http.PostAsync(ServerConfig.InventionsReportWebhook,
                    new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

            return Ok(new { success = true });
        }

        [HttpGet("/api/inventions/v1/delete")]
        public IActionResult DeleteInvention([FromQuery] long inventionId)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return Unauthorized("");
            bool deleted = InventionDB.DeleteInvention(inventionId, (long)playerId);
            return Ok(new { success = deleted });
        }

        [HttpPost("/api/inventions/v1/settags")]
        public IActionResult SetInventionTags([FromBody] SetTagsRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null) return Unauthorized("");
            if (request == null || request.InventionId <= 0) return BadRequest("Invalid request");

            var response = InventionDB.SetTags(request.InventionId, request);
            if (response.Result != 0) return BadRequest("Failed to set tags");
            return Ok(response);
        }

        [HttpGet("/api/inventions/v1/personaldetails/{inventionId}")]
        public IActionResult GetInventionPersonalDetails([FromRoute] long inventionId)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return Unauthorized("");

            var player = PlayerDB.Players.FindById((long)playerId);
            bool isCheering = player?.Player?.PlayerExtra?.PersonalDetails?.inventionsCheered?.Contains(inventionId) ?? false;

            return Ok(new PersonalDetailsResponse { IsCheering = isCheering });
        }

        [HttpPost("/api/inventions/v{ver}/cheer")]
        public IActionResult CheerInvention([FromBody] CheerInventionRequest request)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null) return Unauthorized("");

            var result = InventionDB.CheerInvention(request.InventionId, (long)playerId, request.Cheer);
            if (result == null) return NotFound("");

            return Ok(new { success = true, error = (object?)null, error_id = (object?)null, value = InventionDB.GetInvention(request.InventionId) });
        }

        [HttpGet("/api/inventions/v1/fromcreators")]
        public IActionResult FromCreator()
            => Ok(ServerConfig.Bracket);

        [HttpGet("/api/inventions/v2/search")]
        public async Task<IActionResult> SearchInventions(
            [FromQuery] string query = "",
            [FromQuery(Name = "value")] string value = "",
            [FromQuery] int skip = 0,
            [FromQuery] int take = 999)
        {
            var searchText = string.IsNullOrWhiteSpace(query) ? value : query;

            if (string.IsNullOrWhiteSpace(searchText))
                return Ok(InventionDB.GetTopTodayInventions(false));

            var searchResult = InventionDB.Search(searchText, skip: skip, take: take);
            return Ok(new
            {
                Results = searchResult.Results.Select(inv => InventionDB.MapInventionFull(inv)).ToList(),
                Total = searchResult.Total
            });
        }
    }
}