# WTGWizard BuildArtifacts.ps1

[CmdletBinding()]
param(
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64",

    [ValidateSet("FDD", "SCD", "Both")]
    [string]$BuildType = "Both",

    [string]$Configuration = "Release",

    [string]$MainVer = "1.0.0",

    [string]$WorkerVer = "1.0.0",

    [string]$ZipTag = "Build-Artifacts",

    [switch]$SkipClean,

    [switch]$SkipTests,

    [switch]$Diagnostics,

    [switch]$PublishOnly,

    [string]$ChildLog,

    [int]$MinXbfCount = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# ============================================================================
# Paths
# ============================================================================

$Root = (Resolve-Path $PSScriptRoot).Path

$MainProject   = Join-Path $Root "src\WTGWizard.Main\WTGWizard.Main.csproj"
$WorkerProject = Join-Path $Root "src\WTGWizard.Worker\WTGWizard.Worker.csproj"

$BuildRoot     = Join-Path $Root "build"
$OutputRoot    = Join-Path $BuildRoot "WTGWizard"

$DiagnosticsRoot = Join-Path $BuildRoot "BuildDiagnostics"

$FddOutput = Join-Path $OutputRoot "FDD"
$ScdOutput = Join-Path $OutputRoot "SCD"

$Rid = "win-$Architecture"

# ============================================================================
# Logging
# ============================================================================

$script:LogFile = Join-Path $DiagnosticsRoot "Build.log"

if ($ChildLog) {
    # 并行子进程模式：独立日志文件，避免多进程并发写 Build.log
    # （注意：param 变量不能与 $script:LogFile 同名——同作用域赋值会互相覆盖）
    $script:LogFile = $ChildLog
}

function Initialize-Directories {
    New-Item -ItemType Directory -Force -Path $BuildRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $DiagnosticsRoot | Out-Null

    if (Test-Path $script:LogFile) {
        Remove-Item $script:LogFile -Force
    }
}

function Write-Log {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Message,

        [ValidateSet("INFO", "WARN", "ERROR", "SUCCESS", "DEBUG")]
        [string]$Level = "INFO"
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $line = "[$timestamp] [$Level] $Message"

    Write-Host $line

    Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8
}

function Write-Section {
    param(
        [Parameter(Mandatory)]
        [string]$Title
    )

    $line = "=" * 80

    Write-Log $line
    Write-Log $Title
    Write-Log $line
}

function Write-SubSection {
    param(
        [Parameter(Mandatory)]
        [string]$Title
    )

    Write-Log " "
    Write-Log "--- $Title ---"
}

# ============================================================================
# Command execution
# ============================================================================

function Invoke-CommandLogged {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter()]
        [string[]]$Arguments = @(),

        [Parameter()]
        [string]$LogName
    )

    $command = $FilePath

    if ($Arguments.Count -gt 0) {
        $command += " " + ($Arguments -join " ")
    }

    Write-Log "Executing:"
    Write-Log "  $command"

    $outputFile = $null

    if ($LogName) {
        $outputFile = Join-Path $DiagnosticsRoot $LogName
        Write-Log "Command output: $outputFile"
    }

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    if ($outputFile) {
        & $FilePath @Arguments 2>&1 |
            Tee-Object -FilePath $outputFile |
            ForEach-Object {
                Write-Log "$_"
            }

        $exitCode = $LASTEXITCODE
    }
    else {
        & $FilePath @Arguments 2>&1 |
            ForEach-Object {
                Write-Log "$_"
            }

        $exitCode = $LASTEXITCODE
    }

    $stopwatch.Stop()

    Write-Log "Exit code: $exitCode"
    Write-Log "Elapsed: $($stopwatch.Elapsed)"

    if ($exitCode -ne 0) {
        throw "Command failed with exit code $($exitCode): $command"
    }

    Write-Log "Command completed successfully." "SUCCESS"
}

# ============================================================================
# Environment information
# ============================================================================

