using System.Text;
using System.Text.RegularExpressions;

namespace Vanadium.Classes
{
    public class Sanitize
    {
        public string Context { get; set; } = "RoomChat";
        public int Intent { get; set; } = 1;
        public bool PreRemoveBlockedCharacters { get; set; } = true;
        public string ReplacementChar { get; set; } = "*";
        public string Value { get; set; } = "";
        public int ruleset { get; set; } = 0;

        private const char MaskChar = '*';


        private static readonly HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase)
        {
            "password",
            "pass",
            "class",
            "assistant",
            "account"
        };

        private static readonly List<(string Word, Regex Pattern)> SwearPatterns;

        static Sanitize()
        {
            var swears = File.ReadAllLines("Data/APIS/swears.txt")
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            SwearPatterns = swears
                .Select(word => (word, BuildFuzzyRegex(word)))
                .ToList();
        }

        public static bool ContainsSwears(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            if (ServerConfig.AllowSwears)
                return false;

            var normalized = Normalize(message);

            return SwearPatterns.Any(p => p.Pattern.IsMatch(normalized));
        }

        public static string SanitizeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            var tokens = Regex.Split(message, @"(\W+)");
            var sb = new StringBuilder(message.Length);

            foreach (var token in tokens)
            {
                if (!IsWord(token))
                {
                    sb.Append(token);
                    continue;
                }

                var normalized = Normalize(token);

                if (normalized.Length < 3)
                {
                    sb.Append(token);
                    continue;
                }

                if (Whitelist.Contains(normalized))
                {
                    sb.Append(token);
                    continue;
                }

                bool replaced = false;

                foreach (var (_, pattern) in SwearPatterns)
                {
                    if (pattern.IsMatch(normalized))
                    {
                        sb.Append(new string(MaskChar, token.Length));
                        //Console.WriteLine($"[Sanitize] Censored word in message: '{token}'");
                        replaced = true;
                        break;
                    }
                }

                if (!replaced)
                    sb.Append(token);
            }

            return sb.ToString();
        }
        
        private static bool IsWord(string input) =>
            input.Any(char.IsLetterOrDigit);

        private static Regex BuildFuzzyRegex(string word)
        {
            var pattern = string.Join(".{0,1}",
                word.Select(c => Regex.Escape(c.ToString())));

            return new Regex(
                $"^{pattern}$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            );
        }

        private static string Normalize(string input)
        {
            input = input.ToLowerInvariant();

            input = Regex.Replace(input, @"[0@]", "o");
            input = Regex.Replace(input, @"[1!|]", "i");
            input = Regex.Replace(input, @"[$5]", "s");
            input = Regex.Replace(input, @"3", "e");
            input = Regex.Replace(input, @"7", "t");

            input = Regex.Replace(input, @"[^a-z]", "");

            input = Regex.Replace(input, @"(.)\1{2,}", "$1$1");

            return input;
        }
    }
}