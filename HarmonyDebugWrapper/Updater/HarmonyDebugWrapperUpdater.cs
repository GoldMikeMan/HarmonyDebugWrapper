using HarmonyDebugWrapper.Helpers;
using System.Diagnostics;
using System.Runtime.Versioning;
[assembly: SupportedOSPlatform("windows")]
namespace HarmonyDebugWrapper.Updater
{
    class Update
    {
        public static void UpdateWrapper(bool major = false, bool minor = false, bool forceUpdate = false, bool skipVersion = false)
        {
            var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(exe)!;
            var projectDir = dir;
            while (projectDir != null && !File.Exists(Path.Combine(projectDir, "HarmonyDebugWrapper.csproj"))) projectDir = Directory.GetParent(projectDir)?.FullName;
            if (projectDir == null)
            {
                var possibleRepoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos", "HarmonyDebugWrapper", "HarmonyDebugWrapper");
                if (File.Exists(Path.Combine(possibleRepoPath, "HarmonyDebugWrapper.csproj"))) { projectDir = possibleRepoPath; Console.WriteLine($"📁 Using fallback project path: {projectDir}"); }
                else
                {
                    try
                    {
                        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        List<string> matches = [];
                        try
                        {
                            Console.WriteLine("🔍 Searching for HarmonyDebugWrapper.csproj under your home directory...");
                            foreach (var drive in new[] { home })
                            {
                                try { matches.AddRange(Directory.GetFiles(drive, "HarmonyDebugWrapper.csproj", SearchOption.AllDirectories)); }
                                catch (UnauthorizedAccessException) { }
                                catch (Exception ex) { Console.WriteLine($"⚠️ Skipping folder during search: {ex.Message}"); }
                            }
                        }
                        catch (Exception ex) { Console.WriteLine($"⚠️  Error while searching home directory: {ex.Message}"); }
                        if (matches.Count > 0) { projectDir = Path.GetDirectoryName(matches[0]); Console.WriteLine($"📁 Found project at: {projectDir}"); }
                        else throw new Exception("❌ Could not locate HarmonyDebugWrapper.csproj anywhere in your home directory.");
                    }
                    catch { throw new Exception("Could not locate HarmonyDebugWrapper.csproj."); }
                }
            }
            if (projectDir is null) throw new InvalidOperationException("⚠️ Project directory not found.");
            var csprojPath = Path.Combine(projectDir, "HarmonyDebugWrapper.csproj");
            var nupkgPath = Path.Combine(projectDir, "bin", "Release", "nupkg");
            var installedNupkg = Directory.EnumerateFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools"), "WrapHDL*.nupkg", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new Exception("❌ No installed WrapHDL package found.");
            string? currentHash = null;
            if (forceUpdate) goto SkipHashCurrent;
            Console.WriteLine("🔄 Hashing currently installed package...");
            currentHash = OtherHelpers.ComputeFileHash(installedNupkg);
            Console.WriteLine($"🔒 Currently installed package hash: {currentHash}");
        SkipHashCurrent:
            string? oldVersion = null;
            string? newVersion = null;
            var csprojText = File.ReadAllText(csprojPath);
            try
            {
                if (forceUpdate) goto SkipHashNew;
                Console.WriteLine("🏗️ Building and packing new version...");
                OtherHelpers.CommandRunner("dotnet", "build -c Release", projectDir);
                OtherHelpers.CommandRunner("dotnet", "pack -c Release", projectDir);
                Console.WriteLine("📦 Build and pack complete.");
                var nupkgDir = Path.Combine(projectDir, "bin", "Release", "nupkg");
                if (!Directory.Exists(nupkgDir)) throw new Exception($"❌ .nupkg directory not found: {nupkgDir}");
                var latestNupkg = Directory.GetFiles(nupkgDir, "*.nupkg", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new Exception("❌ No .nupkg file found after packing.");
                Console.WriteLine($"📁 Latest nupkg package found: {Path.GetFileName(latestNupkg)} (modified {File.GetLastWriteTime(latestNupkg):dd-MM-yyyy HH:mm:ss})");
                Console.WriteLine("🔄 Hashing new package...");
                var newHash = OtherHelpers.ComputeFileHash(latestNupkg);
                Console.WriteLine($"🔒 Newly built package hash: {newHash}");
                Console.WriteLine("⚖️ Comparing current hash to new build hash...");
                if (string.Equals(currentHash, newHash, StringComparison.Ordinal)) { Console.WriteLine("🔁 WrapHDL is up to date. Packages are identical."); return; }
                Console.WriteLine("🆕 Changes detected — proceeding with update...");
            SkipHashNew:
                if (skipVersion) { Console.WriteLine("⏭️ Skipping version increment"); Console.WriteLine("🏗️ Building and packing new version..."); goto skipVersion; }
                var match = RegexHelpers.VersionRegex().Match(csprojText);
                if (!match.Success) throw new Exception("⚠️ No <Version> tag found in .csproj.");
                oldVersion = match.Groups[1].Value.Trim();
                var parts = oldVersion.Split('.');
                if (parts.Length != 3 || !int.TryParse(parts[0], out var majorNum) || !int.TryParse(parts[1], out var minorNum) || !int.TryParse(parts[2], out var patchNum)) throw new Exception($"⚠️ Invalid version format: {oldVersion}");
                if (major) { majorNum++; minorNum = patchNum = 0; }
                else if (minor) { minorNum++; patchNum = 0; }
                else patchNum++;
                newVersion = $"{majorNum}.{minorNum}.{patchNum}";
                csprojText = csprojText.Replace($"<Version>{oldVersion}</Version>", $"<Version>{newVersion}</Version>");
                File.WriteAllText(csprojPath, csprojText);
                Console.WriteLine($"⏫ Incremented version: {oldVersion} → {newVersion}");
                Console.WriteLine("🏗️ Building and packing incremented version...");
            skipVersion:
                OtherHelpers.CommandRunner("dotnet", "build -c Release", projectDir);
                OtherHelpers.CommandRunner("dotnet", "pack -c Release", projectDir);
            }
            catch (Exception ex) { Console.WriteLine($"❌ Update failed: {ex.Message}"); OtherHelpers.Cleanup(newVersion, oldVersion, csprojPath); return; }
            var nupkg = Directory.GetFiles(nupkgPath, "*.nupkg").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new Exception("No package found after packing.");
            var pkgDir = Path.GetDirectoryName(nupkg)!;
            int currentPid = Environment.ProcessId;
            var psExe = @"C:\Program Files\PowerShell\7-preview\pwsh.exe";
            if (!File.Exists(psExe)) psExe = @"C:\Program Files\PowerShell\7\pwsh.exe";
            if (!File.Exists(psExe)) psExe = "pwsh";
            var updateScriptPath = Path.Combine(AppContext.BaseDirectory, "Updater", "UpdateScript.ps1");
            var psArgs = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{updateScriptPath}\" {(skipVersion ? "-skipVersion " : "")} -pidToWait {currentPid} -pkgDir \"{pkgDir}\" -csprojPath \"{csprojPath}\" -oldVersion \"{oldVersion}\" -newVersion \"{newVersion}\"";
            var psi = new ProcessStartInfo(psExe, psArgs) { UseShellExecute = false, CreateNoWindow = false, RedirectStandardOutput = false, RedirectStandardError = false, WorkingDirectory = Environment.CurrentDirectory };
            Console.WriteLine("🧠 Executing: UpdateScript.ps1");
            _ = Process.Start(psi) ?? throw new Exception("❌ Failed to start UpdateScript PowerShell process.");
            Console.Out.Flush();
            Environment.Exit(0);
        }
    }
}