using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
[assembly: SupportedOSPlatform("windows")]
namespace HarmonyDebugWrapper.PowerShellIntegrator
{
    static class PwshIntegrator
    {
        const string StartMarker = "# >>> WrapHDL shell integration >>>";
        const string EndMarker = "# <<< WrapHDL shell integration <<<";
        const string SentinelPrefix = "WRAPHDL_UPDATER_PID=";
        static readonly string IntegrationBlock = string.Join(Environment.NewLine, new[]
        {
            StartMarker,
            "function WrapHDL",
            "{",
            "    $exe = (Get-Command WrapHDL -CommandType Application -ErrorAction SilentlyContinue).Source",
            "    if (-not $exe) { throw \"WrapHDL executable not found.\" }",
            "    $old = $env:WRAPHDL_SHELL_INTEGRATED",
            "    $env:WRAPHDL_SHELL_INTEGRATED = '1'",
            "    $pidFile = Join-Path $env:TEMP (\"WrapHDL_UpdaterPid_\" + [Guid]::NewGuid().ToString('N') + \".txt\")",
            "    $origInEnc = [Console]::InputEncoding",
            "    $origOutEnc = [Console]::OutputEncoding",
            "    $origOutputEncoding = $OutputEncoding",
            "    try",
            "    {",
            "        Remove-Item $pidFile -Force -ErrorAction SilentlyContinue | Out-Null",
            "        $utf8 = [System.Text.UTF8Encoding]::new($false)",
            "        [Console]::InputEncoding = $utf8",
            "        [Console]::OutputEncoding = $utf8",
            "        $OutputEncoding = $utf8",
            "        & $exe @args --_updaterPidFile $pidFile",
            "    }",
            "    finally",
            "    {",
            "        [Console]::InputEncoding = $origInEnc",
            "        [Console]::OutputEncoding = $origOutEnc",
            "        $OutputEncoding = $origOutputEncoding",
            "        if ($null -eq $old) { Remove-Item Env:\\WRAPHDL_SHELL_INTEGRATED -ErrorAction SilentlyContinue | Out-Null }",
            "        else { $env:WRAPHDL_SHELL_INTEGRATED = $old }",
            "    }",
            "    if (Test-Path $pidFile)",
            "    {",
            "        $pidText = (Get-Content $pidFile -Raw -ErrorAction SilentlyContinue).Trim()",
            "        Remove-Item $pidFile -Force -ErrorAction SilentlyContinue | Out-Null",
            "        if ($pidText -match '^\d+$')",
            "        {",
            "            $p = Get-Process -Id ([int]$pidText) -ErrorAction SilentlyContinue",
            "            if ($p) { try { $p.WaitForExit() } catch { } }",
            "        }",
            "    }",
            "}",
            EndMarker,
            ""
        });
        static string NormalizeBlock(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();
        static bool ExistingBlockMatches(string text, int startIndex, int endIndex)
        {
            var existing = text.Substring(startIndex, endIndex - startIndex);
            return string.Equals(NormalizeBlock(existing), NormalizeBlock(IntegrationBlock), StringComparison.Ordinal);
        }
        static string UpsertBlock(string text)
        {
            var s = text.IndexOf(StartMarker, StringComparison.Ordinal);
            var e = text.IndexOf(EndMarker, StringComparison.Ordinal);
            if (s >= 0 && e >= s)
            {
                var eEnd = e + EndMarker.Length;
                if (ExistingBlockMatches(text, s, eEnd)) return text;
                var before = text[..s].TrimEnd();
                var after = text[eEnd..].TrimStart();
                return (before + Environment.NewLine + IntegrationBlock + after).TrimEnd() + Environment.NewLine;
            }
            var trimmed = text.TrimEnd();
            if (trimmed.Length == 0) return IntegrationBlock;
            return trimmed + Environment.NewLine + Environment.NewLine + IntegrationBlock;
        }
        public static bool IsIntegratedShell() => string.Equals(Environment.GetEnvironmentVariable("WRAPHDL_SHELL_INTEGRATED"), "1", StringComparison.Ordinal);
        public static void PrintUpdaterPidSentinel(int pid)
        {
            if (!IsIntegratedShell()) return;
            Console.WriteLine($"{SentinelPrefix}{pid}");
            Console.Out.Flush();
        }
        public static void EnsureShellIntegrationAndRestartIfUpdated(string pwshExePath, string[] wrapHdlArgs)
        {
            try
            {
                if (IsIntegratedShell()) return;
                if (!EnsureInstalled(pwshExePath, out var profilePath)) return;
                RestartIntoIntegratedSession(pwshExePath, profilePath, wrapHdlArgs);
            }
            catch (Exception ex) { Console.WriteLine($"⚠️ Shell integration install/restart failed: {ex.Message}"); }
        }
        static bool EnsureInstalled(string pwshExePath, out string profilePath)
        {
            profilePath = GetProfilePath(pwshExePath);
            var dir = Path.GetDirectoryName(profilePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            var existing = File.Exists(profilePath) ? File.ReadAllText(profilePath, Encoding.UTF8) : "";
            var updated = UpsertBlock(existing);
            if (string.Equals(existing, updated, StringComparison.Ordinal)) return false;
            if (File.Exists(profilePath))
            {
                var backup = profilePath + ".bak." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                try { File.Copy(profilePath, backup, false); } catch { }
            }
            File.WriteAllText(profilePath, updated, new UTF8Encoding(false));
            return true;
        }
        static void RestartIntoIntegratedSession(string pwshExePath, string profilePath, string[] wrapHdlArgs)
        {
            var psi = new ProcessStartInfo { FileName = pwshExePath, UseShellExecute = false, CreateNoWindow = false, RedirectStandardOutput = false, RedirectStandardError = false, RedirectStandardInput = false, WorkingDirectory = Environment.CurrentDirectory };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoExit");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add($"& {{ . '{profilePath.Replace("'", "''")}'; WrapHDL @args }}");
            foreach (var a in wrapHdlArgs) psi.ArgumentList.Add(a);
            Process.Start(psi);
            Environment.Exit(0);
        }
        static string GetProfilePath(string pwshExePath)
        {
            var psi = new ProcessStartInfo { FileName = pwshExePath, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("$PROFILE.CurrentUserAllHosts");
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start pwsh to query profile path.");
            var output = p.StandardOutput.ReadToEnd().Trim();
            var err = p.StandardError.ReadToEnd().Trim();
            p.WaitForExit();
            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException($"Failed to get PowerShell profile path. Exit={p.ExitCode}. Error={err}");
            return output;
        }
    }
}