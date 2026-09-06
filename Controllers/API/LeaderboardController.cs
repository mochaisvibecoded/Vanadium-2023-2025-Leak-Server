using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes.DBs;
using static Vanadium.Classes.DBs.DBClasses.LeaderboardDBClasses;

namespace Vanadium.Controllers
{
    [ApiController]
    public class LeaderboardController : ControllerBase // Sort of buggy, uh its get the job done
    {
        [HttpPost("/leaderboard/GetNearbyScores")]
        public IActionResult GetNearbyScores([FromBody] GetNearbyScoresRequest request)
        {
            if (request == null)
                return BadRequest();

            var response = LeaderboardDB.GetNearbyScores(request);
            return Ok(response);
        }

        [HttpPost("/leaderboard/GetPlayerRank")]
        public IActionResult GetPlayerRank([FromBody] GetRanksRequest request)
        {
            if (request == null)
                return BadRequest();

            var result = LeaderboardDB.GetPlayerRank(request);
            return Ok(result);
        }

        [HttpPost("/leaderboard/GetRanks")]
        public IActionResult GetRanks([FromBody] GetRanksRequest request)
        {
            if (request == null)
                return BadRequest();

            var response = LeaderboardDB.GetRanks(request);
            return Ok(response);
        }

		[HttpPost("/leaderboard/CheckAndSetStat")]
		public async Task<IActionResult> CheckAndSetStat([FromBody] CheckAndSetStatRequest request)
		{
    		var id = AuthStuff.GetPlayerId(Request);
    		if (id == null)
        		return Unauthorized("");
    		if (request == null)
        		return BadRequest();
    		var response = await LeaderboardDB.CheckAndSetStat(id.Value, request);
    		return Ok(response);
		}
    }
}