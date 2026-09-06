using System;
using System.Diagnostics;
using System.IO;

namespace Vanadium
{
    public static class Cloudflare
    {
        private static Process? _cloudflaredProcess;
        private static string _binaryPath = "";
        private static string _configPath = "";
        private static bool _restarting = false;

        public static void StartCloudflared()
        {
            try
            {
                string baseDir = Environment.CurrentDirectory;

                _binaryPath = Path.Combine(baseDir, "cloudflared");
                _configPath = Path.Combine(baseDir, ".cloudflared", "config.yml");
                string credentialsPath = Path.Combine(
                    baseDir,
                    ".cloudflared",
                    "8a133824-2cd3-4bb8-8ed3-2e99a17cd3b4.json"
                );

                if (!File.Exists(_binaryPath)) return;
                if (!File.Exists(_configPath)) return;
                if (!File.Exists(credentialsPath)) return;

                LaunchProcess(baseDir);
            }
            catch (Exception ex)
            {
            }
        }

        private static void LaunchProcess(string baseDir)
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                Process chmod = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{_binaryPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                chmod.Start();
                chmod.WaitForExit();
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = _binaryPath,
                Arguments = $"tunnel --config \"{_configPath}\" run reloxa",
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = baseDir
            };

            _cloudflaredProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            _cloudflaredProcess.OutputDataReceived += (_, e) => { };
            _cloudflaredProcess.ErrorDataReceived += (_, e) => { };

            _cloudflaredProcess.Exited += (_, _) =>
            {
                if (!_restarting)
                {
                    _restarting = true;
                    Thread.Sleep(3000);
                    _restarting = false;
                    LaunchProcess(baseDir);
                }
            };

            _cloudflaredProcess.Start();
            _cloudflaredProcess.BeginOutputReadLine();
            _cloudflaredProcess.BeginErrorReadLine();
        }

        public static void Stop()
        {
            try
            {
                _restarting = true;
                if (_cloudflaredProcess != null && !_cloudflaredProcess.HasExited)
                {
                    _cloudflaredProcess.Kill();
                    _cloudflaredProcess.WaitForExit();
                }
            }
            catch (Exception ex)
            {
            }
        }
    }
}