param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$desktopRoot = Split-Path $PSScriptRoot -Parent
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) { throw 'Use a new destination directory.' }
New-Item -ItemType Directory -Path $destinationPath | Out-Null
$relativeFiles = @('.gitignore', 'README.md', 'woker/woker.slnx', 'woker/woker/woker.csproj',
    'woker/woker/nuget.config', 'woker/woker/app.manifest', 'woker/woker/Package.appxmanifest')
foreach ($folder in @('packaging', 'tests', '.github', 'woker/woker/Assets', 'woker/woker/Models',
    'woker/woker/Services', 'woker/woker/Views', 'woker/woker/Properties')) {
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $desktopRoot $folder) -Recurse -File)) {
        $relative = $file.FullName.Substring($desktopRoot.Length + 1)
        if ($relative -match '(^|[\\/])(bin|obj)([\\/])' -or $file.Extension -in '.user','.pfx','.key','.pdb') { continue }
        if ($file.Extension -notin '.cs','.csproj','.xaml','.ps1','.iss','.json','.yml','.pubxml','.png','.ico') { continue }
        $relativeFiles += $relative
    }
}
$relativeFiles += @('woker/woker/App.xaml', 'woker/woker/App.xaml.cs', 'woker/woker/MainWindow.xaml', 'woker/woker/MainWindow.xaml.cs')
foreach ($relative in ($relativeFiles | Select-Object -Unique)) {
    $target = Join-Path $destinationPath $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $desktopRoot $relative) -Destination $target
}
Write-Host "Desktop source exported: $destinationPath. Review before git add/commit. No repository was created or pushed."
