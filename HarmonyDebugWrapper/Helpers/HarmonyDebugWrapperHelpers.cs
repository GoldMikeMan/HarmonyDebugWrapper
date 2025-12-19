using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
[assembly: SupportedOSPlatform("windows")]
namespace HarmonyDebugWrapper.Helpers
{
    public static partial class RegexHelpers
    {
        [GeneratedRegex("<Version>(.*?)</Version>", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
        public static partial Regex VersionRegex();
        [GeneratedRegex("(\\d+\\.\\d+\\.\\d+(?:\\.\\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
        public static partial Regex PwshVersionRegex();
        [GeneratedRegex(@"Microsoft\.PowerShell\.Preview\s+([\d\.]+)\s", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
        public static partial Regex WingetPwshVersionRegex();
        [GeneratedRegex(@"\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
        public static partial Regex NumericPartRegex();
    }
    public class OtherHelpers
    {
        public static(int ExitCode, string Output, string Error) CommandRunner(string exe, string args, string? workingDir = null, bool silent = false)
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
                if (!silent)
                {
                    if (p.ExitCode != 0)
                    {
                        Console.WriteLine($"❌ Command failed to execute: {exe} {args}");
                        Console.WriteLine("--------------------");
                        if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine($"STDOUT:\n{output}");
                        if (!string.IsNullOrWhiteSpace(error)) Console.WriteLine($"STDERR:\n{error}");
                        Console.WriteLine("--------------------");
                    }
                    else Console.WriteLine($"🧠 Executing: {exe} {args}");
                    string exitMessage = p.ExitCode switch
                    {
                        0 => "✅ Success — operation completed successfully.",
                        1 => "⚠️ General error — check command syntax or output for details.",
                        2 => "❌ Invalid arguments or syntax.",
                        3 => "🚫 Access denied or insufficient permissions.",
                        4 => "📦 Target file or package not found.",
                        5 => "🧱 I/O or path-related error.",
                        127 => "❓ Command not found or missing from PATH.",
                        _ => $"🌀 Unknown or tool-specific exit code ({p.ExitCode})."
                    };
                    Console.WriteLine($"🚪 Exit Code {p.ExitCode}: {exitMessage}");
                    if (p.ExitCode != 0) Environment.Exit(p.ExitCode);
                }
                return (p.ExitCode, output.Trim(), error.Trim());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ failed to execute '{exe} {args}': {ex.Message}");
                return (-1, string.Empty, ex.Message);
            }
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
        public static void Cleanup(string projectDir, string? newVersion = null, string? oldVersion = null, string? csprojPath = null)
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
        public static void DetectTerminalType()
        {
            try
            {
                var parentProc = GetParentProcess();
                string? shellPath = parentProc?.MainModule?.FileName;
                if (string.IsNullOrEmpty(shellPath))
                {
                    Console.WriteLine("🖥️ Unknown terminal.");
                    SearchAndInstallPowerShell7();
                    return;
                }
                else if (shellPath.Contains("cmd"))
                {
                    Console.WriteLine("🖥️ Command Prompt detected.");
                    SearchAndInstallPowerShell7();
                    return;
                }
                else if (shellPath.Contains("powershell") || shellPath.Contains("pwsh"))
                {
                    Console.WriteLine($"🖥️ PowerShell detected: {shellPath}");
                    Console.WriteLine("🔍 Checking PowerShell version...");
                    var (pwshVersionExitCode, pwshVersionOutput, pwshVersionError) = OtherHelpers.CommandRunner(shellPath, "-NoLogo -NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"");
                    if (pwshVersionExitCode == 0 && !string.IsNullOrWhiteSpace(pwshVersionOutput))
                    {
                        int majorDigit = pwshVersionOutput[0] - '0';
                        if (majorDigit == 5)
                        {
                            Console.WriteLine($"🖥️ Terminal: Windows PowerShell 5 — Version: {pwshVersionOutput}");
                            SearchAndInstallPowerShell7();
                            return;
                        }
                        if (majorDigit >= 7)
                        {
                            Console.WriteLine($"🖥️ Terminal: PowerShell 7 — Version: {pwshVersionOutput}");
                            var pwshVersion = pwshVersionOutput.Replace("preview.", ".", StringComparison.OrdinalIgnoreCase).Replace("preview", ".", StringComparison.OrdinalIgnoreCase).Replace("rc.", ".", StringComparison.OrdinalIgnoreCase).Replace("rc", ".", StringComparison.OrdinalIgnoreCase).Trim();
                            var parts = RegexHelpers.NumericPartRegex().Matches(pwshVersion).Select(m => m.Value).ToList();
                            while (parts.Count < 4) parts.Add("0");
                            pwshVersion = string.Join('.', parts.Take(4));
                            Console.WriteLine($"⚙️ Normalized version: {pwshVersion}");
                            Console.WriteLine("🔍 Searching WinGet for latest version...");
                            var (wingetExitCode, wingetOutput, wingetError) = OtherHelpers.CommandRunner("winget", "search Microsoft.PowerShell.Preview");
                            var match = RegexHelpers.WingetPwshVersionRegex().Match(wingetOutput);
                            if (match.Success)
                            {
                                var wingetVersion = match.Groups[1].Value.Trim();
                                var wingetParts = RegexHelpers.NumericPartRegex().Matches(wingetVersion).Select(m => m.Value).ToList();
                                while (wingetParts.Count < 4) wingetParts.Add("0");
                                wingetVersion = string.Join('.', wingetParts.Take(4));
                                Console.WriteLine($"📦 Latest Winget PowerShell version: {wingetVersion}");
                                if (CompareVersions(wingetVersion, pwshVersion) > 0)
                                {
                                    Console.WriteLine($"🔄 Updating PowerShell 7 from {pwshVersion} → {wingetVersion}...");
                                    var (updatePwsh7ExitCode, updatePwsh7output, updatePwsh7error) = OtherHelpers.CommandRunner("winget", "upgrade --id Microsoft.PowerShell.Preview -e --source winget --accept-source-agreements --accept-package-agreements -h");
                                    if (updatePwsh7ExitCode == 0) Console.WriteLine($"✅ Updated PowerShell 7 to {wingetVersion} successfully.");
                                    else Console.WriteLine($"❌ PowerShell 7 update failed with code {updatePwsh7ExitCode}.");
                                }
                                else Console.WriteLine("✅ PowerShell 7 is already up to date.");
                                return;
                            }
                        }
                    }
                }
            }
            catch
            {
                Console.WriteLine("🖥️ Failed to detect terminal type.");
                SearchAndInstallPowerShell7();
                return;
            }
        }
        public static Process? GetParentProcess()
        {
            using var query = new ManagementObjectSearcher("SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = " + Environment.ProcessId);
            using var results = query.Get().Cast<ManagementObject>().FirstOrDefault();
            if (results?["ParentProcessId"] is uint parentPid)
            {
                try { return Process.GetProcessById((int)parentPid); }
                catch { return null; }
            }
            return null;
        }
        public static void RelaunchInPowerShell7(string pwshPath)
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var argsJoined = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
            Console.WriteLine($"🚀 Relaunching in PowerShell 7: {pwshPath}");
            var psi = new ProcessStartInfo(pwshPath, $"-ExecutionPolicy Bypass -NoLogo -Command \"dotnet '{exePath}' {argsJoined}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Environment.CurrentDirectory
            };
            Process.Start(psi);
            Console.WriteLine("🚪 Exiting WrapHDL...");
            Environment.Exit(0);
        }
        public static void SearchAndInstallPowerShell7()
        {
            Console.WriteLine("🔍 Searching for PowerShell 7 installation...");
            string[] defaultPaths = [@"C:\Program Files\PowerShell\7-preview\pwsh.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7-preview", "pwsh.exe")];
            string? pwshPath = defaultPaths.FirstOrDefault(File.Exists);
            if (pwshPath != null)
            {
                Console.WriteLine($"✅ Found PowerShell 7 installation at: {pwshPath}");
                RelaunchInPowerShell7(pwshPath);
                return;
            }
            Console.WriteLine("⚠️ PowerShell 7 not found. Installing preview build via Winget...");
            var (exitCode, wingetInstallPreviewOutput, error) = OtherHelpers.CommandRunner("winget", "install --id Microsoft.PowerShell.Preview -e --source winget --accept-source-agreements --accept-package-agreements -h");
            if (exitCode == 0)
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
            else Console.WriteLine($"❌ PowerShell 7 Preview install failed with code {exitCode}.");
        }
    }
}