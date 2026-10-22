param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$ApplicationVersion = '1.0.1'
)

# Builds the Claude widget portable EXE, Inno Setup installer and SHA256SUMS.txt.
# Uses separate artifacts\publish\claude-win-x64 and dist\claude folders so Codex outputs are untouched.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'ClaudeAccountWidget\ClaudeAccountWidget.csproj'
$publishDir = Join-Path $projectRoot 'artifacts\publish\claude-win-x64'
$distDir = Join-Path $projectRoot 'dist\claude'
$portablePath = Join-Path $distDir 'ClaudeAccountSwitcher.exe'
$installerPath = Join-Path $distDir 'ClaudeAccountSwitcher-Setup.exe'
$checksumPath = Join-Path $distDir 'SHA256SUMS.txt'

$innoCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$innoCompiler = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $innoCompiler) {
    throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php.'
}

New-Item -ItemType Directory -Force -Path $publishDir, $distDir | Out-Null
Remove-Item -LiteralPath $publishDir -Recurse -Force
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet'
dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$ApplicationVersion `
    --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$publishedExecutable = Join-Path $publishDir 'ClaudeAccountSwitcher.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "Published executable was not found: $publishedExecutable"
}
Copy-Item -LiteralPath $publishedExecutable -Destination $portablePath -Force

$issPath = Join-Path $projectRoot 'installer\ClaudeAccountSwitcher.iss'
& $innoCompiler "/DPublishDir=$publishDir" "/DOutputDir=$distDir" "/DApplicationVersion=$ApplicationVersion" $issPath
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed: $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $installerPath)) { throw "Installer was not found: $installerPath" }

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally { $sha256.Dispose() }
    }
    finally { $stream.Dispose() }
}

$hashLines = foreach ($path in @($portablePath, $installerPath)) {
    $hash = Get-Sha256 $path
    "$hash  $([IO.Path]::GetFileName($path))"
}
[IO.File]::WriteAllLines($checksumPath, $hashLines, [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host 'Claude release artifacts created:'
Get-Item $portablePath, $installerPath, $checksumPath | Select-Object FullName, Length, LastWriteTime | Format-Table -AutoSize