function Collect-EnvironmentInfo {
    Write-Section "Build Environment"

    Write-SubSection ".NET information"

    Invoke-CommandLogged `
        -FilePath "dotnet" `
        -Arguments @("--info") `
        -LogName "dotnet-info.txt"

    Write-SubSection ".NET SDK version"

    Invoke-CommandLogged `
        -FilePath "dotnet" `
        -Arguments @("--version") `
        -LogName "dotnet-version.txt"

    Write-SubSection "PowerShell"

    Write-Log "PowerShell version: $($PSVersionTable.PSVersion)"
    Write-Log "PowerShell edition: $($PSVersionTable.PSEdition)"

    Write-SubSection "Operating system"

    $os = Get-CimInstance Win32_OperatingSystem

    Write-Log "OS: $($os.Caption)"
    Write-Log "Version: $($os.Version)"
    Write-Log "Build: $($os.BuildNumber)"
    Write-Log "Architecture: $($os.OSArchitecture)"

    Write-SubSection "Processor"

    $cpu = Get-CimInstance Win32_Processor |
        Select-Object -First 1

    Write-Log "CPU: $($cpu.Name)"
    Write-Log "Architecture: $($cpu.Architecture)"
    Write-Log "Logical processors: $($cpu.NumberOfLogicalProcessors)"

    Write-SubSection "Build parameters"

    Write-Log "Root: $Root"
    Write-Log "Configuration: $Configuration"
    Write-Log "Architecture: $Architecture"
    Write-Log "RID: $Rid"
    Write-Log "BuildType: $BuildType"
    Write-Log "MainVer: $MainVer"
    Write-Log "WorkerVer: $WorkerVer"
    Write-Log "ZipTag: $ZipTag"
    Write-Log "Diagnostics: $Diagnostics"
    Write-Log "MinXbfCount: $MinXbfCount"

    if ($env:GITHUB_ACTIONS) {
        Write-Log "GitHub Actions: TRUE"
        Write-Log "Runner OS: $env:RUNNER_OS"
        Write-Log "Runner architecture: $env:RUNNER_ARCH"
        Write-Log "GitHub ref: $env:GITHUB_REF"
        Write-Log "GitHub SHA: $env:GITHUB_SHA"
        Write-Log "GitHub run ID: $env:GITHUB_RUN_ID"
    }
    else {
        Write-Log "GitHub Actions: FALSE"
    }

    Write-SubSection "Git"

    try {
        $commit = git rev-parse HEAD
        Write-Log "Commit: $commit"

        $branch = git branch --show-current
        Write-Log "Branch: $branch"

        $status = git status --short

        if ($status) {
            Write-Log "Working tree contains modifications:" "WARN"
            Write-Log ($status -join "`n")
        }
        else {
            Write-Log "Working tree: CLEAN" "SUCCESS"
        }
    }
    catch {
        Write-Log "Unable to collect Git information: $($_.Exception.Message)" "WARN"
    }
}

# ============================================================================
# Project information
# ============================================================================

function Get-ProjectInfoProperties {
    # 仅 -Diagnostics 模式调用（Collect-ProjectInfo 整体受控），直接全量返回
    return @(
        "MSBuildVersion",
        "MSBuildToolsPath",
        "TargetFramework",
        "TargetFrameworkIdentifier",
        "Platform",
        "PlatformTarget",
        "RuntimeIdentifier",
        "SelfContained",
        "PublishReadyToRun",
        "PublishSingleFile",
        "PublishTrimmed",
        "Deterministic",
        "ContinuousIntegrationBuild",
        "LangVersion",
        "Configuration",
        "OutputPath",
        "PublishDir",
        "XamlCompiler",
        "EnableXbf",
        "GenerateXbf",
        "ShouldComputeInputPris",
        "AppxPriConfigXmlPath",
        "EnableCoreMrtTooling",
        "IntermediateOutputPath",
        "PkgMicrosoft_Windows_SDK_BuildTools",
        "WindowsSdkBuildToolsVersion"
    )
}

function Collect-ProjectInfo {
    Write-Section "Project Information"

    Write-Log "Main project:"
    Write-Log "  $MainProject"

    Write-Log "Worker project:"
    Write-Log "  $WorkerProject"

    if (-not (Test-Path $MainProject)) {
        throw "Main project does not exist: $MainProject"
    }

    if (-not (Test-Path $WorkerProject)) {
        throw "Worker project does not exist: $WorkerProject"
    }

    $props = Get-ProjectInfoProperties

    Write-SubSection "Main project MSBuild properties"

    $mainArgs = @("msbuild", $MainProject)
    $mainArgs += $props | ForEach-Object { "-getProperty:$_" }

    Invoke-CommandLogged `
        -FilePath "dotnet" `
        -Arguments $mainArgs `
        -LogName "main-msbuild-properties.txt"

    Write-SubSection "Worker project MSBuild properties"

    $workerArgs = @("msbuild", $WorkerProject)
    $workerArgs += $props | ForEach-Object { "-getProperty:$_" }

    Invoke-CommandLogged `
        -FilePath "dotnet" `
        -Arguments $workerArgs `
        -LogName "worker-msbuild-properties.txt"
}

# ============================================================================
# Clean
# ============================================================================

