using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using System.Security.Cryptography.X509Certificates;

namespace Vanadium.Controllers
{
    [ApiController]
    public partial class AnnouncementsController : ControllerBase
    {
        [HttpGet("/api/announcement/v1/get")]
        public async Task<IActionResult> AnnouncementV1Get()
        {
            return Ok(AnnoucementDB.GetAllAnnouncements() ?? ServerConfig.Bracket);
        }
        
        // todo db for each
        
		[HttpGet("v2/subscription/mine/unread")]
        [HttpGet("v2/mine/unread")]
		[HttpGet("v1/get")]
		public async Task<IActionResult> SubscriptionAndALlat()
		{
			return Ok(ServerConfig.Bracket);
		}
    }
}
