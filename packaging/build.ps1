param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '2.1.0',
    [string]$Repository = '',
    [string]$IsccPath = '',
    [string]$OutputRoot = '',
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$desktopRoot = Split-Path $PSScriptRoot -Parent
if ($Repository -and $Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_][A-Za-z0-9_.-]*$') {
    throw 'Repository must be owner/repo (public GitHub release repository).'
}
if (!$OutputRoot) { $OutputRoot = Join-Path $desktopRoot 'artifacts' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$releaseDir = Join-Path $OutputRoot $Version
$publishDir = Join-Path $releaseDir 'app'
if (!$SkipPublish -and (Test-Path $releaseDir)) { throw "Output exists: $releaseDir. Use a new output directory or version to avoid mixing releases." }
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
$project = Join-Path $desktopRoot 'woker/woker/woker.csproj'
if (!$SkipPublish) {
    & dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true `
        -p:EnableMsixTooling=false -p:AppxPackage=false -p:AppxBundle=Never `
        -p:PublishSingleFile=false -p:PublishReadyToRun=false `
        "-p:Version=$Version" "-p:AssemblyVersion=$Version.0" "-p:FileVersion=$Version.0" -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}
if (!(Test-Path (Join-Path $publishDir 'woker.exe'))) { throw 'Published woker.exe is missing.' }
foreach ($resource in @('App.xbf','MainWindow.xbf','Views/LoginPage.xbf','Views/SettingsPage.xbf','Assets/Square44x44Logo.scale-200.png')) {
    if (!(Test-Path (Join-Path $publishDir $resource))) { throw "Required WinUI resource missing from publish output: $resource" }
}
$builtVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishDir 'woker.dll')).FileVersion
if ($builtVersion -ne "$Version.0") { throw "Published assembly version mismatch: $builtVersion" }
# An explicit allowlist is used by the project; do not ship local settings, keys or debug files.
$privateFiles = Get-ChildItem -LiteralPath $publishDir -Recurse -File | Where-Object {
    $_.Name -match '^(server\.config\.json|updates\.json|\.env.*)$' -or $_.Extension -in '.pfx','.pem','.key','.pdb','.user'
}
if ($privateFiles) { throw 'Unexpected private/debug files in publish output; review before packaging.' }
[IO.File]::WriteAllText((Join-Path $publishDir 'update-source.json'),
    (@{Repository=$Repository; CheckOnStartup=$true} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
if (!$IsccPath) {
    $candidates = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (!$IsccPath -or !(Test-Path $IsccPath)) { throw 'Install Inno Setup 6 or supply -IsccPath.' }
& $IsccPath "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$releaseDir" (Join-Path $PSScriptRoot 'WorkBench.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$installerName = "WorkBench-$Version-win-x64-Setup.exe"
$installer = Join-Path $releaseDir $installerName
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = @{version=$Version; architecture='x64'; installer=$installerName; sha256=$hash; size=(Get-Item $installer).Length}
[IO.File]::WriteAllText((Join-Path $releaseDir 'update-win-x64.json'), ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $releaseDir 'SHA256SUMS'), "$hash  $installerName`n", [Text.UTF8Encoding]::new($false))
Write-Host "Release files ready: $releaseDir"
if (!$Repository) { Write-Warning 'No GitHub repository configured. Configure it in the app Settings or rebuild with -Repository before distribution.' }