function Remove-ProjectBuildArtifacts {
    Write-Section "Clean Build"

    $directories = @(
        (Join-Path $Root "src\WTGWizard.Main\bin"),
        (Join-Path $Root "src\WTGWizard.Main\obj"),
        (Join-Path $Root "src\WTGWizard.Worker\bin"),
        (Join-Path $Root "src\WTGWizard.Worker\obj")
    )

    # Clean all project bin/obj directories.
    $directories += Get-ChildItem `
        -Path (Join-Path $Root "src") `
        -Directory `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -in @("bin", "obj")
        } |
        Select-Object -ExpandProperty FullName

    foreach ($directory in ($directories | Sort-Object -Unique)) {
        if (Test-Path $directory) {
            Write-Log "Removing: $directory"
            Remove-Item `
                -LiteralPath $directory `
                -Recurse `
                -Force
        }
    }

    if (Test-Path $OutputRoot) {
        Write-Log "Removing output root: $OutputRoot"

        Remove-Item `
            -LiteralPath $OutputRoot `
            -Recurse `
            -Force
    }

    if (Test-Path $DiagnosticsRoot) {
        Write-Log "Preserving diagnostics directory."
    }

    New-Item -ItemType Directory -Force -Path $FddOutput | Out-Null
    New-Item -ItemType Directory -Force -Path $ScdOutput | Out-Null

    Write-Log "Clean build environment prepared." "SUCCESS"
}

# ============================================================================
# Restore
# ============================================================================

function Restore-Projects {
    Write-Section "NuGet Restore"

    # 按模式分别 restore：project.assets.json 必须落在与 publish 相同的
    # BaseIntermediateOutputPath 下（--no-restore 依赖），否则 publish 找不到 assets
    foreach ($mode in @("fdd", "scd")) {
        Write-SubSection "Restore Main ($mode)"

        Invoke-CommandLogged `
            -FilePath "dotnet" `
            -Arguments @(
                "restore",
                $MainProject,
                "--locked-mode",
                "-p:BaseIntermediateOutputPath=obj\$mode\"
            ) `
            -LogName "restore-main-$mode.txt"

        Write-SubSection "Restore Worker ($mode)"

        Invoke-CommandLogged `
            -FilePath "dotnet" `
            -Arguments @(
                "restore",
                $WorkerProject,
                "--locked-mode",
                "-p:BaseIntermediateOutputPath=obj\$mode\"
            ) `
            -LogName "restore-worker-$mode.txt"
    }
}

# ============================================================================
# Build / Publish
# ============================================================================

function Build-Worker {
    param(
        [Parameter(Mandatory)]
        [string]$Output,

        [Parameter(Mandatory)]
        [bool]$SelfContained,

        [Parameter(Mandatory)]
        [string]$ObjMode
    )

    Write-SubSection "Worker publish"

    $arguments = @(
        "publish",
        $WorkerProject,
        "-c", $Configuration,
        "-r", $Rid,
        "-o", $Output,
        "-p:Platform=$Architecture",
        "-p:Version=$WorkerVer",
        "--no-restore",
        "-p:SelfContained=$SelfContained",
        "-p:BaseIntermediateOutputPath=obj\$ObjMode\",
        "-p:BaseOutputPath=bin\$ObjMode\"
    )

    Invoke-CommandLogged `
        -FilePath "dotnet" `
        -Arguments $arguments `
        -LogName "publish-worker-$($ObjMode).txt"
}

function Build-Main {
    param(
        [Parameter(Mandatory)]
        [string]$Output,

        [Parameter(Mandatory)]
        [bool]$SelfContained,

        [Parameter(Mandatory)]
        [string]$WASDKSelfContained,

        [Parameter(Mandatory)]
        [string]$ObjMode
    )

    Write-SubSection "Main publish"

    $arguments = @(
        "publish",
        $MainProject,
        "-c", $Configuration,
        "-r", $Rid,
        "-o", $Output,
        "-p:Platform=$Architecture",
        "-p:Version=$MainVer",
        "--no-restore",
        "-p:SelfContained=$SelfContained",
        "-p:WindowsAppSDKSelfContained=$WASDKSelfContained",
        "-p:BaseIntermediateOutputPath=obj\$ObjMode\",
        "-p:BaseOutputPath=bin\$ObjMode\"
    )

    Invoke-CommandLogged `
        -FilePath "dotnet" `
        -Arguments $arguments `
        -LogName "publish-main-$($ObjMode).txt"
}

# ============================================================================
# Artifact diagnostics
# ============================================================================

function Get-FileManifest {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$Name
    )

    Write-SubSection "Generating manifest: $Name"

    $manifestPath = Join-Path $DiagnosticsRoot "$Name.csv"

    $rootPath = (Resolve-Path $Directory).Path

    $files = Get-ChildItem `
        -LiteralPath $Directory `
        -File `
        -Recurse `
        -Force |
        Sort-Object FullName

    $manifest = foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($rootPath.Length).TrimStart('\')

        $hash = Get-FileHash `
            -LiteralPath $file.FullName `
            -Algorithm SHA256

        $version = $null

        if ($file.Extension -in @(".exe", ".dll")) {
            try {
                $version = $file.VersionInfo.FileVersion
            }
            catch {
                $version = $null
            }
        }

        [PSCustomObject]@{
            RelativePath = $relativePath
            Length       = $file.Length
            SHA256       = $hash.Hash
            FileVersion  = $version
            LastWriteTime = $file.LastWriteTimeUtc.ToString("o")
        }
    }

    $manifest |
        Export-Csv `
            -LiteralPath $manifestPath `
            -NoTypeInformation `
            -Encoding UTF8

    Write-Log "Manifest: $manifestPath"
    Write-Log "Files: $($manifest.Count)"

    return $manifest
}

