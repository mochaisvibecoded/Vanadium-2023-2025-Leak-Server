using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;

namespace Vanadium.Controllers
{
    [Route("/")]
    public class CuratedListsController : ControllerBase
    {
    
    	[HttpGet("curatedlists")]
        public async Task<IActionResult> GetCuratedlists([FromQuery] ulong creatorAccountId, [FromQuery] ulong type, [FromQuery] string name)
        {
            string directory = Path.Combine(
                Program.dataDir, "CuratedLists", $"{creatorAccountId}_{type}_{name}.json"
            );

            if (!System.IO.File.Exists(directory))
                return NotFound();

            return PhysicalFile(directory, "application/json");
        }
    }
}