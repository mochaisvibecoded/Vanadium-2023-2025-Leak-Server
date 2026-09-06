using Microsoft.AspNetCore.Mvc;
using Vanadium.Classes;
    
namespace Vanadium.Controllers
{
	[Route("/")]
    [ApiController]
    public partial class RoomEconController : ControllerBase
    {
    	[HttpGet("econ/roomInventory/room/{roomId}")]
        public IActionResult roomInventoryGet() => Ok(ServerConfig.Bracket); // todo db
        
        [HttpGet("econ/roomOffer/room/{roomId}/purchaseCounts")]
        public IActionResult roomInventoryPurchaseCounts() => Ok(ServerConfig.Bracket); // todo db
    }
}
