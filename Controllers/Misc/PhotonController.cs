//using Microsoft.AspNetCore.Mvc;
//using Vanadium.Auth;
//using Vanadium.Classes;
//using Vanadium.Classes.DBs;
//using System.Text;
//using System.Text.Json;
//using System.Text.Json.Serialization;
//using Vanadium.Classes;

//namespace Vanadium.Controllers
//{
//    [ApiController]
//    public class PhotonController : ControllerBase
//    {
//        private readonly HttpClient _httpClient = new();
        
//        private bool IsRealClient()
//        {
//            var accept = Request.Headers["Accept"].ToString();
//            var userAgent = Request.Headers["User-Agent"].ToString();

//            return userAgent == "BestHTTP"
//                && accept.Contains("application/json")
//                && accept.Contains("image/png")
//                && accept.Contains("image/jpeg");
//        }

//        private async Task SendFakeClientWebhook(string ip, string headersPretty, string body)
//        {
//            try
//            {
//                const int maxLen = 950;
//                var truncatedHeaders = headersPretty.Length > maxLen
//                    ? headersPretty.Substring(0, maxLen) + "\n... (truncated)"
//                    : headersPretty;

//                var embed = new
//                {
//                    title = "⚠️ Fake Client Hit AppSettings (served decoy)",
//                    color = 15158332,
//                    fields = new object[]
//                    {
//                        new { name = "IP", value = $"`{ip}`", inline = false },
//                        new { name = "Headers", value = $"```\n{truncatedHeaders}\n```", inline = false },
//                        new { name = "Body", value = $"json```\n{body}\n```", inline = false }
//                    },
//                    timestamp = DateTime.UtcNow.ToString("O")
//                };
//                var payload = new
//                {
//                    embeds = new[] { embed }
//                };
//                var content = new StringContent(
//                    JsonSerializer.Serialize(payload),
//                    Encoding.UTF8,
//                    "application/json"
//                );
//                await _httpClient.PostAsync(ServerConfig.PhotonWebhook, content);
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Fake client webhook error: {ex.Message}");
//            }
//        }
        
//       [HttpGet("/photon/appSettings")]
//        public IActionResult GetPhoton()
//        {
//			return NotFound("");
//            Console.WriteLine("photon called");
//            if (!IsRealClient()) // dont touch this a fix for the photon botter botting us
//            {
//                var fakeIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
//                var fakeHeaders = GetHeadersPretty();
//                var decoy = new AppSettings
//                {
//                    PhotonRealtimeId = "aaae496f-ece0-4b1b-82ac-aea17d766c82",
//                    PhotonVoiceId = "d1b950c8-95e4-4725-9a3b-f6fc57853484",
//                    PhotonRegion = "us"
//                };
//                string decoyJson = JsonSerializer.Serialize(decoy, new JsonSerializerOptions { WriteIndented = true });
//                _ = SendFakeClientWebhook(fakeIp, fakeHeaders, decoyJson);
//                return Ok(decoy);
//            }

//    var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
//    var settings = ServerConfig.appSettings;

//    string settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
//    string headersPretty = GetHeadersPretty();

//    _ = SendAppSettingsWebhook(ip, headersPretty, settingsJson);
//    return Ok(settings);
//}
        
//        private string GetHeadersPretty()
//        {
//            var sb = new StringBuilder();
//            foreach (var header in Request.Headers)
//            {
//                sb.AppendLine($"{header.Key}: {header.Value}");
//            }
//            return sb.ToString();
//        }
        
//        [HttpPost("/photon/{url}")]
//        public async Task<IActionResult> stub(string url)
//        {
//            return await HandleStub("", "/photon", url);
//        }

//        [HttpPost("/photon_realtime/{url}")]
//        public async Task<IActionResult> stubRealtime(string url)
//        {
//            return await HandleStub("Realtime", "/photon_realtime", url);
//        }

//        [HttpPost("/photon_voice/{url}")]
//        public async Task<IActionResult> stubVoice(string url)
//        {
//            return await HandleStub("Voice", "/photon_voice", url);
//        }

//        private async Task<IActionResult> HandleStub(string type, string urltype, string url)
//        {
//            Request.EnableBuffering();
//            string rawBody;
//            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
//            {
//                rawBody = await reader.ReadToEndAsync();
//                Request.Body.Position = 0;
//            }
//            string prettyJson = rawBody;
//            try
//            {
//                if (!string.IsNullOrWhiteSpace(rawBody))
//                {
//                    using var doc = JsonDocument.Parse(rawBody);
//                    prettyJson = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
//                }
//            }
//            catch (JsonException)
//            {
//            }
//            Console.WriteLine($"Stub called: url={urltype}/{url}, body={rawBody}");
//            await SendStubWebhook(type, urltype, url, prettyJson);
//            return Ok(new
//            {
//                ResultCode = 0
//            });
//        }
        
// private async Task SendAppSettingsWebhook(string ip, string headersPretty, string bodyValue)
//{
//    try
//    {
//        const int maxLen = 950;
//        var truncatedHeaders = headersPretty.Length > maxLen
//            ? headersPretty.Substring(0, maxLen) + "\n... (truncated)"
//            : headersPretty;

//        var embed = new
//        {
//            title = "Photon GET AppSettings Called",
//            color = 15105570,
//            fields = new object[]
//            {
//                new { name = "IP", value = $"`{ip}`", inline = false },
//                new { name = "Headers", value = $"json```\n{truncatedHeaders}\n```", inline = false },
//                new { name = "Body", value = $"json```\n{bodyValue}\n```", inline = false }
//            },
//            timestamp = DateTime.UtcNow.ToString("O")
//        };
//        var payload = new
//        {
//            embeds = new[] { embed }
//        };
//        var content = new StringContent(
//            JsonSerializer.Serialize(payload),
//            Encoding.UTF8,
//            "application/json"
//        );
//        var response = await _httpClient.PostAsync(ServerConfig.PhotonWebhook, content);
//    }
//    catch (Exception ex)
//    {
//    }
//}
//        private async Task SendStubWebhook(string type, string urltype, string url, string prettyJson)
//        {
//            try
//            {
//                const int maxLen = 950;
//                var body = string.IsNullOrWhiteSpace(prettyJson) ? "(empty)" : prettyJson;
//                var truncated = body.Length > maxLen
//                    ? body.Substring(0, maxLen) + "\n... (truncated)"
//                    : body;
//                var bodyValue = body == "(empty)"
//                    ? "(empty)"
//                    : $"```json\n{truncated}\n```";
//                var embed = new
//                {
//                    title = $"Photon {type} Called",
//                    color = 15105570,
//                    fields = new object[]
//                    {
//                        new { name = "URL", value = $"`{urltype}/{url}`", inline = false },
//                        new { name = "Body", value = bodyValue, inline = false }
//                    },
//                    timestamp = DateTime.UtcNow.ToString("O")
//                };
//                var payload = new
//                {
//                    embeds = new[] { embed }
//                };
//                var content = new StringContent(
//                    JsonSerializer.Serialize(payload),
//                    Encoding.UTF8,
//                    "application/json"
//                );
//                var response = await _httpClient.PostAsync(ServerConfig.PhotonWebhook, content);
//                Console.WriteLine($"Stub webhook sent: {response.StatusCode}");
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Stub webhook error: {ex.Message}");
//            }
//        }
//    }
//}