param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_][A-Za-z0-9_.-]*$')][string]$Repository,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$ReleaseDirectory = ''
)
$ErrorActionPreference = 'Stop'
if (!$ReleaseDirectory) { $ReleaseDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/$Version" }
$installer = Join-Path $ReleaseDirectory "WorkBench-$Version-win-x64-Setup.exe"
$manifest = Join-Path $ReleaseDirectory 'update-win-x64.json'
$checksums = Join-Path $ReleaseDirectory 'SHA256SUMS'
foreach ($file in @($installer, $manifest, $checksums)) {
    if (!(Test-Path -LiteralPath $file)) { throw "Missing release file: $file" }
}
$data = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if ($data.version -ne $Version -or $data.installer -ne (Split-Path $installer -Leaf) -or
    $data.sha256 -ne (Get-FileHash $installer -Algorithm SHA256).Hash -or $data.size -ne (Get-Item $installer).Length) {
    throw 'Installer does not match update manifest.'
}
& gh release create "v$Version" $installer $manifest $checksums --repo $Repository --draft --title "WorkBench $Version" --notes "WorkBench desktop $Version (Windows x64)."
if ($LASTEXITCODE -ne 0) { throw 'GitHub draft release creation failed.' }
Write-Host 'Draft created. Add release notes and publish it on GitHub when ready. Clients ignore drafts.'
