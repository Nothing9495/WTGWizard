<#
.SYNOPSIS
    WTGWizard BuildArtifacts.ps1 —— 构建发布产物并打包为分发包（单模式，FDD 或 SCD）。

.DESCRIPTION
    依次执行：环境/工程诊断（可选）→ Clean → NuGet restore → 发布 Main/Worker →
    构建原生 Launcher → 校验产物 → staging → zip 打包。

    输出布局：
      未指定 -OutputDir（默认，<repo>\build 下平铺，CI 依赖）：
        <repo>\build\WTGWizard\{FDD|SCD}\x64\          Main 发布产物
        <repo>\build\WTGWizard-<ZipTag>-x64-<模式>.zip 最终分发包
        <repo>\build\BuildDiagnostics\                 日志与诊断（含 Build.log）
        <repo>\build\Launcher\                         原生启动器构建输出
        <repo>\build\Worker-<模式>\                    Worker 独立验证输出
        <repo>\build\tools\                            7za 缓存
        <repo>\build\staging\                          打包临时目录（结束后删除）

      指定 -OutputDir（全部中间产物收敛到 <OutputDir>\WTGWizard）：
        <OutputDir>\WTGWizard\{FDD|SCD}\x64\                  Main 发布产物
        <OutputDir>\WTGWizard\WTGWizard-<ZipTag>-x64-<模式>.zip 最终分发包
        <OutputDir>\WTGWizard\BuildDiagnostics\               日志与诊断（含 Build.log）
        <OutputDir>\WTGWizard\tools\                          7za 缓存
        <OutputDir>\WTGWizard\Temp\Launcher\                  原生启动器构建输出
        <OutputDir>\WTGWizard\Temp\Worker-<模式>\             Worker 独立验证输出
        <OutputDir>\WTGWizard\Temp\staging\                   打包临时目录（结束后删除）

.PARAMETER Architecture
    目标架构，目前仅支持 x64。

.PARAMETER BuildType
    发布形态：FDD（依赖框架）或 SCD（自包含）；由 Properties/PublishProfiles/{FDD|SCD}-x64.pubxml 定义。

.PARAMETER MainVer
    Main 应用版本（写入产物并决定 staging 子目录名 WTGWizard-v{版本}）。

.PARAMETER WorkerVer
    Worker 应用版本。

.PARAMETER ZipTag
    分发包名称中的标签：WTGWizard-<ZipTag>-x64-<BuildType>.zip。

.PARAMETER OutputDir
    构建输出根目录；指定后所有中间产物收敛到其下 WTGWizard 子目录（见 .DESCRIPTION）。
    可为绝对路径，或相对仓库根目录的路径。默认 <repo>\build（布局与是否指定无关，不受影响）。

.PARAMETER SkipClean
    跳过 bin/obj 与旧输出清理。

.PARAMETER Diagnostics
    采集环境/工程信息与产物清单（manifest、PRI/XBF 校验等），用于跨机对比。

.PARAMETER MinXbfCount
    PRI 完整性校验所需的最少 XBF 条目数（默认 20）。

.EXAMPLE
    .\BuildArtifacts.ps1 -BuildType SCD

.EXAMPLE
    .\BuildArtifacts.ps1 -BuildType FDD -OutputDir D:\out -Diagnostics

.EXAMPLE
    .\BuildArtifacts.ps1 -BuildType SCD -OutputDir ..\artifacts -ZipTag v1.0.0

.NOTES
    PowerShell 5.1 兼容；本地不支持多实例并发（并行由 CI matrix 承担）。
#>

[CmdletBinding()]
param(
    [ValidateSet("x64")]
    [string]$Architecture = "x64",

    [ValidateSet("FDD", "SCD")]
    [string]$BuildType = "FDD",

    [string]$MainVer = "1.0.0",

    [string]$WorkerVer = "1.0.0",

    [string]$ZipTag = "Build-Artifacts",

    [string]$OutputDir = "",

    [switch]$SkipClean,

    [switch]$Diagnostics,

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
$LauncherProject = Join-Path $Root "src\WTGWizard.Launcher\WTGWizard.Launcher.vcxproj"

# 构建输出根目录：-OutputDir 覆盖，默认 <root>\build（相对路径按仓库根解析）
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $BuildRoot = Join-Path $Root "build"
}
elseif ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $BuildRoot = [System.IO.Path]::GetFullPath($OutputDir)
}
else {
    $BuildRoot = [System.IO.Path]::GetFullPath((Join-Path $Root $OutputDir))
}