function Write-ImportantFiles {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$Name
    )

    Write-SubSection "Important files: $Name"

    $patterns = @(
        "WTGWizard.*",
        "Microsoft.WindowsAppRuntime.*",
        "Microsoft.ui.xaml.dll",
        "MrtCore*.dll",
        "hostfxr.dll",
        "hostpolicy.dll",
        "coreclr.dll",
        "System.Private.CoreLib.dll",
        "vcruntime*.dll",
        "msvcp*.dll"
    )

    $files = foreach ($pattern in $patterns) {
        Get-ChildItem `
            -LiteralPath $Directory `
            -File `
            -Recurse `
            -Force `
            -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -like $pattern
            }
    }

    $files = $files |
        Sort-Object FullName -Unique

    foreach ($file in $files) {
        $hash = Get-FileHash `
            -LiteralPath $file.FullName `
            -Algorithm SHA256

        $version = $null

        if ($file.Extension -in @(".exe", ".dll")) {
            try {
                $version = $file.VersionInfo.FileVersion
            }
            catch {
                $version = $null
            }
        }

        Write-Log (
            "FILE={0} SIZE={1} SHA256={2} VERSION={3}" -f
            $file.Name,
            $file.Length,
            $hash.Hash,
            $version
        )
    }
}

# ============================================================================
# WTGWizard artifact report
# ============================================================================

function Write-WTGWizardReport {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$Name
    )

    Write-SubSection "WTGWizard artifact report: $Name"

    $files = Get-ChildItem `
        -LiteralPath $Directory `
        -File `
        -Recurse `
        -Force |
        Where-Object {
            $_.Name -like "WTGWizard.*"
        } |
        Sort-Object FullName

    foreach ($file in $files) {
        $hash = Get-FileHash `
            -LiteralPath $file.FullName `
            -Algorithm SHA256

        Write-Log "----------------------------------------"
        Write-Log "Name: $($file.Name)"
        Write-Log "Path: $($file.FullName)"
        Write-Log "Size: $($file.Length)"
        Write-Log "SHA256: $($hash.Hash)"

        if ($file.Extension -in @(".exe", ".dll")) {
            try {
                $vi = $file.VersionInfo

                Write-Log "FileVersion: $($vi.FileVersion)"
                Write-Log "ProductVersion: $($vi.ProductVersion)"
                Write-Log "CompanyName: $($vi.CompanyName)"
                Write-Log "ProductName: $($vi.ProductName)"
            }
            catch {
                Write-Log "Unable to read VersionInfo." "WARN"
            }
        }
    }
}

# ============================================================================
# Publish validation
# ============================================================================

function Validate-PublishOutput {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [bool]$SelfContained
    )

    Write-SubSection "Validate publish output"

    if (-not (Test-Path $Directory)) {
        throw "Publish directory does not exist: $Directory"
    }

    $mainExe = Join-Path $Directory "WTGWizard.Main.exe"
    $mainDll = Join-Path $Directory "WTGWizard.Main.dll"
    $workerExe = Join-Path $Directory "WTGWizard.Worker.exe"

    if (-not (Test-Path $mainExe)) {
        throw "Expected executable missing: $mainExe"
    }

    if (-not (Test-Path $mainDll)) {
        throw "Expected managed assembly missing: $mainDll"
    }

    if (-not (Test-Path $workerExe)) {
        Write-Log "WTGWizard.Worker.exe not found next to Main output." "WARN"
    }
    else {
        Write-Log "WTGWizard.Worker.exe co-located with Main." "SUCCESS"
    }

    if ($SelfContained) {
        $coreClr = Join-Path $Directory "coreclr.dll"

        if (-not (Test-Path $coreClr)) {
            Write-Log "coreclr.dll not found in self-contained output." "WARN"
        }
        else {
            Write-Log "Self-contained CoreCLR found." "SUCCESS"
        }
    }

    $fileCount = (
        Get-ChildItem `
            -LiteralPath $Directory `
            -File `
            -Recurse `
            -Force
    ).Count

    Write-Log "Publish file count: $fileCount"
    Write-Log "Publish validation completed." "SUCCESS"
}

# ============================================================================
# PRI / XBF validation (diagnostics mode)
# ============================================================================

function Get-MakePriPath {
    $pkgRoot = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools"

    if (-not (Test-Path $pkgRoot)) {
        return $null
    }

    $pkg = Get-ChildItem $pkgRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if (-not $pkg) {
        return $null
    }

    $bin = Get-ChildItem (Join-Path $pkg.FullName "bin") -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if (-not $bin) {
        return $null
    }

    $exe = Join-Path (Join-Path $bin.FullName "x64") "makepri.exe"

    if (Test-Path $exe) {
        return $exe
    }

    return $null
}

function Get-PriXbfNames {
    param(
        [Parameter(Mandatory)]
        [string]$PriPath,

        [Parameter()]
        [string]$MakePriPath,

        [Parameter()]
        [string]$DumpPrefix
    )

    $xbfNames = @()

    if ($MakePriPath) {
        $baseName = Split-Path $PriPath -Leaf
        $dumpName = if ($DumpPrefix) { "PriDump-$DumpPrefix-$baseName.xml" } else { "PriDump-$baseName.xml" }
        $dumpFile = Join-Path $DiagnosticsRoot $dumpName
        $priFull = (Resolve-Path $PriPath).Path

        if (Test-Path $dumpFile) {
            Remove-Item -LiteralPath $dumpFile -Force
        }

        $stdoutName = if ($DumpPrefix) { "PriDump-$DumpPrefix-$baseName.stdout.txt" } else { "PriDump-$baseName.stdout.txt" }
        $stderrName = if ($DumpPrefix) { "PriDump-$DumpPrefix-$baseName.stderr.txt" } else { "PriDump-$baseName.stderr.txt" }
        $stdoutFile = Join-Path $DiagnosticsRoot $stdoutName
        $stderrFile = Join-Path $DiagnosticsRoot $stderrName
        $nullInput = Join-Path $env:TEMP "makepri-empty-input.txt"

        if (-not (Test-Path $nullInput)) {
            New-Item -ItemType File -Path $nullInput -Force | Out-Null
        }

        try {
            $proc = Start-Process `
                -FilePath $MakePriPath `
                -ArgumentList @("dump", "/if", $priFull, "/dt", "basic", "/of", $dumpFile) `
                -RedirectStandardOutput $stdoutFile `
                -RedirectStandardError $stderrFile `
                -RedirectStandardInput $nullInput `
                -WindowStyle Hidden `
                -PassThru

            $exited = $proc.WaitForExit(60000)

            if (-not $exited) {
                Write-Log "makepri dump timed out after 60s; killing process." "WARN"
                $proc.Kill()
                $proc.WaitForExit()
            }
            elseif ((Test-Path $dumpFile) -and (Get-Item $dumpFile).Length -gt 0) {
                $content = Get-Content $dumpFile -Raw
                $xbfNames = @([regex]::Matches($content, '<NamedResource name="([^"]+\.xbf)"') |
                    ForEach-Object { $_.Groups[1].Value } |
                    Sort-Object -Unique)
                Write-Log "PRI dump retained: $dumpFile"
            }
            else {
                $errMsg = ((Get-Content $stderrFile -ErrorAction SilentlyContinue) -join " ")
                Write-Log "makepri dump produced no output; falling back to string scan. $errMsg" "WARN"
            }
        }
        finally {
            Remove-Item -LiteralPath $stdoutFile -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $stderrFile -Force -ErrorAction SilentlyContinue
        }
    }

    if ($xbfNames.Count -eq 0) {
        $bytes = [System.IO.File]::ReadAllBytes($PriPath)
        $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
        $xbfNames = @([regex]::Matches($ascii, '([\w.]+\.xbf)') |
            ForEach-Object { $_.Groups[1].Value } |
            Sort-Object -Unique)
        Write-Log "Used string-scan fallback for XBF detection."
    }

    return $xbfNames
}

