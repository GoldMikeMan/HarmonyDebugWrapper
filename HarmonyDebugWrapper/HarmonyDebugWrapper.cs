using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
class HarmonyDebugWrapper
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Contains("--scanFolderStructure", StringComparer.Ordinal))
        {
            RepoMap.MapRepoFolderStructure();
            return 0;
        }
        if (args.Contains("--update", StringComparer.Ordinal))
        {
            try
            {
                Update.UpdateWrapper();
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
}
class RepoMap
{
    public string RootPath { get; set; } = "";
    public string LoggerPath { get; set; } = "";
    public List<string> ProjectFiles { get; set; } = new();
    public List<string> SolutionFiles { get; set; } = new();
    public DateTime RepoMapCreatedAt { get; set; } = DateTime.Now;
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
        var json = System.Text.Json.JsonSerializer.Serialize(map, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
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
class Update()
{
    public static void UpdateWrapper()
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
                    List<string> matches = new();
                    try
                    {
                        Console.WriteLine("🔍 Searching for HarmonyDebugWrapper.csproj under your home directory...");
                        foreach (var drive in new[] { home })
                        {
                            try
                            {
                                matches.AddRange(Directory.GetFiles(drive, "HarmonyDebugWrapper.csproj", SearchOption.AllDirectories));
                            }
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
                    else
                    {
                        throw new Exception("❌ Could not locate HarmonyDebugWrapper.csproj anywhere in your home directory.");
                    }
                }
                catch { throw new Exception("Could not locate HarmonyDebugWrapper.csproj."); }
            }
        }
        Console.WriteLine("🔄 Rebuilding and updating WrapHDL...");
        if (projectDir is null) throw new InvalidOperationException("⚠️ Project directory not found.");
        var srcFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories);
        var latestSrcTime = srcFiles.Select(File.GetLastWriteTimeUtc).OrderByDescending(t => t).FirstOrDefault();
        var nupkgPath = Path.Combine(projectDir, "bin", "release", "nupkg");
        var latestNupkg = Directory.Exists(nupkgPath) ? Directory.GetFiles(nupkgPath, "*.nupkg").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (latestNupkg != null)
        {
            var nupkgTime = File.GetLastWriteTimeUtc(latestNupkg);
            if (latestSrcTime <= nupkgTime)
            {
                Console.WriteLine($"⚙️ No source changes since last build ({nupkgTime:HH:mm:ss}). Skipping rebuild and version bump.");
                Console.WriteLine($"✅ WrapHDL is already up-to-date at version {Path.GetFileNameWithoutExtension(latestNupkg)}.");
                return;
            }
        }
        var csprojPath = Path.Combine(projectDir, "HarmonyDebugWrapper.csproj");
        var csprojText = File.ReadAllText(csprojPath);
        var match = System.Text.RegularExpressions.Regex.Match(csprojText, @"<Version>(.*?)</Version>");
        if (match.Success)
        {
            var oldVersion = match.Groups[1].Value.Trim();
            var parts = oldVersion.Split('.');
            if (parts.Length == 3 && int.TryParse(parts[2], out var patch))
            {
                var newVersion = $"{parts[0]}.{parts[1]}.{patch + 1}";
                csprojText = csprojText.Replace($"<Version>{oldVersion}</Version>", $"<Version>{newVersion}</Version>");
                File.WriteAllText(csprojPath, csprojText);
                Console.WriteLine($"⚙️ Incremented version: {oldVersion} → {newVersion}");
            }
            else Console.WriteLine($"⚠️ Could not parse version '{oldVersion}', skipping bump.");
        }
        else Console.WriteLine("⚠️ No <Version> tag found in .csproj — skipping bump.");
        HarmonyDebugWrapper.Run("dotnet", "pack -c Release", projectDir);
        var nupkg = Directory.GetFiles(Path.Combine(projectDir, "bin", "release", "nupkg"), "*.nupkg").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
        if (nupkg == null) throw new Exception("No package found after packing.");
        var pkgDir = Path.GetDirectoryName(nupkg)!;
        var psScript = $"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; Start-Sleep -Seconds 1; Write-Host '🔄 Updating WrapHDL...'; dotnet tool update --global --add-source '{pkgDir}' WrapHDL; if ($LASTEXITCODE -eq 0) {{ Write-Host '✅ WrapHDL successfully updated to latest build at {DateTime.Now:HH:mm:ss}'; WrapHDL --scanFolderStructure; }} else {{ Write-Host '❌ WrapHDL update failed with exit code $LASTEXITCODE'; }} Remove-Item -Path $MyInvocation.MyCommand.Definition -Force;";
        var tempScript = Path.Combine(Path.GetTempPath(), "WrapHDL_Update.ps1");
        File.WriteAllText(tempScript, psScript, System.Text.Encoding.UTF8);
        Console.WriteLine("🚀 Launching PowerShell in current terminal to perform update...");
        string psExe;
        try
        {
            var pwshCheck = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "pwsh",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var whereProc = Process.Start(pwshCheck);
            whereProc!.WaitForExit(1000);
            psExe = whereProc.ExitCode == 0 ? "pwsh" : "powershell";
        }
        catch { psExe = "powershell"; }
        Console.WriteLine($"🚀 Launching update via {(psExe == "pwsh" ? "PowerShell 7" : "PowerShell 5")}...");
        var psi = new ProcessStartInfo(psExe, $"-NoExit -ExecutionPolicy Bypass -File \"{tempScript}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false
        };
        Process.Start(psi);
        Console.WriteLine("🚪 Exiting current instance to allow update...");
        Environment.Exit(0);
    }
}