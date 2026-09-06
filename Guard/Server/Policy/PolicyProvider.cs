using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VanadiumGuard.Protocol;

namespace VanadiumGuard.Server.Policy
{
    /// <summary>
    /// Serves the policy handed to clients at session open, reloading it from disk when the
    /// file changes so rules can be adjusted without a deploy.
    /// </summary>
    public sealed class PolicyProvider
    {
        private readonly ILogger<PolicyProvider> _logger;
        private readonly string _path;
        private GuardPolicy _policy;
        private DateTime _loadedStamp;

        public PolicyProvider(ILogger<PolicyProvider> logger, string policyDirectory)
        {
            _logger = logger;
            _path = Path.Combine(policyDirectory, "policy.json");
            _policy = Load();
        }

        public GuardPolicy Current
        {
            get
            {
                ReloadIfChanged();
                return _policy;
            }
        }

        private void ReloadIfChanged()
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                DateTime stamp = File.GetLastWriteTimeUtc(_path);
                if (stamp <= _loadedStamp)
                    return;

                _policy = Load();
            }
            catch (Exception ex)
            {
                // Keep serving the last good policy. A malformed edit must not take the
                // whole fleet offline.
                _logger.LogError(ex, "Policy reload failed; continuing with the previous policy");
            }
        }

        private GuardPolicy Load()
        {
            if (!File.Exists(_path))
            {
                _logger.LogWarning("No policy at {Path}; writing the built-in default", _path);

                GuardPolicy fallback = Default();

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.WriteAllText(_path, JsonSerializer.Serialize(
                        fallback, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not write the default policy to {Path}", _path);
                }

                _loadedStamp = DateTime.UtcNow;
                return fallback;
            }

            try
            {
                GuardPolicy? loaded = JsonSerializer.Deserialize<GuardPolicy>(
                    File.ReadAllText(_path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (loaded == null)
                    throw new InvalidOperationException("policy.json deserialised to null");

                _loadedStamp = File.GetLastWriteTimeUtc(_path);
                _logger.LogInformation("Loaded policy version {Version}", loaded.PolicyVersion);
                return loaded;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Policy at {Path} is invalid; using the built-in default", _path);
                _loadedStamp = DateTime.UtcNow;
                return Default();
            }
        }

        /// <summary>
        /// The shipped starting point.
        /// <para>
        /// Note what is missing relative to the old hard-coded lists. There is no filesystem
        /// scanner, so nothing looks at what a player keeps in Documents or Downloads; that
        /// was a large privacy cost for a signal that only ever proved someone had once
        /// downloaded a tool. Generic process names are gone. Reverse-engineering tools that
        /// developers use every day are present but weighted low, because owning IDA is not
        /// evidence of cheating in a match.
        /// </para>
        /// </summary>
        public static GuardPolicy Default()
        {
            return new GuardPolicy
            {
                PolicyVersion = "2024.09.05-1",
                ReportIntervalMs = 5000,
                HeartbeatIntervalMs = 15000,

                Detectors = new[]
                {
                    new DetectorSettings { Name = "module", Enabled = true, MaxConfidence = 100 },
                    new DetectorSettings { Name = "process", Enabled = true, MaxConfidence = 60 },
                    new DetectorSettings { Name = "window", Enabled = true, MaxConfidence = 60 },
                    new DetectorSettings { Name = "debugger", Enabled = true, MaxConfidence = 100 },
                    new DetectorSettings { Name = "nativehook", Enabled = true, MaxConfidence = 100 },

                    // Starts in observe-only mode. Every player with a second mod installed
                    // trips this, so it reports for a while and earns its confidence later.
                    new DetectorSettings { Name = "managedpatch", Enabled = true, MaxConfidence = 0 },

                    new DetectorSettings { Name = "il2cpp", Enabled = true, MaxConfidence = 100 },
                },

                ModuleAllowPrefixes = new[]
                {
                    "vanadiumguard", "melonloader", "0harmony", "il2cpp", "unityplayer", "unityengine",
                    "gameassembly", "baselib", "unitycrashhandler", "recroom", "easyanticheat",
                    "system.", "microsoft.", "mono", "netstandard", "mscor", "clr", "coreclr",
                    "hostfxr", "hostpolicy", "ucrtbase", "vcruntime", "msvcp", "msvcr",
                    "ntdll", "kernel32", "kernelbase", "user32", "gdi32", "win32u", "advapi32",
                    "sechost", "rpcrt4", "combase", "ole32", "oleaut32", "shell32", "shlwapi",
                    "psapi", "winhttp", "wininet", "crypt32", "bcrypt", "ncrypt", "schannel",
                    "iphlpapi", "ws2_32", "mswsock", "dnsapi", "setupapi", "cfgmgr32", "devobj",
                    "powrprof", "secur32", "sspicli", "userenv", "profapi", "winmm", "dsound",
                    "xaudio", "x3daudio", "xinput", "dinput", "openal", "physx", "steam",
                    "nvapi", "nvcuda", "nvoglv", "cudart", "amdxc", "atiadl", "igd", "dxgi",
                    "d3d", "d3dcompiler", "dcomp", "dwmapi", "uxtheme", "propsys", "version",
                    "dbghelp", "dbgcore", "symsrv", "textinputframework", "coreuicomponents",
                    "coremessaging", "windows.storage", "wintypes", "apphelp", "avrt", "wtsapi32",
                },

                TrustedPathPrefixes = new[]
                {
                    "\\windows\\system32\\",
                    "\\windows\\winsxs\\",
                    "\\program files\\",
                    "\\program files (x86)\\",
                    "\\steam\\steamapps\\",
                    "\\dotnet\\",
                },

                ModuleDenyList = new[]
                {
                    "minhook", "easyhook", "blackbone", "detours", "polyhook",
                    "scyllahide", "scyllahook",
                    "frida-agent", "frida-gadget",
                    "cheatengine", "ceserver", "vehdebug", "speedhack",
                    "unityexplorer", "monoinjector", "melonsampler",
                    "vmthook", "internalhack", "externalhack",
                },

                // Exact image names only. Anything that is also a normal program name has
                // been left off entirely rather than matched loosely.
                ProcessDenyList = new[]
                {
                    "cheatengine-x86_64.exe", "cheatengine-i386.exe",
                    "cheatengine-x86_64-SSE4-AVX2.exe", "ceserver.exe",
                    "x64dbg.exe", "x32dbg.exe", "ollydbg.exe", "immunitydebugger.exe",
                    "windbg.exe", "windbgx.exe",
                    "ida.exe", "ida64.exe", "idaq.exe", "idaq64.exe",
                    "ghidraRun.exe",
                    "dnSpy.exe", "dnSpy-x86.exe", "dnSpyEx.exe", "ILSpy.exe", "dotPeek64.exe",
                    "scylla.exe", "scylla_x64.exe", "ScyllaHide.exe",
                    "frida.exe", "frida-server.exe", "frida-trace.exe",
                    "artmoney.exe", "squalr.exe",
                    "HTTPDebuggerUI.exe", "HTTPDebuggerSvc.exe",
                    "extremeinjector.exe", "processhacker.exe", "SystemInformer.exe",
                },

                // A class alone never counts; the detector requires a title match to treat
                // one of these as more than informational.
                WindowClassDenyList = new[]
                {
                    "TFormCheatEngine", "TMemoryBrowser", "Qt5152QWindowIcon", "OLLYDBG",
                    "ID  Window", "WinDbgFrameClass",
                },

                WindowTitleDenyList = new[]
                {
                    "Cheat Engine", "Memory Viewer", "Add Address Manually",
                    "x64dbg", "x32dbg", "OllyDbg", "Immunity Debugger",
                    "IDA -", "Ghidra:", "dnSpy", "ILSpy",
                    "HTTP Debugger", "Extreme Injector",
                },

                WatchedExports = new[]
                {
                    new WatchedExport { Module = "ntdll.dll", Export = "NtProtectVirtualMemory" },
                    new WatchedExport { Module = "ntdll.dll", Export = "NtWriteVirtualMemory" },
                    new WatchedExport { Module = "ntdll.dll", Export = "NtReadVirtualMemory" },
                    new WatchedExport { Module = "ntdll.dll", Export = "NtCreateThreadEx" },
                    new WatchedExport { Module = "ntdll.dll", Export = "NtOpenProcess" },
                    new WatchedExport { Module = "ntdll.dll", Export = "LdrLoadDll" },
                },

                // Replace these with the real signatures for your build. They are examples,
                // and a target that does not resolve is skipped rather than treated as a fault.
                Il2CppTargets = new[]
                {
                    new Il2CppTarget { TypeName = "UnityEngine.Camera", MethodName = "set_fieldOfView", ParameterCount = 1 },
                },

                AllowedPatchOwners = new[]
                {
                    "vanadiumguard", "vanadium.patch", "melonloader",
                },

                ChallengeableModules = new[]
                {
                    "GameAssembly.dll", "UnityPlayer.dll", "VanadiumGuard.dll",
                },
            };
        }
    }
}
