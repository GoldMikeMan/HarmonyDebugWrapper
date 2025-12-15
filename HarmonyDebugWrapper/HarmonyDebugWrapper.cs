using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
using System.Text.RegularExpressions;
class HarmonyDebugWrapper
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("🔍 Checking terminal environment...");
        PowerShell();
        if (args.Contains("--scanFolderStructure", StringComparer.Ordinal))
        {
            RepoMap.MapRepoFolderStructure();
            return 0;
        }
        if (args.Contains("--updateMajor", StringComparer.Ordinal))
        {
            try
            {
                if (args.Contains("--forceUpdate", StringComparer.Ordinal)) Update.UpdateWrapper(major: true, minor: false, forceUpdate: true);
                else Update.UpdateWrapper(major: true, minor: false);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Update failed: {ex.Message}");
                return 1;
            }
        }
        if (args.Contains("--updateMinor", StringComparer.Ordinal))
        {
            try
            {
                if (args.Contains("--forceUpdate", StringComparer.Ordinal)) Update.UpdateWrapper(major: false, minor: true, forceUpdate: true);
                else Update.UpdateWrapper(major: false, minor: true);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Update failed: {ex.Message}");
                return 1;
            }
        }
        if (args.Contains("--update", StringComparer.Ordinal))
        {
            try
            {
                if (args.Contains("--forceUpdate", StringComparer.Ordinal)) Update.UpdateWrapper(forceUpdate: true);
                else Update.UpdateWrapper();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Update failed: {ex.Message}");
                return 1;
            }
        }
        RepoMap map;
        try
        {
            map = RepoMap.LoadRepoMap();
            Console.WriteLine($"🔃 Loaded cached repo map from: {RepoMap.GetCachedRepoMapPath()}");
            Console.WriteLine($"🕓 Repo map last updated: {map.RepoMapCreatedAt}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ {ex.Message}");
            return 1;
        }
        Console.WriteLine($"📁 Repo root: {map.RootPath}");
        Console.WriteLine($"📁 Logger: {map.LoggerPath}");
        try
        {
            var target = FindSolutionOrProject(Directory.GetCurrentDirectory());
            Console.WriteLine($"🎯 Target solution: {target}");
            var loggerSrc = map.LoggerPath;
            if (!File.Exists(loggerSrc))
            {
                Console.WriteLine($"❌ HarmonyDebugLogger not found at path: {loggerSrc}");
                return 1;
            }
            var loggerCode = File.ReadAllText(loggerSrc);
            var syntaxTree = CSharpSyntaxTree.ParseText(loggerCode);
            var loggerProj = Path.Combine(Path.GetDirectoryName(loggerSrc)!, "HarmonyDebugLogger.csproj");
            if (!File.Exists(loggerProj)) throw new FileNotFoundException("❌ Harmony Debug Logger project file not found.", loggerProj);
            Console.WriteLine("🔧 Building Harmony Debug Logger project...");
            int buildResult = Run("dotnet", $"build \"{loggerProj}\" -c Release");
            if (buildResult != 0)
            {
                Console.WriteLine("❌ Harmony Debug Logger build failed.");
                return 1;
            }
            var loggerDLL = Path.Combine(Path.GetDirectoryName(loggerProj)!, "bin", "Release", "net10.0", "HarmonyDebugLogger.dll");
            if (!File.Exists(loggerDLL)) throw new FileNotFoundException("⚠️ Harmony Debug Logger build succeeded but DLL missing", loggerDLL);
            Console.WriteLine($"✅ Compiled Harmony Debug Logger: {loggerDLL}");
            var cmd = $"build \"{target}\" /p:ReferencePath=\"{loggerDLL}\"";
            Console.WriteLine($"> dotnet {cmd}");
            Run("dotnet", cmd);
            try { if (File.Exists(loggerDLL)) File.Delete(loggerDLL); } catch { }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Error: {ex.Message}");
            return 1;
        }
    }
    public static int Run(string exe, string args, string? workingDir = null)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = new Process { StartInfo = psi };
            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                Console.WriteLine($"❌ Command failed to execute: {exe} {args}");
                Console.WriteLine("--------------------");
                if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine($"STDOUT:\n{output}");
                if (!string.IsNullOrWhiteSpace(error)) Console.WriteLine($"STDERR:\n{error}");
                Console.WriteLine("--------------------");
            }
            else
            {
                Console.WriteLine($"✅ Command executed: {exe} {args}");
            }
            Console.WriteLine($"Exit Code: {p.ExitCode}");
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ failed to execute '{exe} {args}': {ex.Message}");
            return -1;
        }
    }
    static string FindSolutionOrProject(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            var proj = dir.GetFiles("*.csproj").FirstOrDefault();
            var sln = dir.GetFiles("*.sln").FirstOrDefault();
            var slnx = dir.GetFiles("*.slnx").FirstOrDefault();
            if (proj != null) return proj.FullName;
            if (sln != null) return sln.FullName;
            if (slnx != null) return slnx.FullName;
            dir = dir.Parent;
        }
        throw new Exception("❌ No solution or project file found.");
    }
    static void PowerShell()
    {
        try
        {
            var psi = new ProcessStartInfo("pwsh", "-Command \"$PSVersionTable.PSVersion.ToString()\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"🖥️ Terminal: PowerShell 7 — Version {output}");
                var pwshVersion = output.Replace("preview.", ".", StringComparison.OrdinalIgnoreCase).Replace("preview", ".", StringComparison.OrdinalIgnoreCase).Replace("rc.", ".", StringComparison.OrdinalIgnoreCase).Replace("rc", ".", StringComparison.OrdinalIgnoreCase).Trim();
                var parts = Regex.Matches(pwshVersion, @"\d+").Select(m => m.Value).ToList();
                while (parts.Count < 4) parts.Add("0");
                string.Join('.', parts.Take(4));
                Console.WriteLine($"⚙️ Normalized version: {pwshVersion}");
                psi = new ProcessStartInfo("winget", "search Microsoft.PowerShell.Preview")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var wingetProc = Process.Start(psi);
                string wingetOutput = wingetProc.StandardOutput.ReadToEnd();
                wingetProc.WaitForExit();
                var match = Regex.Match(wingetOutput, @"Microsoft\.PowerShell\.Preview\s+([\d\.]+)\s");
                if (match.Success)
                {
                    var wingetVersion = match.Groups[1].Value.Trim();
                    var wingetParts = Regex.Matches(wingetVersion, @"\d+").Select(m => m.Value).ToList();
                    while (wingetParts.Count < 4) wingetParts.Add("0");
                    wingetVersion = string.Join('.', wingetParts.Take(4));
                    Console.WriteLine($"📦 Winget PowerShell Preview version: {wingetVersion}");
                    if (CompareVersions(wingetVersion, pwshVersion) > 0)
                    {
                        Console.WriteLine($"🔄 Updating PowerShell 7 Preview from {pwshVersion} → {wingetVersion}...");
                        var updateProc = Process.Start(new ProcessStartInfo("winget", "upgrade --id Microsoft.PowerShell.Preview -e --source winget --accept-source-agreements --accept-package-agreements -h")
                        {
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = false
                        }) ?? throw new Exception("❌ Failed to start Winget update process.");
                        updateProc.WaitForExit();
                        if (updateProc.ExitCode == 0) Console.WriteLine($"✅ Updated PowerShell 7 Preview to {wingetVersion} successfully.");
                        else Console.WriteLine($"❌ PowerShell 7 Preview update failed with code {updateProc.ExitCode}.");
                    }
                    else Console.WriteLine("✅ PowerShell 7 Preview is already up to date.");
                    return;
                }
                else
                {
                    Console.WriteLine("⚠️ Could not determine latest PowerShell version from Winget search.");
                    return;
                }

            }
        }
        catch { /* ignored */ }
        try
        {
            var psi = new ProcessStartInfo("powershell", "-Command \"$PSVersionTable.PSVersion.ToString()\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"🖥️ Terminal: Windows PowerShell 5 — Version {output}");
                SearchAndInstallPowerShell7();
                return;
            }
        }
        catch { /* ignored */ }
        if (Environment.GetEnvironmentVariable("ComSpec")?.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase) == true)
        {
            Console.WriteLine("🖥️ Terminal: Command Prompt (cmd.exe)");
            SearchAndInstallPowerShell7();
            return;
        }
        Console.WriteLine("❌ No supported terminal detected (not PowerShell or CMD).");
    }
    static void SearchAndInstallPowerShell7()
    {
        Console.WriteLine("🔍 Searching for PowerShell 7 installation...");
        string[] defaultPaths =
        [
            @"C:\Program Files\PowerShell\7-preview\pwsh.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7-preview", "pwsh.exe")
        ];
        string? pwshPath = defaultPaths.FirstOrDefault(File.Exists);
        if (pwshPath != null)
        {
            Console.WriteLine($"✅ Found PowerShell 7 installation at: {pwshPath}");
            RelaunchInPowerShell7(pwshPath);
            return;
        }
        Console.WriteLine("⚠️ PowerShell 7 not found. Installing preview build via Winget...");
        var installProc = Process.Start(new ProcessStartInfo("winget", "install --id Microsoft.PowerShell.Preview -e --source winget --accept-source-agreements --accept-package-agreements -h")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        }) ?? throw new Exception("❌ Failed to start Winget install process.");
        installProc.WaitForExit();
        if (installProc.ExitCode == 0)
        {
            Console.WriteLine("✅ Installed PowerShell 7 Preview successfully.");
            pwshPath = defaultPaths.FirstOrDefault(File.Exists);
            if (pwshPath != null)
            {
                RelaunchInPowerShell7(pwshPath);
                return;
            }
            Console.WriteLine("⚠️ PowerShell 7 installation complete, but executable not found in default paths.");
        }
        else Console.WriteLine($"❌ PowerShell 7 Preview install failed with code {installProc.ExitCode}.");
    }
    static void RelaunchInPowerShell7(string pwshPath)
    {
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var argsJoined = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
        Console.WriteLine($"🚀 Relaunching in PowerShell 7: {pwshPath}");
        var psi = new ProcessStartInfo(pwshPath, $"-ExecutionPolicy Bypass -NoLogo -Command \"& '{exePath}' {argsJoined}\"")
        {
            UseShellExecute = true,
            CreateNoWindow = false,
            WorkingDirectory = Environment.CurrentDirectory
        };
        Process.Start(psi);
        Console.WriteLine("🚪 Exiting current instance to allow PowerShell 7 to take over...");
        Environment.Exit(0);
    }
    static int CompareVersions(string v1, string v2)
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
}
class RepoMap
{
    public string RootPath { get; set; } = "";
    public string LoggerPath { get; set; } = "";
    public List<string> ProjectFiles { get; set; } = [];
    public List<string> SolutionFiles { get; set; } = [];
    public DateTime RepoMapCreatedAt { get; set; } = DateTime.Now;
    private static readonly System.Text.Json.JsonSerializerOptions CachedJsonOptions = new()
    {
        WriteIndented = true
    };
    public static void MapRepoFolderStructure()
    {
        var startDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos");
        Console.WriteLine($"🔍 Scanning folder structure starting at {startDir}...");
        var map = new RepoMap { RootPath = startDir };
        foreach (var file in Directory.EnumerateFiles(startDir, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith("HarmonyDebugLogger.cs", StringComparison.Ordinal) && string.IsNullOrEmpty(map.LoggerPath)) map.LoggerPath = file;
            else if (file.EndsWith(".csproj", StringComparison.Ordinal)) map.ProjectFiles.Add(file);
            else if (file.EndsWith(".sln", StringComparison.Ordinal)) map.SolutionFiles.Add(file);
            else if (file.EndsWith(".slnx", StringComparison.Ordinal)) map.SolutionFiles.Add(file);
        }
        var json = System.Text.Json.JsonSerializer.Serialize(map, CachedJsonOptions);
        var cachedRepoMap = GetCachedRepoMapPath();
        File.WriteAllText(cachedRepoMap, json);
        Console.WriteLine($"📁 Repo map cached to: {cachedRepoMap}");
    }
    public static RepoMap LoadRepoMap()
    {
        var cachedRepoMap = GetCachedRepoMapPath();
        if (!File.Exists(cachedRepoMap)) throw new FileNotFoundException($"Repo cache not found. Run with --scanFolderStructure first.");
        var json = File.ReadAllText(cachedRepoMap);
        return System.Text.Json.JsonSerializer.Deserialize<RepoMap>(json) ?? throw new InvalidDataException("Cache file corrupted.");
    }
    public static string GetCachedRepoMapPath()
    {
        var repoMapDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WrapHDL");
        Directory.CreateDirectory(repoMapDir);
        return Path.Combine(repoMapDir, "RepoMap.json");
    }
}
class Update
{
    public static void UpdateWrapper(bool major = false, bool minor = false, bool forceUpdate = false)
    {
        var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var dir = Path.GetDirectoryName(exe)!;
        var projectDir = dir;
        while (projectDir != null && !File.Exists(Path.Combine(projectDir, "HarmonyDebugWrapper.csproj"))) projectDir = Directory.GetParent(projectDir)?.FullName;
        if (projectDir == null)
        {
            var possibleRepoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos", "HarmonyDebugWrapper", "HarmonyDebugWrapper");
            if (File.Exists(Path.Combine(possibleRepoPath, "HarmonyDebugWrapper.csproj")))
            {
                projectDir = possibleRepoPath;
                Console.WriteLine($"📁 Using fallback project path: {projectDir}");
            }
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
                            catch (UnauthorizedAccessException) { /* skip restricted folders */ }
                            catch (Exception ex) { Console.WriteLine($"⚠️ Skipping folder during search: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"⚠️  Error while searching home directory: {ex.Message}"); }
                    if (matches.Count > 0)
                    {
                        projectDir = Path.GetDirectoryName(matches[0]);
                        Console.WriteLine($"📁 Found project at: {projectDir}");
                    }
                    else throw new Exception("❌ Could not locate HarmonyDebugWrapper.csproj anywhere in your home directory.");
                }
                catch { throw new Exception("Could not locate HarmonyDebugWrapper.csproj."); }
            }
        }
        if (projectDir is null) throw new InvalidOperationException("⚠️ Project directory not found.");
        var csprojPath = Path.Combine(projectDir, "HarmonyDebugWrapper.csproj");
        var nupkgPath = Path.Combine(projectDir, "bin", "Release", "nupkg");
        var hashFile = Path.Combine(projectDir, ".lastbuildhash");
        var installedNupkg = Directory.EnumerateFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools"), "WrapHDL*.nupkg", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new Exception("❌ No installed WrapHDL package found.");
        Console.WriteLine("🔄 Hashing currently installed package...");
        var currentHash = ComputeFileHash(installedNupkg);
        Console.WriteLine($"🔒 Currently installed package hash: {currentHash}");
        string? oldVersion = null;
        string? newVersion = null;
        var csprojText = File.ReadAllText(csprojPath);
        try
        {
            if (forceUpdate) goto SkipHash;
            Console.WriteLine("🏗️ Building and packing new version...");
            HarmonyDebugWrapper.Run("dotnet", "build -c Release", projectDir);
            HarmonyDebugWrapper.Run("dotnet", "pack -c Release", projectDir);
            Console.WriteLine("📦 Build and pack complete.");
            var nupkgDir = Path.Combine(projectDir, "bin", "Release", "nupkg");
            if (!Directory.Exists(nupkgDir)) throw new Exception($"❌ .nupkg directory not found: {nupkgDir}");
            var latestNupkg = Directory.GetFiles(nupkgDir, "*.nupkg", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new Exception("❌ No .nupkg file found after packing.");
            Console.WriteLine($"📁 Latest nupkg package found: {Path.GetFileName(latestNupkg)} (modified {File.GetLastWriteTime(latestNupkg):HH:mm:ss})");
            Console.WriteLine("🔄 Hashing new package...");
            var newHash = ComputeFileHash(latestNupkg);
            Console.WriteLine($"🔒 Newly built package hash: {newHash}");
            Console.WriteLine("⚖️ Comparing current hash to new build hash...");
            if (string.Equals(currentHash, newHash, StringComparison.Ordinal))
            {
                Console.WriteLine("🔁 WrapHDL is up to date. Packages are identical.");
                Cleanup(projectDir, newVersion, oldVersion, csprojPath);
                return;
            }
            Console.WriteLine("🆕 Changes detected — proceeding with update...");
            File.Delete(latestNupkg);
        SkipHash:
            var match = RegexHelpers.VersionRegex().Match(csprojText);
            if (!match.Success) throw new Exception("⚠️ No <Version> tag found in .csproj.");
            oldVersion = match.Groups[1].Value.Trim();
            var parts = oldVersion.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var majorNum) || !int.TryParse(parts[1], out var minorNum) || !int.TryParse(parts[2], out var patchNum)) throw new Exception($"⚠️ Invalid version format: {oldVersion}");
            if (major)
            {
                majorNum++;
                minorNum = patchNum = 0;
            }
            else if (minor)
            {
                minorNum++;
                patchNum = 0;
            }
            else patchNum++;
            newVersion = $"{majorNum}.{minorNum}.{patchNum}";
            csprojText = csprojText.Replace($"<Version>{oldVersion}</Version>", $"<Version>{newVersion}</Version>");
            File.WriteAllText(csprojPath, csprojText);
            Console.WriteLine($"⚙️ Incremented version: {oldVersion} → {newVersion}");
            Console.WriteLine("🏗️ Building and packing incremented version...");
            HarmonyDebugWrapper.Run("dotnet", "build -c Release", projectDir);
            HarmonyDebugWrapper.Run("dotnet", "pack -c Release", projectDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Update failed: {ex.Message}");
            Cleanup(projectDir, newVersion, oldVersion, csprojPath);
            return;
        }
        var nupkg = Directory.GetFiles(nupkgPath, "*.nupkg").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new Exception("No package found after packing.");
        var pkgDir = Path.GetDirectoryName(nupkg)!;
        int currentPid = Environment.ProcessId;
        var psExe = @"C:\Program Files\PowerShell\7-preview\pwsh.exe";
        var psScript = $"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $pidToWait = {currentPid}; Write-Host \"⌛ Waiting for WrapHDL process PID=$pidToWait to exit...\"; while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 200; }} Write-Host '✅ WrapHDL process exited. Proceeding with update...'; Write-Host '🔄 Updating WrapHDL...'; $cmd = \"dotnet tool update --global --add-source '{pkgDir}' WrapHDL\"; Write-Host \"🧠 Executing: $cmd\"; Invoke-Expression $cmd; if ($LASTEXITCODE -eq 0) {{ Write-Host '✅ WrapHDL successfully updated to latest build at {DateTime.Now:HH:mm:ss}'; Write-Host '📂 Running WrapHDL --scanFolderStructure...'; WrapHDL --scanFolderStructure; }} else {{ Write-Host \"❌ WrapHDL update failed with exit code $LASTEXITCODE\"; $proj = '{csprojPath}'; $old = '{oldVersion}'; $new = '{newVersion}'; $text = Get-Content $proj -Raw; $text = $text -replace \"<Version>$new</Version>\", \"<Version>$old</Version>\"; Set-Content $proj $text -Encoding UTF8; Write-Host \"↩️ Rolled back version to $old\"; }} Remove-Item -Path $MyInvocation.MyCommand.Definition -Force;";
        var tempScript = Path.Combine(Path.GetTempPath(), "WrapHDL_Update.ps1");
        File.WriteAllText(tempScript, psScript, System.Text.Encoding.UTF8);
        Console.WriteLine("🚀 Launching PowerShell 7...");
        Console.WriteLine($"🧠 Executing: {psExe} -ExecutionPolicy Bypass -NoLogo -File \"{tempScript}\"");
        var psModulePath = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
        var psHome = Environment.GetEnvironmentVariable("PSHOME") ?? string.Empty;
        var procName = Process.GetCurrentProcess().ProcessName;
        var isPwsh = psModulePath.Contains("PowerShell\\7", StringComparison.OrdinalIgnoreCase) && psHome.Contains("PowerShell\\7", StringComparison.OrdinalIgnoreCase);
        var isDevShell = procName.Contains("VsDevCmd", StringComparison.OrdinalIgnoreCase) || procName.Contains("VsDevShell", StringComparison.OrdinalIgnoreCase) || procName.Contains("devenv", StringComparison.OrdinalIgnoreCase);
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var argsJoined = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
        if (!isPwsh || isDevShell)
        {
            Console.WriteLine("⚙️ Not running in standalone PowerShell 7 — relaunching self in pwsh...");
            var relaunchCmd = $"{psExe} -ExecutionPolicy Bypass -NoLogo -Command \"& '{exePath}' {argsJoined}\"";
            var relaunch = new ProcessStartInfo("cmd.exe", $"/C \"{relaunchCmd}\"")
            {
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = Environment.CurrentDirectory
            };
            Process.Start(relaunch);
            Console.WriteLine("🚪 Exiting current instance so pwsh can take over...");
            Environment.Exit(0);
        }
        else
        {
            var psi = new ProcessStartInfo(psExe, $"-ExecutionPolicy Bypass -NoLogo -File \"{tempScript}\"")
            {
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = Environment.CurrentDirectory
            };
            Process.Start(psi);
            Console.WriteLine("🚪 Exiting current instance to allow update...");
            Environment.Exit(0);
        }
    }
    private static string ComputeFileHash(string filePath)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
    private static void Cleanup(string projectDir, string? newVersion = null, string? oldVersion = null, string? csprojPath = null)
    {
        Console.WriteLine("🧹 Performing cleanup...");
        try
        {
            if (!string.IsNullOrEmpty(oldVersion) && !string.IsNullOrEmpty(newVersion) && !string.IsNullOrEmpty(csprojPath))
            {
                var rollbackText = File.ReadAllText(csprojPath);
                rollbackText = rollbackText.Replace($"<Version>{newVersion}</Version>", $"<Version>{oldVersion}</Version>");
                File.WriteAllText(csprojPath, rollbackText);
                Console.WriteLine($"🔁 Restored version number: {newVersion} → {oldVersion}");
            }
            var nupkgDir = Path.Combine(projectDir, "bin", "Release");
            if (Directory.Exists(nupkgDir))
            {
                var latestNupkg = Directory.GetFiles(nupkgDir, "*.nupkg", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (latestNupkg != null)
                {
                    File.Delete(latestNupkg);
                    Console.WriteLine($"🗑️ Deleted generated package: {Path.GetFileName(latestNupkg)}");
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"⚠️ Cleanup encountered an issue: {ex.Message}"); }
    }
}
#pragma warning disable CA1050
public static partial class RegexHelpers
{
    [GeneratedRegex("<Version>(.*?)</Version>", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    public static partial Regex VersionRegex();
    [GeneratedRegex("(\\d+\\.\\d+\\.\\d+(?:\\.\\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    public static partial Regex PwshVersionRegex();
}
#pragma warning restore CA1050