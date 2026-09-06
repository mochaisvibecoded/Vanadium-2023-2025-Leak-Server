using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;

namespace VanadiumGuard.Server.Challenges
{
    /// <summary>
    /// Supplies the known-good bytes a challenge answer is checked against.
    /// </summary>
    public interface IReferenceImageStore
    {
        /// <summary>Returns the reference bytes, or null when this build or region is unknown.</summary>
        byte[]? Read(string gameBuild, string module, uint rva, int length);

        /// <summary>Size of the reference image, used to pick a valid random offset.</summary>
        long Size(string gameBuild, string module);
    }

    /// <summary>
    /// Reads reference images from disk, laid out as
    /// <c>{ReferenceImageDirectory}/{gameBuild}/{module}.bin</c>.
    /// <para>
    /// Produce those files from the shipped binaries as part of your build pipeline: dump
    /// each module's mapped image once, on a clean machine, and publish it alongside the
    /// build. Without them the server cannot verify a challenge answer, and challenges
    /// degrade to a liveness check only.
    /// </para>
    /// </summary>
    public sealed class FileReferenceImageStore : IReferenceImageStore
    {
        private readonly ILogger<FileReferenceImageStore> _logger;
        private readonly string _root;
        private readonly ConcurrentDictionary<string, byte[]?> _cache = new ConcurrentDictionary<string, byte[]?>();

        public FileReferenceImageStore(ILogger<FileReferenceImageStore> logger, string root)
        {
            _logger = logger;
            _root = root;
        }

        public byte[]? Read(string gameBuild, string module, uint rva, int length)
        {
            byte[]? image = Image(gameBuild, module);
            if (image == null)
                return null;

            if (rva + (uint)length > image.Length)
                return null;

            var slice = new byte[length];
            Buffer.BlockCopy(image, (int)rva, slice, 0, length);
            return slice;
        }

        public long Size(string gameBuild, string module)
        {
            return Image(gameBuild, module)?.LongLength ?? 0;
        }

        private byte[]? Image(string gameBuild, string module)
        {
            // Both components land in a file path, so anything that could escape the root
            // is rejected outright rather than sanitised.
            if (!IsSafeComponent(gameBuild) || !IsSafeComponent(module))
            {
                _logger.LogWarning("Rejected reference lookup for build={Build} module={Module}", gameBuild, module);
                return null;
            }

            string key = gameBuild + "/" + module;

            return _cache.GetOrAdd(key, _ =>
            {
                string path = Path.Combine(_root, gameBuild, module + ".bin");

                if (!File.Exists(path))
                {
                    _logger.LogWarning("No reference image at {Path}; challenges for it cannot be verified", path);
                    return null;
                }

                try
                {
                    return File.ReadAllBytes(path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read reference image {Path}", path);
                    return null;
                }
            });
        }

        private static bool IsSafeComponent(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                return false;

            foreach (char c in value)
            {
                bool ok = char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_';
                if (!ok)
                    return false;
            }

            return !value.Contains("..", StringComparison.Ordinal);
        }
    }
}