$OutputRoot    = Join-Path $BuildRoot "WTGWizard"

# 指定 -OutputDir 时把所有中间产物收敛到 <OutputRoot>（WTGWizard）；
# 未指定时保持历史布局（build\ 下平铺，CI 依赖该布局）不变。
$ConsolidateOutput = -not [string]::IsNullOrWhiteSpace($OutputDir)

if ($ConsolidateOutput) {
    # <OutputRoot>\
    #   FDD|SCD\x64\       发布产物
    #   BuildDiagnostics\  日志与诊断
    #   tools\             7za 缓存
    #   Temp\              Launcher / Worker-* / staging
    #   WTGWizard-*.zip    分发包
    $DiagnosticsRoot = Join-Path $OutputRoot "BuildDiagnostics"
    $ToolRoot        = Join-Path $OutputRoot "tools"
    $TempRoot        = Join-Path $OutputRoot "Temp"
    $LauncherOutput  = Join-Path $TempRoot "Launcher"
    $StagingRoot     = Join-Path $TempRoot "staging"
    $WorkerOutput    = Join-Path $TempRoot ("Worker-" + $BuildType.ToLower())
    $ArchiveRoot     = $OutputRoot
}
else {
    $DiagnosticsRoot = Join-Path $BuildRoot "BuildDiagnostics"
    $ToolRoot        = Join-Path $BuildRoot "tools"
    $LauncherOutput  = Join-Path $BuildRoot "Launcher"
    $StagingRoot     = Join-Path $BuildRoot "staging"
    $WorkerOutput    = Join-Path $BuildRoot ("Worker-" + $BuildType.ToLower())
    $ArchiveRoot     = $BuildRoot
}

$FddOutput = Join-Path $OutputRoot "FDD"
$ScdOutput = Join-Path $OutputRoot "SCD"

# ============================================================================
# Logging
# ============================================================================