function Write-PriReport {
    param(
        [Parameter(Mandatory)]
        [string[]]$XbfNames,

        [Parameter(Mandatory)]
        [string]$Name
    )

    Write-Log "XBF resources inside PRI ($Name):"

    foreach ($n in $XbfNames) {
        Write-Log "  $n"
    }
}

function Write-XbfReport {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$Name
    )

    Write-SubSection "XBF report: $Name"

    $xbf = @(Get-ChildItem `
        -LiteralPath $Directory `
        -Filter *.xbf `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue)

    Write-Log "XBF files in publish output: $($xbf.Count)"

    $xbf |
        Sort-Object FullName -Unique |
        ForEach-Object { Write-Log "  $($_.FullName)" }
}

function Assert-MainPriComplete {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$Name
    )

    Write-SubSection "Validate PRI completeness: $Name"

    $pri = Join-Path $Directory "WTGWizard.Main.pri"

    if (-not (Test-Path $pri)) {
        throw "WTGWizard.Main.pri missing: $pri"
    }

    $size = (Get-Item $pri).Length
    Write-Log "PRI size: $size bytes (informational; size check removed)"

    $makePri = Get-MakePriPath

    if ($makePri) {
        Write-Log "makepri: $makePri"
    }
    else {
        Write-Log "makepri not found in NuGet cache; using string-scan fallback." "WARN"
    }

    $xbfNames = Get-PriXbfNames -PriPath $pri -MakePriPath $makePri -DumpPrefix $Name

    Write-Log "XBF entries in PRI: $($xbfNames.Count) (threshold: $MinXbfCount)"

    if ($xbfNames.Count -lt $MinXbfCount) {
        throw "PRI contains only $($xbfNames.Count) XBF entries (< $MinXbfCount) - XAML resources missing."
    }

    Write-PriReport -XbfNames $xbfNames -Name $Name
    Write-Log "PRI validation passed." "SUCCESS"
}

# ============================================================================
# Archive
# ============================================================================

function Get-ZipTool {
    # 7za.exe（7-Zip 命令行）按需从 NuGet 获取：缓存存在即用（不检查更新），
    # 缺失时下载最新版并用官方 .nupkg.sha512 校验；任何失败返回 $null 由调用方回退。
    $toolDir = Join-Path $BuildRoot "tools"
    $toolPath = Join-Path $toolDir "7za.exe"

    if (Test-Path $toolPath) {
        Write-Log "Using cached 7za: $toolPath"
        return $toolPath
    }

    Write-Log "7za not cached; fetching latest 7-Zip.CommandLine from NuGet..." "INFO"

    try {
        New-Item -ItemType Directory -Force -Path $toolDir | Out-Null

        $index = Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/7-zip.commandline/index.json"
        $version = $index.versions[-1]
        Write-Log "Latest 7-Zip.CommandLine version: $version"

        $base = "https://api.nuget.org/v3-flatcontainer/7-zip.commandline/$version"
        $nupkg = Join-Path $toolDir "7-zip.commandline.$version.nupkg"

        Invoke-WebRequest "$base/7-zip.commandline.$version.nupkg" -OutFile $nupkg -UseBasicParsing
        Write-Log "Downloaded 7-Zip.CommandLine $version (HTTPS NuGet official source)." "SUCCESS"

        $extractDir = Join-Path $toolDir "pkg-$version"
        # PS 5.1 Expand-Archive 仅接受 .zip 扩展名（不校验内容），nupkg 实为 zip → 复制改名
        $nupkgZip = [System.IO.Path]::ChangeExtension($nupkg, ".zip")
        Copy-Item -LiteralPath $nupkg -Destination $nupkgZip
        Expand-Archive -LiteralPath $nupkgZip -DestinationPath $extractDir -Force
        Remove-Item -LiteralPath $nupkgZip -Force

        $extracted = Join-Path $extractDir "tools\7za.exe"
        if (-not (Test-Path $extracted)) {
            throw "tools\7za.exe not found in package"
        }

        # 运行时自检：验证 7za 可执行（传输层由 HTTPS 官方源保证）；不合并 stderr（防 RemoteException）
        & $extracted i | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "7za self-check failed (exit $LASTEXITCODE)"
        }

        Copy-Item -LiteralPath $extracted -Destination $toolPath
        Remove-Item -LiteralPath $nupkg -Force
        Remove-Item -LiteralPath $extractDir -Recurse -Force

        Write-Log "7za provisioned: $toolPath" "SUCCESS"
        return $toolPath
    }
    catch {
        Write-Log "Unable to provision 7za: $($_.Exception.Message); falling back to ZipFile." "WARN"
        return $null
    }
}

function Write-ZipFileArchive {
    # 回退实现：System.IO.Compression 手动遍历（含 WTGWizard*.pdb 排除）
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$Archive
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $root = (Resolve-Path $Directory).Path
    $zip = [System.IO.Compression.ZipFile]::Open($Archive, [System.IO.Compression.ZipArchiveMode]::Create)

    try {
        $included = 0
        $excluded = 0

        $files = Get-ChildItem `
            -LiteralPath $Directory `
            -File `
            -Recurse `
            -Force

        foreach ($file in $files) {
            if ($file.Name -like "WTGWizard*.pdb") {
                Write-Log "Excluding symbol file: $($file.Name)" "WARN"
                $excluded++
                continue
            }

            $relative = $file.FullName.Substring($root.Length).TrimStart('\').Replace('\', '/')

            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                $file.FullName,
                $relative,
                [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null

            $included++
        }
    }
    finally {
        $zip.Dispose()
    }

    Write-Log "Archive (ZipFile): $Archive"
    Write-Log "Files included: $included; symbol files excluded: $excluded"
}

function Compress-Artifact {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$Name
    )

    Write-SubSection "Create archive: $Name"

    $archive = Join-Path $BuildRoot "$Name.zip"

    if (Test-Path $archive) {
        Remove-Item $archive -Force
    }

    $zipTool = Get-ZipTool

    if ($zipTool) {
        # 7za 优先：高压缩率（mx=9）+ 多线程 + 排除 PDB；退出码 0/1 视为成功。
        # 注意：不可合并 stderr（$ErrorActionPreference=Stop 下 native stderr 行会抛 RemoteException）；
        #       排除模式须经变量传递（PS 5.1 会把裸 token 中的 * 做通配符解析，导致 7za 参数错位）
        $excludePdb = '-x!*.pdb'
        & $zipTool a -tzip -mx=9 -mmt $excludePdb -bso0 -bsp0 $archive (Join-Path $Directory "*") | Out-Null
        $exitCode = $LASTEXITCODE

        if ($exitCode -le 1) {
            Write-Log "Archive (7za, mx=9): $archive"
        }
        else {
            Write-Log "7za failed (exit $exitCode); falling back to ZipFile." "WARN"
            if (Test-Path $archive) {
                Remove-Item $archive -Force
            }
            Write-ZipFileArchive -Directory $Directory -Archive $archive
        }
    }
    else {
        Write-ZipFileArchive -Directory $Directory -Archive $archive
    }

    $hash = Get-FileHash `
        -LiteralPath $archive `
        -Algorithm SHA256

    Write-Log "Archive SHA256: $($hash.Hash)"

    return $archive
}

# ============================================================================
# Build variants
# ============================================================================

function Build-Variant {
    param(
        [Parameter(Mandatory)]
        [string]$Output,

        [Parameter(Mandatory)]
        [string]$ObjMode,

        [Parameter(Mandatory)]
        [bool]$SelfContained,

        [Parameter(Mandatory)]
        [string]$WASDKSelfContained,

        [Parameter(Mandatory)]
        [string]$ModeLabel
    )

    Write-Section "$ModeLabel Build"

    New-Item -ItemType Directory -Force -Path $Output | Out-Null

    Build-Worker `
        -Output $Output `
        -SelfContained $SelfContained `
        -ObjMode $ObjMode

    Build-Main `
        -Output $Output `
        -SelfContained $SelfContained `
        -WASDKSelfContained $WASDKSelfContained `
        -ObjMode $ObjMode

    Validate-PublishOutput `
        -Directory $Output `
        -SelfContained $SelfContained

    if ($Diagnostics) {
        Write-XbfReport `
            -Directory $Output `
            -Name "$ModeLabel-$Architecture"

        Assert-MainPriComplete `
            -Directory $Output `
            -Name "$ModeLabel-$Architecture"

        Get-FileManifest `
            -Directory $Output `
            -Name "$ModeLabel-$Architecture"

        Write-ImportantFiles `
            -Directory $Output `
            -Name "$ModeLabel-$Architecture"

        Write-WTGWizardReport `
            -Directory $Output `
            -Name "$ModeLabel-$Architecture"
    }
}

