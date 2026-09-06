using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Utils.NotiController;
using static Vanadium.Classes.DBs.DBClasses.GiftsDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    [Route("/")]
    public partial class ChallengesController : ControllerBase
    {
    	[HttpPost("api/objectives/v1/cleargroup")]
        [HttpPost("api/objectives/v1/updateobjective")]
        public IActionResult updateobjective() => Ok();
    }
}