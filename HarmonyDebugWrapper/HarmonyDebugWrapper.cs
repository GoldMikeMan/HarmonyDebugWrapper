using HarmonyDebugWrapper.Updater;
using HarmonyDebugWrapper.ToolBox;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.Versioning;
using System.Text;
[assembly: SupportedOSPlatform("windows")]
namespace HarmonyDebugWrapper
{
    class WrapHDL
    {
        static int Main(string[] args)
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            bool hasHelp = args.Contains("--help", StringComparer.Ordinal);
            bool hasScan = args.Contains("--scanFolderStructure", StringComparer.Ordinal);
            if (hasHelp)
            {
                Console.WriteLine("Commands:");
                Console.WriteLine("  'WrapHDL'                              Tries to wrap HarmonyDebugLogger around C# project in current directory");
                Console.WriteLine("Primary args:");
                Console.WriteLine("  '--help'                               Print help to console.");
                Console.WriteLine("  '--scanFolderStructure'                Scan repos and cache repo map.");
                Console.WriteLine("  '--update [secondary] [tertiary]'      Increment patch version.");
                Console.WriteLine("  '--updateMajor [secondary] [tertiary]' Increment major version.");
                Console.WriteLine("  '--updateMinor [secondary] [tertiary]' Increment minor version.");
                Console.WriteLine("Secondary args:");
                Console.WriteLine("  '<primary> --forceUpdate [tertiary]'   Force rebuild/reinstall even if nothing changed. Requires an update primary arg.");
                Console.WriteLine("Tertiary args:");
                Console.WriteLine("  '<primary> <secondary> --skipVersion'  Do not update version number. Requires --forceUpdate arg.");
                return 0;
            }
            if (!ToolBoxHandshake.VerifyToolBoxHost()) return 1;
            if (Update.TryHandleUpdateCommandTree(args, "WrapHDL", "HarmonyDebugWrapper.csproj", out var updateExitCode)) return updateExitCode;
            if (hasHelp && hasScan) { Console.WriteLine("⚠️ Only one primary arg can be used at a time."); return 0; }
            if (hasScan) { RepoMap.MapRepoFolderStructure(); return 0; }
            RepoMap map;
            try
            {
                map = RepoMap.LoadRepoMap();
                Console.WriteLine($"🔃 Loaded cached repo map from: {RepoMap.GetCachedRepoMapPath()}");
                Console.WriteLine($"🕓 Repo map last updated: {map.RepoMapCreatedAt}");
            }
            catch (Exception ex) { Console.WriteLine($"❌ {ex.Message}"); return 1; }
            Console.WriteLine($"📁 Repo root: {map.RootPath}");
            Console.WriteLine($"📁 Logger: {map.LoggerPath}");
            try
            {
                var target = FindSolutionOrProject(Directory.GetCurrentDirectory());
                Console.WriteLine($"🎯 Target solution: {target}");
                var loggerSrc = map.LoggerPath;
                if (!File.Exists(loggerSrc)) { Console.WriteLine($"❌ HarmonyDebugLogger not found at path: {loggerSrc}"); return 1; }
                var loggerCode = File.ReadAllText(loggerSrc);
                var syntaxTree = CSharpSyntaxTree.ParseText(loggerCode);
                var loggerProj = Path.Combine(Path.GetDirectoryName(loggerSrc)!, "HarmonyDebugLogger.csproj");
                if (!File.Exists(loggerProj)) throw new FileNotFoundException("❌ Harmony Debug Logger project file not found.", loggerProj);
                Console.WriteLine("🔧 Building Harmony Debug Logger project...");
                var (buildResultExitCode, buildResultOutput, buildResultError) = Update.Cmd.Run("dotnet", $"build \"{loggerProj}\" -c Release", streamToConsole: true, exitOnFail: false);
                if (buildResultExitCode != 0) { Console.WriteLine("❌ Harmony Debug Logger build failed."); return 1; }
                var loggerDLL = Path.Combine(Path.GetDirectoryName(loggerProj)!, "bin", "Release", "net10.0", "HarmonyDebugLogger.dll");
                if (!File.Exists(loggerDLL)) throw new FileNotFoundException("⚠️ Harmony Debug Logger build succeeded but DLL missing", loggerDLL);
                Console.WriteLine($"✅ Compiled Harmony Debug Logger: {loggerDLL}");
                var cmd = $"build \"{target}\" /p:ReferencePath=\"{loggerDLL}\"";
                Console.WriteLine($"> dotnet {cmd}");
                Update.Cmd.Run("dotnet", cmd);
                try { if (File.Exists(loggerDLL)) File.Delete(loggerDLL); } catch { }
                return 0;
            }
            catch (Exception ex) { Console.WriteLine($"💥 Error: {ex.Message}"); return 1; }
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
        public List<string> ProjectFiles { get; set; } = [];
        public List<string> SolutionFiles { get; set; } = [];
        public DateTime RepoMapCreatedAt { get; set; } = DateTime.Now;
        private static readonly System.Text.Json.JsonSerializerOptions CachedJsonOptions = new() { WriteIndented = true };
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
            if (!File.Exists(cachedRepoMap)) throw new FileNotFoundException("Repo cache not found. Run with --scanFolderStructure first.");
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
}