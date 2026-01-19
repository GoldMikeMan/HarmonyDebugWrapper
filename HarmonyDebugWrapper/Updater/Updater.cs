using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
[assembly: SupportedOSPlatform("windows")]
namespace HarmonyDebugWrapper.Updater
{
    public static partial class Update
    {
        public static bool TryHandleUpdateCommandTree(string[] args, string toolId, string csprojFileName, out int exitCode)
        {
            exitCode = 0;
            bool hasUpdateMajor = args.Contains("--updateMajor", StringComparer.Ordinal);
            bool hasUpdateMinor = args.Contains("--updateMinor", StringComparer.Ordinal);
            bool hasUpdate = args.Contains("--update", StringComparer.Ordinal);
            if ((hasUpdateMajor && hasUpdateMinor) || (hasUpdateMajor && hasUpdate) || (hasUpdateMinor && hasUpdate)) { Console.WriteLine("Only one primary argument allowed."); exitCode = 1; return true; }
            bool isUpdatePrimary = hasUpdateMajor || hasUpdateMinor || hasUpdate;
            if (!isUpdatePrimary) return false;
            bool forceUpdate = args.Contains("--forceUpdate", StringComparer.Ordinal);
            bool skipVersion = args.Contains("--skipVersion", StringComparer.Ordinal);
            if (skipVersion && !forceUpdate) { Console.WriteLine("❌ --skipVersion requires --forceUpdate as a secondary arg."); exitCode = 1; return true; }
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "--updateMajor", "--updateMinor", "--update", "--forceUpdate", "--skipVersion" };
            foreach (var a in args) { if (a.StartsWith("--", StringComparison.Ordinal) && !allowed.Contains(a)) { Console.WriteLine($"❌ Unknown arg for update command: {a}"); exitCode = 1; return true; } }
            try { UpdateTool(toolId, csprojFileName, hasUpdateMajor, hasUpdateMinor, forceUpdate, skipVersion); exitCode = 0; return true; }
            catch (Exception ex) { Console.WriteLine($"❌ Update failed: {ex.Message}"); exitCode = 1; return true; }
        }
        public static void UpdateTool(string toolId, string csprojFileName, bool major, bool minor, bool forceUpdate, bool skipVersion)
        {
            var projectDir = FindProjectDir(csprojFileName);
            var csprojPath = Path.Combine(projectDir, csprojFileName);
            var nupkgPath = Path.Combine(projectDir, "bin", "Release", "nupkg");
            var installedNupkg = FindInstalledNupkg(toolId) ?? throw new Exception($"❌ No installed {toolId} package found.");
            if (!forceUpdate)
            {
                Console.WriteLine("🔄 Hashing currently installed package...");
                var currentHash = ComputeFileHash(installedNupkg);
                Console.WriteLine($"🔒 Currently installed package hash: {currentHash}");
                Console.WriteLine("🏗️ Building and packing current version...");
                Cmd.Run("dotnet", "build -c Release", projectDir, false, false, false, true);
                Cmd.Run("dotnet", "pack -c Release", projectDir, false, false, false, true);
                var latestForCompare = FindLatestNupkg(nupkgPath);
                Console.WriteLine($"📁 Latest nupkg package found: {Path.GetFileName(latestForCompare)} (modified {File.GetLastWriteTime(latestForCompare):dd-MM-yyyy HH:mm:ss})");
                Console.WriteLine("🔄 Hashing new package...");
                var newHash = ComputeFileHash(latestForCompare);
                Console.WriteLine($"🔒 Newly built package hash: {newHash}");
                Console.WriteLine("⚖️ Comparing current hash to new build hash...");
                if (string.Equals(currentHash, newHash, StringComparison.Ordinal)) { Console.WriteLine($"🔁 {toolId} is up to date. Packages are identical."); return; }
                Console.WriteLine("🆕 Changes detected — proceeding with update...");
            }
            string? oldVersion = null;
            string? newVersion = null;
            try
            {
                if (!skipVersion)
                {
                    var csprojText = File.ReadAllText(csprojPath);
                    var match = VersionRegex().Match(csprojText);
                    if (!match.Success) throw new Exception("⚠️ No <Version> tag found in .csproj.");
                    oldVersion = match.Groups[1].Value.Trim();
                    var parts = oldVersion.Split('.');
                    if (parts.Length != 3 || !int.TryParse(parts[0], out var majorNum) || !int.TryParse(parts[1], out var minorNum) || !int.TryParse(parts[2], out var patchNum)) throw new Exception($"⚠️ Invalid version format: {oldVersion}");
                    if (major) { majorNum++; minorNum = 0; patchNum = 0; }
                    else if (minor) { minorNum++; patchNum = 0; }
                    else patchNum++;
                    newVersion = $"{majorNum}.{minorNum}.{patchNum}";
                    csprojText = csprojText.Replace($"<Version>{oldVersion}</Version>", $"<Version>{newVersion}</Version>");
                    File.WriteAllText(csprojPath, csprojText);
                    Console.WriteLine($"⏫ Incremented version: {oldVersion} → {newVersion}");
                }
                else Console.WriteLine("⏭️ Skipping version increment");
                Console.WriteLine("🏗️ Building and packing...");
                Cmd.Run("dotnet", "build -c Release", projectDir, false, false, false, true);
                Cmd.Run("dotnet", "pack -c Release", projectDir, false, false, false, true);
            }
            catch (Exception ex) { Console.WriteLine($"❌ Update failed: {ex.Message}"); Cleanup(newVersion, oldVersion, csprojPath); return; }
            var nupkg = FindLatestNupkg(nupkgPath);
            var pkgDir = Path.GetDirectoryName(nupkg)!;
            int currentPid = Environment.ProcessId;
            var psExe = FindPowerShellExe();
            var updateScriptPath = Path.Combine(AppContext.BaseDirectory, "Updater", "UpdateScript.ps1");
            var psArgs = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{updateScriptPath}\" -toolId \"{toolId}\" {(skipVersion ? "-skipVersion " : "")}-pidToWait {currentPid} -pkgDir \"{pkgDir}\" -csprojPath \"{csprojPath}\" -oldVersion \"{oldVersion ?? ""}\" -newVersion \"{newVersion ?? ""}\"";
            var psi = new ProcessStartInfo(psExe, psArgs) { UseShellExecute = false, CreateNoWindow = false, RedirectStandardOutput = false, RedirectStandardError = false, WorkingDirectory = Environment.CurrentDirectory };
            Console.WriteLine("🧠 Executing: UpdateScript.ps1");
            _ = Process.Start(psi) ?? throw new Exception("❌ Failed to start UpdateScript PowerShell process.");
            Console.Out.Flush();
            Environment.Exit(0);
        }
        private static string FindProjectDir(string csprojFileName)
        {
            var dir = new DirectoryInfo(Environment.CurrentDirectory);
            while (dir != null) { if (File.Exists(Path.Combine(dir.FullName, csprojFileName))) return dir.FullName; dir = dir.Parent; }
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var repos = Path.Combine(home, "source", "repos");
            var found = TryFindFile(repos, csprojFileName) ?? TryFindFile(home, csprojFileName);
            if (found != null) { var projDir = Path.GetDirectoryName(found)!; Console.WriteLine($"📁 Found project at: {projDir}"); return projDir; }
            throw new Exception($"❌ Could not locate {csprojFileName}.");
        }
        private static string? TryFindFile(string root, string fileName)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, ReturnSpecialDirectories = false };
            try { return Directory.EnumerateFiles(root, fileName, opts).FirstOrDefault(); } catch { return null; }
        }
        private static string? FindInstalledNupkg(string toolId)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var toolsRoot = Path.Combine(home, ".dotnet", "tools");
            if (!Directory.Exists(toolsRoot)) return null;
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, ReturnSpecialDirectories = false };
            try { return Directory.EnumerateFiles(toolsRoot, $"{toolId}*.nupkg", opts).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault(); } catch { return null; }
        }
        private static string FindLatestNupkg(string nupkgDir)
        {
            if (!Directory.Exists(nupkgDir)) throw new Exception($"❌ .nupkg directory not found: {nupkgDir}");
            return Directory.EnumerateFiles(nupkgDir, "*.nupkg", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new Exception("❌ No .nupkg file found after packing.");
        }
        private static string FindPowerShellExe()
        {
            var psExe = @"C:\Program Files\PowerShell\7-preview\pwsh.exe";
            if (!File.Exists(psExe)) psExe = @"C:\Program Files\PowerShell\7\pwsh.exe";
            if (!File.Exists(psExe)) psExe = "pwsh";
            return psExe;
        }
        private static string ComputeFileHash(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
        private static void Cleanup(string? newVersion, string? oldVersion, string csprojPath)
        {
            Console.WriteLine("🧹 Performing cleanup...");
            try
            {
                if (!string.IsNullOrEmpty(oldVersion) && !string.IsNullOrEmpty(newVersion))
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
        [GeneratedRegex("<Version>(.*?)</Version>", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
        private static partial Regex VersionRegex();
        internal static class Cmd
        {
            public static (int ExitCode, string Output, string Error) Run(string exe, string args, string? workingDir = null, bool silent = false, bool streamToConsole = false, bool exitOnFail = false, bool inheritConsole = false)
            {
                try
                {
                    if (!silent) Console.WriteLine($"🧠 Executing: {exe} {args}");
                    if (exe.Equals("dotnet")) args += " --tl:on";
                    var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = workingDir ?? Environment.CurrentDirectory, UseShellExecute = false, CreateNoWindow = !inheritConsole, RedirectStandardOutput = !inheritConsole, RedirectStandardError = !inheritConsole };
                    if (!inheritConsole) { psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8; }
                    using var p = new Process { StartInfo = psi };
                    if (inheritConsole)
                    {
                        p.Start();
                        p.WaitForExit();
                        if (!silent)
                        {
                            Console.WriteLine($"🚪 Exit Code {p.ExitCode}: {ExitMessage(p.ExitCode)}");
                            if (p.ExitCode != 0 && exitOnFail) Environment.Exit(p.ExitCode);
                        }
                        return (p.ExitCode, string.Empty, string.Empty);
                    }
                    var sbOut = new StringBuilder();
                    var sbErr = new StringBuilder();
                    p.OutputDataReceived += (_, e) => { if (e.Data == null) return; sbOut.AppendLine(e.Data); if (streamToConsole) Console.WriteLine(e.Data); };
                    p.ErrorDataReceived += (_, e) => { if (e.Data == null) return; sbErr.AppendLine(e.Data); if (streamToConsole) Console.Error.WriteLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    var output = sbOut.ToString().Trim();
                    var error = sbErr.ToString().Trim();
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
                        Console.WriteLine($"🚪 Exit Code {p.ExitCode}: {ExitMessage(p.ExitCode)}");
                        if (p.ExitCode != 0 && exitOnFail) { Console.Out.Flush(); Console.Error.Flush(); Environment.Exit(p.ExitCode); }
                    }
                    return (p.ExitCode, output, error);
                }
                catch (Exception ex) { Console.WriteLine($"❌ failed to execute '{exe} {args}': {ex.Message}"); return (-1, string.Empty, ex.Message); }
            }
            private static string ExitMessage(int code)
            {
                return code switch
                {
                    0 => "✅ Success — operation completed successfully.",
                    1 => "⚠️ General error — check command syntax or output for details.",
                    2 => "❌ Invalid arguments or syntax.",
                    3 => "🚫 Access denied or insufficient permissions.",
                    4 => "📦 Target file or package not found.",
                    5 => "🧱 I/O or path-related error.",
                    127 => "❓ Command not found or missing from PATH.",
                    _ => $"🌀 Tool-specific exit code ({code})."
                };
            }
        }
    }
}