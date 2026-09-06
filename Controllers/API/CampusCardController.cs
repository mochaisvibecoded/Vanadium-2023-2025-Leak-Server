using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes.DBs;
using Vanadium.Classes;
using Vanadium.Utils;
namespace Vanadium.Controllers
{
    [ApiController]
    [Route("/")]
    public partial class CampuscardController : ControllerBase
    {
        private static HashSet<long> GetWhitelistedPlayerIds()
        {
            var path = Path.Combine(Environment.CurrentDirectory, "Data", "APIS", "RRPlus.txt");
            if (!System.IO.File.Exists(path))
                return new HashSet<long>();
            var ids = new HashSet<long>();
            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                if (long.TryParse(line.Trim(), out var id))
                    ids.Add(id);
            }
            return ids;
        }

        [HttpPost("api/CampusCard/v1/UpdateAndGetSubscription")]
        public IActionResult UpdateAndGetSubscription()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
            if (!GetWhitelistedPlayerIds().Contains(id.Value))
                return Ok(new {});
            if (AuthStuff.GetAndEnsureAppVersion(Request) == "20250724" || AuthStuff.GetAndEnsureAppVersion(Request) == "20230616" || AuthStuff.GetAndEnsureAppVersion(Request) == "20240105")
                return Ok(new
            {
                    subscription = new {
        subscriptionId = 395739,
        recNetPlayerId = id.Value,
        platformType = 1,
        platformId = "24891688440416441",
        platformPurchaseId = (string?)null,
        @event = (string?)null,
        type = 0,
        level = 0,
        period = 0,
        expirationDate = "2050-01-01T00:00:00Z",
        isAutoRenewing = false,
        createdAt = "2023-02-14T23:35:54.6894998Z",
        modifiedAt = "2026-03-30T23:04:50.6260935Z"
    },
    platformAccountSubscribedPlayerId = (long?)null
            });
            return Ok(new
            {
                CanBuySubscription = false,
                PlatformAccountSubscribedPlayerId = (long?)null,
                Subscription = new
                {
                    CreatedAt = DateTime.MinValue.ToString("O"),
                    ExpirationDate = DateTime.MaxValue.ToString("O"),
                    IsActive = true,
                    IsAutoRenewing = true,
                    Level = 0,
                    ModifiedAt = DateTime.MinValue.ToString("O"),
                    Period = 0,
                    PlatformId = "1",
                    PlatformPurchaseId = (string?)null,
                    PlatformType = 1,
                    RecNetPlayerId = id.Value,
                    SubscriptionId = 0
                }
            });
        }
        
        [HttpGet("acc/emojiConfig/whitelistedEmojis")]
        public async Task<IActionResult> whitelistedEmojisConfig()
        {
            return Ok(Emojis.emojis);
        }

        [HttpGet("api/CampusCard/v1/SignUpBonus")]
        public async Task<IActionResult> SignUpBonus()
        {
            return Ok(new { success = false, value = "Bro? You skid‼😡 I really am gay.! 😡‼😡‼😡‼😡‼" });
        }
    }
}