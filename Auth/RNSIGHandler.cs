using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Vanadium.Classes.DBs;
using Microsoft.AspNetCore.Http;
using static Vanadium.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Vanadium.Auth
{
    public static class RNSIGHandler
    {
        private static readonly ConcurrentDictionary<long, string> _playerKeys = new();

        public static string IssuePlayerKey(long accountId)
        {
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _playerKeys[accountId] = key;
            //Console.WriteLine($"[RNSIG] made a key for player={accountId} key={key}");
            return key;
        }
        public static string IssuePlayerFixedKey(long accountId, string key)
        {
            if (!HasPlayerKey(accountId))
            {
                _playerKeys[accountId] = key;
                //Console.WriteLine($"[RNSIG] made a fixed key for player={accountId} key={key}");
            }
            return key;
        }

        public static bool HasPlayerKey(long accountId)
        {
            return _playerKeys.ContainsKey(accountId);
        }

        private static string ComputeSignature(byte[] keyBytes, string uri, byte[] body)
        {
            using var hmac = new HMACSHA256(keyBytes);

            hmac.TransformBlock(Encoding.ASCII.GetBytes(uri), 0, Encoding.ASCII.GetByteCount(uri), null, 0);

            if (body != null && body.Length > 0)
            {
                byte[] lenBytes = BitConverter.GetBytes((uint)body.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(lenBytes);
                hmac.TransformBlock(lenBytes, 0, 4, null, 0);

                if (body.Length > 2048)
                {
                    int step = body.Length / 16;
                    for (int i = 0; i < 16; i++)
                    {
                        int offset = i * step;
                        int count = Math.Min(128, body.Length - offset);
                        hmac.TransformBlock(body, offset, count, null, 0);
                    }
                }
                else
                {
                    hmac.TransformBlock(body, 0, body.Length, null, 0);
                }
            }

            hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToBase64String(hmac.Hash!);
        }

        public static async Task<bool> ValidateRNSIG(HttpRequest request)
        {
            string method = request.Method.ToUpperInvariant();
            if (method != "POST" && method != "PUT")
                return true;

            var rnsig = request.Headers["X-RNSIG"].FirstOrDefault()?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(rnsig))
                return false;

            var auth = request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer "))
                return false;

            var playerId = AuthStuff.GetPlayerId(request);
            if (playerId == null)
                return false;
            
            var player = PlayerDB.Players.FindById(playerId.Value);
            bool isDeveloper = player.PlayerRoles?.Contains(PlayerRoles.Developer) ?? false;
            if (isDeveloper)
                return true;

            if (!_playerKeys.TryGetValue(playerId.Value, out var storedKeyB64) || string.IsNullOrWhiteSpace(storedKeyB64))
                return false;

            byte[] keyBytes = Convert.FromBase64String(storedKeyB64);

            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms);
            byte[] body = ms.ToArray();
            request.Body.Position = 0;

            string uri = request.Path.Value ?? "";
            string expected = ComputeSignature(keyBytes, uri, body);

            return string.Equals(expected, rnsig, StringComparison.Ordinal);
        }
    }
}