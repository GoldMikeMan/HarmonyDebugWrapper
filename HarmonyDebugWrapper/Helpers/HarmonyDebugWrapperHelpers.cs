using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
[assembly: SupportedOSPlatform("windows")]
namespace HarmonyDebugWrapper.Helpers
{
    public static partial class RegexHelpers
    {
        [GeneratedRegex("<Version>(.*?)</Version>", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
        public static partial Regex VersionRegex();
    }
    public class OtherHelpers
    {
        public static (int ExitCode, string Output, string Error) CommandRunner(string exe, string args, string? workingDir = null, bool silent = false, bool streamToConsole = false, bool exitOnFail = false, bool inheritConsole = false)
        {
            try
            {
                if (!silent) Console.WriteLine($"🧠 Executing: {exe} {args}");
                var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = workingDir ?? Environment.CurrentDirectory, UseShellExecute = false, CreateNoWindow = !inheritConsole, RedirectStandardOutput = !inheritConsole, RedirectStandardError = !inheritConsole };
                if (!inheritConsole) { psi.StandardOutputEncoding = System.Text.Encoding.UTF8; psi.StandardErrorEncoding = System.Text.Encoding.UTF8; }
                using var p = new Process { StartInfo = psi };
                if (inheritConsole)
                {
                    p.Start();
                    p.WaitForExit();
                    if (!silent)
                    {
                        string exitMessage = p.ExitCode switch
                        {
                            0 => "✅ Success — operation completed successfully.",
                            1 => "⚠️ General error — check command syntax or output for details.",
                            2 => "❌ Invalid arguments or syntax.",
                            3 => "🚫 Access denied or insufficient permissions.",
                            4 => "📦 Target file or package not found.",
                            5 => "🧱 I/O or path-related error.",
                            127 => "❓ Command not found or missing from PATH.",
                            _ => $"🌀 Tool-specific exit code ({p.ExitCode})."
                        };
                        Console.WriteLine($"🚪 Exit Code {p.ExitCode}: {exitMessage}");
                        if (p.ExitCode != 0 && exitOnFail) Environment.Exit(p.ExitCode);
                    }
                    return (p.ExitCode, string.Empty, string.Empty);
                }
                var sbOut = new System.Text.StringBuilder();
                var sbErr = new System.Text.StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data == null) return; sbOut.AppendLine(e.Data); if (streamToConsole) Console.WriteLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data == null) return; sbErr.AppendLine(e.Data); if (streamToConsole) Console.Error.WriteLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                p.WaitForExit();
                var output = sbOut.ToString();
                var error = sbErr.ToString();
                if (!silent)
                {
                    if (p.ExitCode != 0 && !streamToConsole)
                    {
                        Console.WriteLine($"❌ Command failed to execute: {exe} {args}");
                        Console.WriteLine("--------------------");
                        if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine($"STDOUT:\n{output}");
                        if (!string.IsNullOrWhiteSpace(error)) Console.WriteLine($"STDERR:\n{error}");
                        Console.WriteLine("--------------------");
                    }
                    string exitMessage = p.ExitCode switch
                    {
                        0 => "✅ Success — operation completed successfully.",
                        1 => "⚠️ General error — check command syntax or output for details.",
                        2 => "❌ Invalid arguments or syntax.",
                        3 => "🚫 Access denied or insufficient permissions.",
                        4 => "📦 Target file or package not found.",
                        5 => "🧱 I/O or path-related error.",
                        127 => "❓ Command not found or missing from PATH.",
                        _ => $"🌀 Tool-specific exit code ({p.ExitCode})."
                    };
                    Console.WriteLine($"🚪 Exit Code {p.ExitCode}: {exitMessage}");
                    if (p.ExitCode != 0 && exitOnFail) { Console.Out.Flush(); Console.Error.Flush(); Environment.Exit(p.ExitCode); }
                }
                return (p.ExitCode, output.Trim(), error.Trim());
            }
            catch (Exception ex) { Console.WriteLine($"❌ failed to execute '{exe} {args}': {ex.Message}"); return (-1, string.Empty, ex.Message); }
        }
        public static int CompareVersions(string v1, string v2)
        {
            var p1 = v1.Split('.').Select(int.Parse).ToArray();
            var p2 = v2.Split('.').Select(int.Parse).ToArray();
            for (int i = 0; i < Math.Max(p1.Length, p2.Length); i++)
            {
                int a = i < p1.Length ? p1[i] : 0;
                int b = i < p2.Length ? p2[i] : 0;
                if (a > b) return 1;
                if (a < b) return -1;
            }
            return 0;
        }
        public static string ComputeFileHash(string filePath)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        public static void Cleanup(string? newVersion = null, string? oldVersion = null, string? csprojPath = null)
        {
            Console.WriteLine("🧹 Performing cleanup...");
            try
            {
                if (!string.IsNullOrEmpty(oldVersion) && !string.IsNullOrEmpty(newVersion) && !string.IsNullOrEmpty(csprojPath))
                {
                    var rollbackText = File.ReadAllText(csprojPath);
                    rollbackText = rollbackText.Replace($"<Version>{newVersion}</Version>", $"<Version>{oldVersion}</Version>");
                    File.WriteAllText(csprojPath, rollbackText);
                    Console.WriteLine($"↩️ Restored version number: {newVersion} → {oldVersion}");
                }
            }
            catch (Exception ex) { Console.WriteLine($"⚠️ Cleanup encountered an issue: {ex.Message}"); }
            Console.WriteLine("✅ Cleanup complete.");
        }
        public static bool VerifyToolBoxHost()
        {
            const string sentinel = "🔍 Verifying parent is ToolBox";
            Console.WriteLine(sentinel);
            using var spin = new Spinner("|", "/", "-", "\\");
            if (!Console.IsOutputRedirected) spin.Start("⏳ Waiting for ToolBox");
            long end = Environment.TickCount64 + 5000;
            var readTask = Task.Run(() => Console.ReadLine());
            while (Environment.TickCount64 < end)
            {
                int remaining = (int)Math.Max(0, end - Environment.TickCount64);
                if (readTask.Wait(remaining))
                {
                    var resp = ((readTask.Result ?? "").Trim()).TrimStart('\uFEFF');
                    if (string.Equals(resp, "ToolBox is open", StringComparison.Ordinal))
                    {
                        spin.Stop();
                        Console.WriteLine("✅ ToolBox detected.");
                        return true;
                    }
                }
                Thread.Sleep(10);
            }
            spin.Stop();
            Console.WriteLine("❌ ToolBox required to use this tool.");
            return false;
        }
        sealed class Spinner(params string[] frames) : IDisposable
        {
            readonly string[] frames = frames.Length == 0 ? ["|", "/", "-", "\\"] : frames;
            volatile bool running;
            Thread? t;
            string text = "";
            bool oldCursorVisible = true;
            public void Start(string text)
            {
                if (Console.IsOutputRedirected) return;
                if (running) return;
                this.text = text;
                running = true;
                try { oldCursorVisible = Console.CursorVisible; Console.CursorVisible = false; } catch { }
                t = new Thread(() => {
                    int i = 0;
                    while (running)
                    {
                        try { Console.Write("\r" + this.text + " " + frames[i++ % frames.Length]); } catch { }
                        Thread.Sleep(100);
                    }
                }) { IsBackground = true };
                t.Start();
            }
            public void Stop()
            {
                if (!running) return;
                running = false;
                try { t?.Join(200); } catch { }
                try { int w = Math.Max(1, Console.BufferWidth); Console.Write("\r" + new string(' ', w - 1) + "\r"); } catch { try { Console.Write("\r"); } catch { } }
                try { Console.CursorVisible = oldCursorVisible; } catch { }
            }
            public void Dispose() { Stop(); }
        }
        public static void PrintHelp()
        {
            Console.WriteLine("WrapHDL");
            Console.WriteLine("Usage: WrapHDL [primary] [secondary] [tertiary]");
            Console.WriteLine("");
            Console.WriteLine("Primary:");
            Console.WriteLine("  --help                 Show this help and exit.");
            Console.WriteLine("  --scanFolderStructure  Scan repos and cache repo map.");
            Console.WriteLine("  --updateMajor          Increment major version (resets minor+patch), build+pack, and install.");
            Console.WriteLine("  --updateMinor          Increment minor version (resets patch), build+pack, and install.");
            Console.WriteLine("  --update               Increment patch version, build+pack, and install.");
            Console.WriteLine("");
            Console.WriteLine("Secondary:");
            Console.WriteLine("  --forceUpdate          Force rebuild/reinstall even if nothing changed. Requires an update primary.");
            Console.WriteLine("");
            Console.WriteLine("Tertiary:");
            Console.WriteLine("  --skipVersion          Do not change the <Version> in the .csproj. Requires --forceUpdate.");
            Console.WriteLine("");
        }
    }
}