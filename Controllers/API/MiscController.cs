using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration.UserSecrets;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Classes.Rooms;
using System.Text;
using System.Text.Json;
using static Vanadium.Classes.DBs.DBClasses.EventDBClasses;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
namespace Vanadium.Controllers
{
    [Route("/")]
    public class MiscController : ControllerBase
    {
        [HttpPost("data/event")]
        public async Task<IActionResult> CollectionEvent()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized("");
            var (xpAwarded, leveledUp, newLevel, newXP) = await PlayerDB.AwardHeartbeatXP(id.Value);
            return Ok();
        }
        
        [HttpPost("api/gamesight/event")]
        public IActionResult Gamesight()
        {
        	return Ok(new { gay = true });
        }

        [HttpGet("actionlink/{code}")]
		public IActionResult GetActionLink(string code)
		{
    		var id = AuthStuff.GetPlayerId(Request);
    		if (id == null) return Unauthorized("");
    		return Ok(ActionLinkDB.GetActionLinkResponse(code));
        }

        [HttpPost("actionlink")]
		public async Task<IActionResult> CreateActionLink()
		{
    		var id = AuthStuff.GetPlayerId(Request);
    		if (id == null) return Unauthorized("");
            
    		var form = await Request.ReadFormAsync();
            
    		if (!int.TryParse(form["validHours"], out int validHours)) return BadRequest();
    		if (!int.TryParse(form["codeType"], out int codeType)) return BadRequest();
            
    		int? maxCount = int.TryParse(form["maxCount"], out int mc) ? mc : null;
    		int extraDataId = int.TryParse(form["extraDataId"], out int ed) ? ed : 0;
            
    		var data = form["data"].ToString();
    		var link = ActionLinkDB.CreateActionLink(id.Value, data, codeType, extraDataId, validHours);
    		if (link == null) return BadRequest();
            
    		return Ok($"https://reloxa.xyz/p/Share?a={link.Code}");
		}
        
        [HttpGet("datalink/{code}")]
		public IActionResult GetDataLink(string code)
		{
    		var id = AuthStuff.GetPlayerId(Request);
    		if (id == null) return Unauthorized("");
    		var decoded = System.Net.WebUtility.UrlDecode(code);
    		return Ok(ActionLinkDB.GetActionLinkResponse(decoded));
		}

        [HttpPost("actionlink/{code}/consume")]
		public async Task<IActionResult> ConsumeActionLink(string code)
		{
    		var id = AuthStuff.GetPlayerId(Request);
    		if (id == null) return Unauthorized("");
            
   			var form = await Request.ReadFormAsync();
            
    		bool newPlayer = form["newPlayer"] == "true";
    		bool newInstall = form["newInstall"] == "true";
            
    		return Ok(ActionLinkDB.GetActionLinkResponse(code));
		}
    }
}