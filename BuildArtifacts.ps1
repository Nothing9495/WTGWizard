param(
    [string] $Architecture = "x64",
    [string] $MainVer = "1.0.0",
    [string] $WorkerVer = "1.0.0",
    [string] $Output = "build/WTGWizard",
    [string] $ZipTag = "",          # 非空则打包：WTGWizard-{ZipTag}-{Architecture}[.with-runtimes].zip
    [string] $ZipDir = "build"      # zip 输出目录
)

$ErrorActionPreference = "SilentlyContinue";

# ── zip 打包（保留顶层目录结构，产物校验）──
function New-Package([string] $sourceDir, [string] $suffix) {
    $name = "WTGWizard-$ZipTag-$Architecture$suffix.zip"
    $dest = Join-Path $ZipDir $name

    Write-Host "  Packaging '$sourceDir' -> '$dest' ..."
    Compress-Archive -Path $sourceDir -DestinationPath $dest -Force
    if (-not (Test-Path $dest)) {
        throw "Zip packaging produced no output: $dest"
    }
    Write-Host "  Package OK: $name ($([math]::Round((Get-Item $dest).Length/1MB,1)) MB)"
    Remove-Item $sourceDir -Recurse -Force
    Write-Host "  Removed staging directory '$sourceDir'"
}

# ── dotnet publish 封装（退出码检查 + 日志）──
function Invoke-DotNetPublish([string] $project, [string] $label, [string[]] $publishArgs) {
    Write-Host "  -> Publishing $label ($project)"
    Write-Host "     args: $($publishArgs -join ' ')"
    dotnet publish $project @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit $($LASTEXITCODE)): $label"
    }
    Write-Host "  -> OK: $label"
}

Write-Host "================================================"
Write-Host "  WTGWizard $MainVer | Worker $WorkerVer | $Architecture"
Write-Host "  Output: $Output | ZipTag: $(if ($ZipTag) { $ZipTag } else { '(none)' })"
Write-Host "================================================"

$commonArgs = @("-c", "Release", "-r", "win-$Architecture", "-o", "$Output", "-p:Platform=$Architecture")

# ── 1/2: Build without runtimes（框架依赖）──
Write-Host ""
Write-Host "[1/2] Building WITHOUT runtimes (framework-dependent)"
Remove-Item $Output -Recurse -Force
$workerArgs = $commonArgs + @("-p:Version=$WorkerVer")
Invoke-DotNetPublish "src/WTGWizard.Worker" "Worker $WorkerVer (FDD)" $workerArgs
$mainArgs = $commonArgs + @("-p:Version=$MainVer")
Invoke-DotNetPublish "src/WTGWizard.Main" "Main $MainVer (FDD)" $mainArgs

Remove-Item "$Output/WTGWizard.*.pdb" -Force
Write-Host "  Removed PDB files from '$Output'"

if ($ZipTag) {
    New-Package $Output "";
} else {
    Write-Host "  (ZipTag empty - skipped packaging)"
}

# ── 2/2: Build with runtimes（自包含）──
Write-Host ""
Write-Host "[2/2] Building WITH runtimes (self-contained)"
Remove-Item $Output -Recurse -Force
$workerScArgs = $commonArgs + @("-p:Version=$WorkerVer", "-p:SelfContained=true")
Invoke-DotNetPublish "src/WTGWizard.Worker" "Worker $WorkerVer (SCD)" $workerScArgs
$mainScArgs = $commonArgs + @("-p:Version=$MainVer", "-p:SelfContained=true", "-p:WindowsAppSDKSelfContained=true")
Invoke-DotNetPublish "src/WTGWizard.Main" "Main $MainVer (SCD + WinAppSDK)" $mainScArgs

Remove-Item "$Output/WTGWizard.*.pdb" -Force
Write-Host "  Removed PDB files from '$Output'"

if ($ZipTag) {
    New-Package $Output "-with-runtimes";
} else {
    Write-Host "  (ZipTag empty - skipped packaging)"
}

Write-Host ""
Write-Host "Build complete."
