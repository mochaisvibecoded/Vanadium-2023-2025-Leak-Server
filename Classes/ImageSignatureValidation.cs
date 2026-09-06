using System.Security.Cryptography;
using System.Text;
using Vanadium.Classes;

namespace Vanadium.Utils
{
    public static class ImageSignatureValidation
    {
        public static string GenerateCacheKey(string path, int? w, int? h, bool crop)
        {
            string rawKey = $"{path}_{w}_{h}_{crop}";
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
            return Convert.ToHexString(hashBytes);
        }

        public static string SignPayload(byte[] payload, string sig)
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(ServerConfig.RsaParams);

            byte[] signatureBytes = rsa.SignData(payload, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            string base64Signature = Convert.ToBase64String(signatureBytes);

            return $"key-id=KEY:RSA:{sig}.rec.net; data={base64Signature};";
        }
    }
}