$script:LogFile = Join-Path $DiagnosticsRoot "Build.log"

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

    # 原生命令的 stderr 在 $ErrorActionPreference=Stop 下经 2>&1 合并会被提升为
    # terminating RemoteException（Pitfall 20），真实错误信息随之中断丢失。
    # 故调用期间临时降为 Continue，让 stderr 进入管道被记录；
    # 命令失败仍由下方 $LASTEXITCODE 显式判定。
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"

    try {
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
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
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

    Write-Log "Architecture: $Architecture"
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
        "TargetFrameworkVersion",
        "TargetPlatformMinVersion",
        "SupportedOSPlatformVersion",
        "Platform",
        "PlatformTarget",
        "RuntimeIdentifier",
        "PublishReadyToRun",
        "PublishSingleFile",
        "PublishTrimmed",
        "PublishDir",
        "PublishProfile",
        "WindowsAppSDKSelfContained",
        "SelfContained",
        "Deterministic",
        "ContinuousIntegrationBuild",
        "LangVersion",
        "Configuration",
        "OutputPath",
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

    # -getProperty 评估时携带当前发布 Profile，诊断输出反映实际生效值
    $publishProfileName = "$BuildType-x64"

    Write-SubSection "Main project MSBuild properties (profile: $publishProfileName)"

    $mainArgs = @("msbuild", $MainProject, "-p:PublishProfile=$publishProfileName")
    $mainArgs += $props | ForEach-Object { "-getProperty:$_" }

    Invoke-CommandLogged `
        -FilePath "dotnet" `
        -Arguments $mainArgs `
        -LogName "main-msbuild-properties.txt"

    Write-SubSection "Worker project MSBuild properties (profile: $publishProfileName)"

    $workerArgs = @("msbuild", $WorkerProject, "-p:PublishProfile=$publishProfileName")
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

    # Clean all project bin/obj directories.
    $directories = Get-ChildItem `
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

    if ($ConsolidateOutput) {
        # 收敛布局（-OutputDir）：仅清理发布产物与临时中间产物；
        # BuildDiagnostics / tools / Worker 验证输出的保留行为与默认布局一致
        foreach ($dir in @($FddOutput, $ScdOutput, $LauncherOutput, $StagingRoot)) {
            if (Test-Path $dir) {
                Write-Log "Removing: $dir"
                Remove-Item -LiteralPath $dir -Recurse -Force
            }
        }
    }
    else {
        if (Test-Path $OutputRoot) {
            Write-Log "Removing output root: $OutputRoot"

            Remove-Item `
                -LiteralPath $OutputRoot `
                -Recurse `
                -Force
        }

        foreach ($dir in @($LauncherOutput, $StagingRoot)) {
            if (Test-Path $dir) {
                Write-Log "Removing: $dir"
                Remove-Item -LiteralPath $dir -Recurse -Force
            }
        }
    }

    # 陈旧 zip（历史实验/不同模式残留）会污染 CI upload 通配符 WTGWizard-*.zip，
    # Clean 阶段一并清除。
    Get-ChildItem `
        -LiteralPath $ArchiveRoot `
        -File `
        -Force |
        Where-Object { $_.Name -like "WTGWizard-*.zip" } |
        ForEach-Object {
            Write-Log "Removing stale archive: $($_.FullName)"
            Remove-Item -LiteralPath $_.FullName -Force
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

    # 单模式构建：同一 obj/ 下一次性恢复（发布属性由 PublishProfile 传入，不参与 restore）
    $projects = @(
        [PSCustomObject]@{ Project = $MainProject; Name = "main" },
        [PSCustomObject]@{ Project = $WorkerProject; Name = "worker" }
    )

    foreach ($item in $projects) {
        Write-SubSection "Restore $($item.Name)"

        Invoke-CommandLogged `
            -FilePath "dotnet" `
            -Arguments @(
                "restore",
                $item.Project,
                "--locked-mode"
            ) `
            -LogName "restore-$($item.Name).txt"
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
        [string]$PublishProfile,

        [Parameter(Mandatory)]
        [string]$Version
    )

    Write-SubSection "Worker publish [$PublishProfile]"

    $arguments = @(
        "publish",
        $WorkerProject,
        "-p:PublishProfile=$PublishProfile",
        "-p:PublishDir=$Output",
        "-p:Version=$Version",
        "--no-restore"
    )

    Invoke-CommandLogged `
        -FilePath "dotnet" `
        -Arguments $arguments `
        -LogName "publish-worker-$PublishProfile.txt"
}

function Build-Main {
    param(
        [Parameter(Mandatory)]
        [string]$Output,

        [Parameter(Mandatory)]
        [string]$PublishProfile,

        [Parameter(Mandatory)]
        [string]$Version
    )

    Write-SubSection "Main publish [$PublishProfile]"

    $arguments = @(
        "publish",
        $MainProject,
        "-p:PublishProfile=$PublishProfile",
        "-p:PublishDir=$Output",
        "-p:Version=$Version",
        "--no-restore"
    )

    Invoke-CommandLogged `
        -FilePath "dotnet" `
        -Arguments $arguments `
        -LogName "publish-main-$PublishProfile.txt"
}

# ============================================================================
# Artifact diagnostics
# ============================================================================

function Build-Launcher {
    Write-Section "Launcher Build"

    $vswhere = Join-Path (${env:ProgramFiles(x86)}) "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found. Install Visual Studio 2022 (or Build Tools) with the 'Desktop development with C++' workload to build the launcher."
    }

    # 不合并 stderr（Pitfall 20）：native 工具 stderr 行会抛 RemoteException
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\Current\Bin\MSBuild.exe" |
        Select-Object -First 1
    if (-not ($msbuild -and (Test-Path $msbuild))) {
        throw "MSBuild.exe for C++ projects (vcxproj) not found via vswhere. Install the 'Desktop development with C++' workload."
    }
    Write-Log "VS MSBuild: $msbuild"

    # $MainVer（可含 prerelease 后缀，如 1.0.0-preview1）→ FileVersion 数字四段 "1,0,0,0"
    $segs = (($MainVer -replace '-.*$', '' -replace '[^0-9.]', '') -split '\.') + @('0', '0', '0', '0')
    $versionNumeric = "{0},{1},{2},{3}" -f $segs[0], $segs[1], $segs[2], $segs[3]
    Write-Log "Launcher version: string='$MainVer' numeric=$versionNumeric"

    $intDir = "$LauncherOutput/obj/"
    New-Item -ItemType Directory -Force -Path $LauncherOutput | Out-Null

    # 版本经环境变量注入（msbuild 子进程继承）：含逗号的 Numeric 值不经过任何 shell
    # 命令行层——pwsh 7 与 PS 5.1 对含引号参数的转义规则不同（曾致 CI MSB1008/RC1109），
    # 消灭命令行引号是跨 shell 唯一稳定解。显式 /p: 覆盖仍可用。
    $env:WTGW_LAUNCHER_VER_NUM = $versionNumeric
    $env:WTGW_LAUNCHER_VER_STR = $MainVer

    Invoke-CommandLogged `
        -FilePath $msbuild `
        -Arguments @(
            $LauncherProject,
            "/m", "/nologo", "/v:m",
            "/p:Configuration=Release",
            "/p:Platform=x64",
            "/p:OutDir=$LauncherOutput/",
            "/p:IntDir=$intDir",
            "/p:LauncherVersionString=$MainVer"
        ) `
        -LogName "build-launcher.txt"

    $launcherExe = Join-Path $LauncherOutput "WTGWizard.exe"
    if (-not (Test-Path $launcherExe)) {
        throw "Launcher build did not produce WTGWizard.exe (expected: $launcherExe)."
    }
    Write-Log "Launcher: $launcherExe" "SUCCESS"
}

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
        [string]$PublishProfile
    )

    Write-SubSection "Validate publish output ($PublishProfile)"

    # SCD-* = SelfContained（.NET 运行时随包），由 Profile 命名推得，避免双源不一致
    $isSelfContained = $PublishProfile -like "SCD-*"

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

    if ($isSelfContained) {
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
    # 缺失时下载最新版并用官方 blob SHA512 元数据校验；任何失败返回 $null 由调用方回退。
    $toolDir = $ToolRoot
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
        $nupkgUrl = "$base/7-zip.commandline.$version.nupkg"

        # 完整性校验：flat container 无 .sha512 sidecar；官方源把 SHA512（Base64）
        # 存放在 nupkg blob 的 x-ms-meta-SHA512 元数据里，须 HEAD 获取。本地用
        # SHA512 + ToBase64String 计算（Get-FileHash 返回 Hex，不可直接比较）；
        # 缺失或不匹配由外层 catch 记 WARN 并回退 ZipFile。
        $expectedHash = (Invoke-WebRequest $nupkgUrl -Method Head -UseBasicParsing).Headers["x-ms-meta-SHA512"]

        Invoke-WebRequest $nupkgUrl -OutFile $nupkg -UseBasicParsing
        Write-Log "Downloaded 7-Zip.CommandLine $version (HTTPS NuGet official source)." "SUCCESS"

        $actualHash = [Convert]::ToBase64String(
            [System.Security.Cryptography.SHA512]::Create().ComputeHash(
                [System.IO.File]::ReadAllBytes($nupkg)))
        if ((-not $expectedHash) -or ($actualHash -ne $expectedHash)) {
            throw "7-Zip.CommandLine $version SHA512 mismatch"
        }

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

    $archive = Join-Path $ArchiveRoot "$Name.zip"

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
}

# ============================================================================
# Build variants
# ============================================================================

function Build-Variant {
    param(
        [Parameter(Mandatory)]
        [string]$Output,

        [Parameter(Mandatory)]
        [string]$PublishProfile,

        [Parameter(Mandatory)]
        [string]$ModeLabel
    )

    Write-Section "$ModeLabel Build"

    New-Item -ItemType Directory -Force -Path $Output | Out-Null

    Build-Launcher

    # Worker 先单独 publish 到独立验证目录：确认其 PublishProfile 可用，
    # 同时把 Worker 的 Build 输出（bin\x64\...）留下，供 Main publish 后的 copy target 注入
    Build-Worker `
        -Output $WorkerOutput `
        -PublishProfile $PublishProfile `
        -Version $WorkerVer

    Build-Main `
        -Output $Output `
        -PublishProfile $PublishProfile `
        -Version $MainVer

    Validate-PublishOutput `
        -Directory $Output `
        -PublishProfile $PublishProfile

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

        Write-WTGWizardReport `
            -Directory $Output `
            -Name "$ModeLabel-$Architecture"
    }
}

function Build-FDD {
    Build-Variant `
        -Output (Join-Path $FddOutput $Architecture) `
        -PublishProfile "FDD-x64" `
        -ModeLabel "FDD"
}

function Build-SCD {
    Build-Variant `
        -Output (Join-Path $ScdOutput $Architecture) `
        -PublishProfile "SCD-x64" `
        -ModeLabel "SCD"
}

# ============================================================================
# Package (build phase completed; archiving is centralized here)
# ============================================================================

function Package-Artifacts {
    Write-Section "Package Artifacts"

    $publishDir = if ($BuildType -eq "FDD") { Join-Path $FddOutput $Architecture } else { Join-Path $ScdOutput $Architecture }
    if (-not (Test-Path $publishDir)) {
        throw "Publish output not found: $publishDir"
    }

    $launcherExe = Join-Path $LauncherOutput "WTGWizard.exe"
    if (-not (Test-Path $launcherExe)) {
        throw "Launcher exe not found: $launcherExe (Build-Launcher must run before packaging)."
    }

    # staging：publish 全部内容 → WTGWizard-v{version}\ 子目录；Launcher exe 置 zip 根
    if (Test-Path $StagingRoot) {
        Remove-Item -LiteralPath $StagingRoot -Recurse -Force
    }
    $appDirName = "WTGWizard-v" + $MainVer
    $appDir = Join-Path $StagingRoot $appDirName
    New-Item -ItemType Directory -Force -Path $appDir | Out-Null

    Copy-Item `
        -Path (Join-Path $publishDir "*") `
        -Destination $appDir `
        -Recurse `
        -Force
    Copy-Item -LiteralPath $launcherExe -Destination $StagingRoot -Force

    $appCount = (Get-ChildItem $appDir -Recurse -File).Count
    Write-Log "Staging: $appDirName <- $publishDir ($appCount files)"
    Write-Log "Staging: WTGWizard.exe <- $launcherExe"

    Compress-Artifact `
        -Directory $StagingRoot `
        -Name "WTGWizard-$ZipTag-$Architecture-$BuildType"

    Remove-Item -LiteralPath $StagingRoot -Recurse -Force
}

# ============================================================================
# Main
# ============================================================================

try {
    Initialize-Directories

    Write-Section "WTGWizard Build ($BuildType)"

    Write-Log "Build started."
    Write-Log "Timestamp: $(Get-Date -Format o)"
    Write-Log "Root: $Root"
    Write-Log "OutputDir: $BuildRoot"

    # SDK 解析观测（global.json rollForward=latestMinor）：解析失败在此快速报错，
    # 不带 2>&1 合并（Pitfall 20：native stderr 会抛 RemoteException）
    $dotnetSdkVersion = & dotnet --version
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet SDK resolution failed (global.json). Exit code: $LASTEXITCODE"
    }
    Write-Log "dotnet SDK: $dotnetSdkVersion"

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

    # 单模式构建：发布形态由 PublishProfile（Properties/PublishProfiles/*.pubxml）决定
    if ($BuildType -eq "FDD") { Build-FDD } else { Build-SCD }

    # 打包阶段：构建完成后统一执行
    Package-Artifacts

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
            -LiteralPath $ArchiveRoot `
            -File `
            -Force |
            Where-Object { $_.Name -like "WTGWizard-*.zip" } |
            Sort-Object Name |
            ForEach-Object {
                Write-Log "$($_.Name) [$($_.Length) bytes]"
            }
    }

    Write-Log "WTGWizard build finished successfully." "SUCCESS"
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
