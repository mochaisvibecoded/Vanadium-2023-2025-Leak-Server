using System.Security.Cryptography;
using System.Text.Json;

namespace Vanadium.Classes
{
    public class ServerConfig
    {
        private static string ConfigPath => Path.Combine(Environment.CurrentDirectory, "Configs", "ServerConfig.json");

        private static ConfigData Load()
        {
            if (!File.Exists(ConfigPath))
                throw new FileNotFoundException("ServerConfig.json not found (this should never happen) " + ConfigPath);
            return JsonSerializer.Deserialize<ConfigData>(File.ReadAllText(ConfigPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }

        public static void Reload() => _ = Load();

        public static object Bracket => new List<object>();

        public static string BaseURL => Load().BaseURL;
        public static int GameVersion => Load().GameVersion;
        public static string ImagesWebhook => Load().ImagesWebhook;
        public static string InventionsReportWebhook => Load().InventionsReportWebhook;
        public static string AntiCheatWebhook => Load().AntiCheatWebhook;
        public static string PhotonWebhook => Load().PhotonWebhook;
        public static string BotToken => Load().BotToken;
        public static string LinkBotToken => Load().LinkBotToken;
        public static string CountBotToken => Load().CountBotToken;
        public static string RoomCommentsWebhook => Load().RoomCommentsWebhook;
        public static string BotStatus => Load().BotStatus;
        public static string SteamWebApiKey => Load().SteamWebApiKey;
        public static string OculusAppSecret => Load().OculusAppSecret;
        public static bool OculusAuth => Load().OculusAuth;
        public static bool EquipmentAntiCheatBanEnabled => Load().EquipmentAntiCheatBanEnabled;
        public static bool AutoAccount => Load().AutoAccount;
        public static bool CA_Auth => Load().CA_Auth;
        public static bool UseStatSig => Load().UseStatSig;
        public static bool PostingEnabled => Load().PostingEnabled;
        public static bool AllowSwears => Load().AllowSwears;
        public static bool DevsOnlyToken => Load().DevsOnlyToken;

        /// <summary>Master switch for the Bluetint patch requirement. Off changes nothing.</summary>
        public static bool RequirePatchToPlay => Load().RequirePatchToPlay;

        /// <summary>Seconds a fresh session has to check in before it is kicked.</summary>
        public static int PatchGraceSeconds => Load().PatchGraceSeconds;

        /// <summary>Shown at the refused login and on the kick dialog.</summary>
        public static string PatchRequiredMessage => Load().PatchRequiredMessage;

        /// <summary>
        /// Require the patch to keep checking in, not just announce itself once. Only turn
        /// this on once you know the shipped client re-submits on a timer; if it does not,
        /// this kicks every honest player a few minutes into their session.
        /// </summary>
        public static bool PatchRequiresPeriodicCheckIn => Load().PatchRequiresPeriodicCheckIn;

        /// <summary>Require proof of the patch in the login request itself. Needs client support.</summary>
        public static bool RequirePatchHeader => Load().RequirePatchHeader;

        public static string PatchHeaderSecret => Load().PatchHeaderSecret;
        public static bool LetPCPlay => Load().LetPCPlay;
        public static long PCMaximumIdForPlay => Load().PCMaximumIdForPlay;

        public static HashSet<ulong> WhitelistedSteamIds => Load().WhitelistedSteamIds.ToHashSet();
        public static HashSet<ulong> BlacklistedPlatformIds => Load().BlacklistedPlatformIds.ToHashSet();

        public static bool IsSteamIdWhitelisted(ulong steamId) => true;

        public static RSAParameters RsaParams
        {
            get
            {
                var cfg = Load();
                return new RSAParameters
                {
                    Modulus  = Convert.FromBase64String(cfg.RsaModulus),
                    Exponent = Convert.FromBase64String(cfg.RsaExponent),
                    P        = Convert.FromBase64String(cfg.RsaP),
                    Q        = Convert.FromBase64String(cfg.RsaQ),
                    DP       = Convert.FromBase64String(cfg.RsaDP),
                    DQ       = Convert.FromBase64String(cfg.RsaDQ),
                    InverseQ = Convert.FromBase64String(cfg.RsaInverseQ),
                    D        = Convert.FromBase64String(cfg.RsaD)
                };
            }
        }

        private class ConfigData
        {
            public string BaseURL { get; set; } = "";
            public int GameVersion { get; set; }
            public string ImagesWebhook { get; set; } = "";
            public string InventionsReportWebhook { get; set; } = "";
            public string AntiCheatWebhook { get; set; } = "";
            public string PhotonWebhook { get; set; } = "";
            public string RoomCommentsWebhook { get; set; } = "";
            public string BotToken { get; set; } = "";
            public string LinkBotToken { get; set; } = "";
            public string CountBotToken { get; set; } = "";
            public string BotStatus { get; set; } = "";
            public string SteamWebApiKey { get; set; } = "";
            public string OculusAppSecret { get; set; } = "";
            public bool OculusAuth { get; set; } = true;
            public bool EquipmentAntiCheatBanEnabled { get; set; }
            public bool AutoAccount { get; set; }
            public bool CA_Auth { get; set; }
            public bool UseStatSig { get; set; }
            public bool PostingEnabled { get; set; }
            public bool AllowSwears { get; set; }
            public bool DevsOnlyToken { get; set; }
            public bool RequirePatchToPlay { get; set; } = false;
            public int PatchGraceSeconds { get; set; } = 180;
            public string PatchRequiredMessage { get; set; } = "No skidding. Download the Bluetint patch";
            public bool PatchRequiresPeriodicCheckIn { get; set; } = false;
            public bool RequirePatchHeader { get; set; } = false;
            public string PatchHeaderSecret { get; set; } = "";
            public List<ulong> WhitelistedSteamIds { get; set; } = new();
            public List<ulong> BlacklistedPlatformIds { get; set; } = new();
            public string PhotonRealtimeId { get; set; } = "";
            public string PhotonVoiceId { get; set; } = "";
            public string PhotonRegion { get; set; } = "us";
            public string RsaModulus { get; set; } = "";
            public string RsaExponent { get; set; } = "";
            public string RsaP { get; set; } = "";
            public string RsaQ { get; set; } = "";
            public string RsaDP { get; set; } = "";
            public string RsaDQ { get; set; } = "";
            public string RsaInverseQ { get; set; } = "";
            public string RsaD { get; set; } = "";
            public bool LetPCPlay { get; set; } = false;
            public long PCMaximumIdForPlay { get; set; } = 3000;
        }
    }
}

/* rsa key

sq/7i1nBoHMmlUInLsaGlqrVgwbmE/u+O5RLfJMCkGvHhfPrBz8tOEmujbKFuCQOWZDA8tqD3s6aWo/mSRh7NGfs80eArL/LIs8sebwZkPQx0buRbtVgWggqtI68lZQQIY4jUTCEgYcl9jNoXD+9zCt5p/NUoplg0plga1QhyHfEnGmSVt8bNsx5Zynz+Gg+8GNkxvcCwiGNcH7Se1v+mx0m/ZENFl7OGlOA9go7aG0HaTayeo2Hc7vGfVLUxw3PNiEbC5MJcTPoqYY9dQnRBaPdMAwz3coXur2//Lf3lA+ijWIFhpnAAuciLYRRcbR7l3b/ikRqC3xdvsVQ9/3CvQ==

*/
