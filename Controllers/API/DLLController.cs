using Microsoft.AspNetCore.Mvc;
using Vanadium.Auth;
using Vanadium.Classes;
using Vanadium.Classes.DBs;
using Vanadium.Classes.DBs.DBClasses;
using Vanadium.Hubs;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;
using Vanadium.Utils.NotiController;

namespace Vanadium.Controllers
{
    [ApiController]
    public class DLLController : ControllerBase
    {
        private const string CurrentVersion = "1.0.0";
        private const string DllFileName = "Vanadium.Patch.dll";

        [HttpGet("/version")]
        public IActionResult GetVersion()
        {
            var downloadUrl = $"{Request.Scheme}://{Request.Host}/download/{DllFileName}";

            return Ok(new UpdateManifest
            {
                version = CurrentVersion,
                downloadUrl = downloadUrl
            });
        }

        [HttpGet("/download/{fileName}")]
        public IActionResult DownloadDll(string fileName)
        {
            if (fileName != DllFileName)
                return NotFound("");

            var dllDir = Path.Combine(Directory.GetCurrentDirectory(), "UpdateFiles");
            var filePath = Path.Combine(dllDir, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound("");

            var bytes = System.IO.File.ReadAllBytes(filePath);
            return File(bytes, "application/octet-stream", fileName);
        }

        public class UpdateManifest
        {
            public string version { get; set; } = string.Empty;
            public string downloadUrl { get; set; } = string.Empty;
        }
    }
}