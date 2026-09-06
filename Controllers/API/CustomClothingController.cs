using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using static Vanadium.Classes.DBs.DBClasses.CustomClothingDBClasses;

namespace Vanadium.Controllers /* experimental */
{
    [ApiController]
    [Route("api/customAvatarItems")]
    public class CustomClothingController : ControllerBase
    {
        [HttpGet("v1/isCreationEnabled")]
        [HttpGet("v1/isRenderingEnabled")]
        public IActionResult IsCreationEnabled() => Ok(true);

        [HttpGet("v1/minPriceForPublicItem")]
        public IActionResult MinPriceForPublicItem() => Ok(0);
        
        [HttpGet("v1/featured")]
        public IActionResult FeaturedClothing()
        {
            var items = CustomClothingDB.GetFeatured(20)
                .Select(x => x.ToDictionary())
                .ToList();

            return Ok(items);
        }
        
        [HttpGet("v1/hot")]
        public IActionResult HotClothing()
        {
            var items = CustomClothingDB.GetHot(20)
                .Select(x => x.ToDictionary())
                .ToList();

            return Ok(items);
        }

        [HttpGet("v1/isCreationAllowedForAccount")]
        public IActionResult IsCreationAllowedForAccount()
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            var player = PlayerDB.Players.FindById(playerId.Value);
            if (player == null)
                return Unauthorized("");

            bool allowed = player.PlayerRoles.Contains(PlayerDBClasses.PlayerRoles.Developer);

            return Ok(new { Success = allowed });
        }

        [HttpGet("/econ/customAvatarItems/v1/owned")]
        public IActionResult Owned([FromQuery] int skip = 0, [FromQuery] int take = 100)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            int total = CustomClothingDB.CountByCreator(playerId.Value, true);
            var items = CustomClothingDB.GetByCreator(playerId.Value, true)
                .Skip(skip)
                .Take(take)
                .Select(x => x.ToDictionary());

            return Ok(new { Results = items, TotalResults = total });
        }

        [HttpGet("v2/fromCreator/{accId}")]
        public IActionResult FromCreator(long accId, [FromQuery] int skip = 0, [FromQuery] int take = 100)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            bool isCreator = playerId.Value == accId;
            int total = CustomClothingDB.CountByCreator(accId, isCreator);
            var items = CustomClothingDB.GetByCreator(accId, isCreator)
                .Skip(skip)
                .Take(take)
                .Select(x => x.ToDictionary());

            return Ok(new { Results = items, TotalResults = total });
        }

        [HttpPost("v1")]
        public async Task<IActionResult> Create(
            [FromForm(Name = "thumbnailImage"), Required] IFormFile thumbnailImage,
            [FromForm(Name = "design"), Required] IFormFile design,
            [FromForm(Name = "metadata"), Required] string metaJson)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            var player = PlayerDB.Players.FindById(playerId.Value);
            if (player == null)
                return Unauthorized("");

            bool canCreate = player.PlayerRoles.Contains(PlayerDBClasses.PlayerRoles.Developer);
            if (!canCreate)
                return StatusCode(StatusCodes.Status403Forbidden);

            CustomClothingMetaRequest? meta;
            try
            {
                meta = JsonSerializer.Deserialize<CustomClothingMetaRequest>(metaJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (meta == null)
                    return BadRequest("Invalid metadata format.");
            }
            catch (Exception ex)
            {
                return BadRequest($"Metadata is not valid JSON. {ex}");
            }

            string? thumbFilename = await SaveUploadedFileAsync(thumbnailImage, (long)playerId);
            if (thumbFilename == null)
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to save thumbnail image.");

            string? designFilename = await SaveUploadedFileAsync(design, (long)playerId);
            if (designFilename == null)
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to save design image.");

            var item = new CustomClothingItem
            {
                CreatorAccountId = playerId.Value,
                Name = meta.Name,
                Description = meta.Description,
                Price = meta.Price,
                BaseAvatarItemId = meta.BaseAvatarItemId,
                BaseAvatarItemColor = meta.BaseAvatarItemColor,
                Accessibility = meta.Accessibility,
                ThumbnailImageFilename = thumbFilename,
                DesignFilename = designFilename
            };

            CustomClothingDB.Insert(item);
            return Ok(new { Success = true, Value = item.ToDictionary() });
        }

        [HttpPut("v1/design")]
        public async Task<IActionResult> UploadDesign(
            [FromForm(Name = "customAvatarItemId"), Required] Guid customAvatarItemId,
            [FromForm(Name = "design"), Required] IFormFile designImage)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            var item = CustomClothingDB.FindById(customAvatarItemId);
            if (item == null)
                return NotFound("Clothing item not found.");

            if (item.CreatorAccountId != playerId.Value)
                return StatusCode(StatusCodes.Status403Forbidden, "You do not own this item.");

            string? designFilename = await SaveUploadedFileAsync(designImage, (long)playerId);
            if (designFilename == null)
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to save design image.");

            item.DesignFilename = designFilename;
            CustomClothingDB.Update(item);

            return Ok(new { Success = true, Value = item.ToDictionary() });
        }

        [HttpPost("v1/bulk")]
        public IActionResult Bulk([FromForm(Name = "customAvatarItemIds")] List<Guid> customAvatarItemIds)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            var items = CustomClothingDB.GetBulk(customAvatarItemIds);

            return Ok(items.Select(x => x.ToDictionary()).ToList());
        }

        [HttpGet("v1/{customAvatarItemId}")]
        public IActionResult Get(Guid customAvatarItemId)
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized("");

            var item = CustomClothingDB.FindById(customAvatarItemId);

            if (item == null)
                return NotFound("");

            return Ok(item.ToDictionary());
        }

        private static async Task<string?> SaveUploadedFileAsync(IFormFile? file, long id)
        {
            if (file == null || file.Length == 0)
            {
                Console.WriteLine("Upload attempted with an empty or null file.");
                return null;
            }

            try
            {
                if (!file.ContentType.StartsWith("image/"))
                {
                    Console.WriteLine("Rejected file upload: Not an image. Content-Type: {ContentType}", file.ContentType);
                    return null;
                }

                string dir = Path.Combine(Program.dataDir, "Images", "player_shirts", id.ToString());

                Directory.CreateDirectory(dir);

                string filename = $"{Guid.NewGuid()}_customshirt.png";
                string fullPath = Path.Combine(dir, filename);

                using var stream = new FileStream(
                    fullPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true);

                await file.CopyToAsync(stream).ConfigureAwait(false);

                return filename;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while saving the uploaded file. {ex.Message}");
                return null;
            }
        }
    }
}