param
(
    [switch]$skipVersion,
    [int]$pidToWait,
    [string]$pkgDir,
    [string]$csprojPath,
    [string]$oldVersion,
    [string]$newVersion
)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Write-Host "⌛ Waiting for WrapHDL process PID=$pidToWait to exit..."
while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 200 }
Write-Host "✅ WrapHDL process exited. Proceeding with update..."
Write-Host "⚙️ Updating WrapHDL..."
Write-Host "🧠 Executing: dotnet tool update --global --add-source `"$pkgDir`" WrapHDL"
& dotnet tool update --global --add-source $pkgDir WrapHDL
if ($LASTEXITCODE -eq 0)
{
    $timestamp = Get-Date -Format "dd-MM-yyyy HH:mm:ss"
    Write-Host "✅ WrapHDL successfully updated to latest build at $timestamp"
}
else
{
    Write-Host "❌ WrapHDL update failed with exit code $LASTEXITCODE"
    if (-not $skipVersion)
    {
        $proj = $csprojPath
        $text = Get-Content $proj -Raw
        $text = $text -replace "<Version>$newVersion</Version>", "<Version>$oldVersion</Version>"
        Set-Content $proj $text -Encoding UTF8
        Write-Host "↩️ Restored version number: $newVersion → $oldVersion"
    }
}