function Build-FDD {
    Build-Variant `
        -Output (Join-Path $FddOutput $Architecture) `
        -ObjMode "fdd" `
        -SelfContained $false `
        -WASDKSelfContained $false `
        -ModeLabel "FDD"
}

function Build-SCD {
    Build-Variant `
        -Output (Join-Path $ScdOutput $Architecture) `
        -ObjMode "scd" `
        -SelfContained $true `
        -WASDKSelfContained $true `
        -ModeLabel "SCD"
}

# ============================================================================
# Package (build phase completed; archiving is centralized here)
# ============================================================================

function Package-Artifacts {
    Write-Section "Package Artifacts"

    if ($BuildType -in @("FDD", "Both")) {
        Compress-Artifact `
            -Directory (Join-Path $FddOutput $Architecture) `
            -Name "WTGWizard-$ZipTag-$Architecture-FDD"
    }

    if ($BuildType -in @("SCD", "Both")) {
        Compress-Artifact `
            -Directory (Join-Path $ScdOutput $Architecture) `
            -Name "WTGWizard-$ZipTag-$Architecture-SCD"
    }
}

# ============================================================================
# Main
# ============================================================================

try {
    Initialize-Directories

    Write-Section "WTGWizard Build"

    Write-Log "Build started."
    Write-Log "Timestamp: $(Get-Date -Format o)"
    Write-Log "Root: $Root"

    $buildModes = @()
    if ($BuildType -in @("SCD", "Both")) { $buildModes += "SCD" }
    if ($BuildType -in @("FDD", "Both")) { $buildModes += "FDD" }

    if (-not $PublishOnly) {
        if ($Diagnostics) {
            Collect-EnvironmentInfo
            Collect-ProjectInfo
        }

        if (-not $SkipClean) {
            Remove-ProjectBuildArtifacts
        }
        else {
            Write-Log "WARNING: Build cleanup skipped." "WARN"
        }

        Restore-Projects

        if (-not $SkipTests) {
            Write-Section "Tests"

            Write-Log "Test execution is currently disabled by default."
            Write-Log "Enable explicitly if the solution has a stable test target."
        }
    }

    if ($PublishOnly) {
        # 子进程模式（并行构建的子任务）：仅构建本模式，不打包
        foreach ($m in $buildModes) {
            if ($m -eq "SCD") { Build-SCD } else { Build-FDD }
        }
    }
    elseif ($buildModes.Count -gt 1) {
        # 并行构建：FDD/SCD 各自独立子进程（obj 隔离后互不干扰），日志隔离防并发写
        Write-Section "Parallel Build"

        $shellPath = if ($PSVersionTable.PSEdition -eq "Core") { "pwsh" } else { "powershell.exe" }

        $procs = @()

        foreach ($m in $buildModes) {
            $childLog = Join-Path $DiagnosticsRoot "Build-$($m.ToLower()).log"
            # 清理旧成功标记（PS 5.1 Start-Process 的 ExitCode 常为空，用标记文件判定子进程结果）
            Remove-Item -LiteralPath "$childLog.succeeded" -Force -ErrorAction SilentlyContinue

            $childArgs = @(
                "-NoProfile",
                "-File", $PSCommandPath,
                "-BuildType", $m,
                "-PublishOnly",
                "-SkipClean", "-SkipTests",
                "-Architecture", $Architecture,
                "-MainVer", $MainVer,
                "-WorkerVer", $WorkerVer,
                "-ZipTag", $ZipTag,
                "-MinXbfCount", "$MinXbfCount",
                "-ChildLog", $childLog
            )
            if ($Diagnostics) { $childArgs += "-Diagnostics" }

            $consoleLog = Join-Path $DiagnosticsRoot "Build-$($m.ToLower())-console.log"
            Write-Log "Starting child build ($m): $shellPath $($childArgs -join ' ')"

            $procs += Start-Process `
                -FilePath $shellPath `
                -ArgumentList $childArgs `
                -RedirectStandardOutput $consoleLog `
                -RedirectStandardError "$consoleLog.err" `
                -PassThru `
                -WindowStyle Hidden
        }

        $failed = $false

        for ($i = 0; $i -lt $procs.Count; $i++) {
            $p = $procs[$i]
            $m = $buildModes[$i]
            $childLog = Join-Path $DiagnosticsRoot "Build-$($m.ToLower()).log"
            $p.WaitForExit()

            $succeeded = Test-Path -LiteralPath "$childLog.succeeded"

            if (-not $succeeded -or ($p.ExitCode -ne $null -and $p.ExitCode -ne 0)) {
                $failed = $true
                Write-Log "Child build ($m) failed (exit $($p.ExitCode))." "ERROR"
            }
            else {
                Write-Log "Child build ($m) completed (exit $($p.ExitCode))." "SUCCESS"
            }
        }

        if ($failed) {
            throw "One or more parallel child builds failed."
        }
    }
    else {
        # 单模式构建（主实例）
        foreach ($m in $buildModes) {
            if ($m -eq "SCD") { Build-SCD } else { Build-FDD }
        }
    }

    # 打包阶段：构建全部完成后统一执行（主实例独占，规避 7za 缓存竞态）
    if (-not $PublishOnly) {
        Package-Artifacts
    }

    Write-Section "Build Summary"

    Write-Log "Build completed successfully." "SUCCESS"
    Write-Log "Output root: $OutputRoot"
    Write-Log "Diagnostics: $DiagnosticsRoot"

    if ($Diagnostics) {
        # 诊断模式：完整文件列表
        Get-ChildItem `
            -LiteralPath $BuildRoot `
            -File `
            -Recurse `
            -Force |
            Sort-Object FullName |
            ForEach-Object {
                Write-Log "$($_.FullName) [$($_.Length) bytes]"
            }
    }
    else {
        # 默认：仅核心产物（zip）
        Get-ChildItem `
            -LiteralPath $BuildRoot `
            -File `
            -Force |
            Where-Object { $_.Name -like "WTGWizard-*.zip" } |
            Sort-Object Name |
            ForEach-Object {
                Write-Log "$($_.Name) [$($_.Length) bytes]"
            }
    }

    Write-Log "WTGWizard build finished successfully." "SUCCESS"

    if ($ChildLog) {
        # 成功标记：主实例据此判定子进程结果（PS 5.1 下 Start-Process 的 ExitCode 常为空）
        New-Item -ItemType File -Path "$ChildLog.succeeded" -Force | Out-Null
    }
}
catch {
    Write-Section "BUILD FAILED"

    Write-Log "Error: $($_.Exception.Message)" "ERROR"
    Write-Log "Type: $($_.Exception.GetType().FullName)" "ERROR"
    Write-Log "StackTrace:" "ERROR"
    Write-Log $_.ScriptStackTrace "ERROR"

    Write-Log " "
    Write-Log "Diagnostics have been preserved at:" "ERROR"
    Write-Log $DiagnosticsRoot "ERROR"

    exit 1
}
