param([string]$Repository = 'aaAarJimedes/desktop-workbench')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'build\Workbench.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Run build.ps1 -Test first.' }
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository name' }
$version = ([Version](Get-Item -LiteralPath $exe).VersionInfo.FileVersion).ToString(3)
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$zipName = "desktop-workbench-$version-win-x64.zip"
$zip = Join-Path $dist $zipName
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entry in @(@('build\Workbench.exe','Workbench.exe'), @('build\Workbench.Contracts.dll','Workbench.Contracts.dll'), @('README.md','README.md'), @('assets\workbench.png','assets/workbench.png'))) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root $entry[0]), $entry[1], [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{version=$version;tag="v$version";url="https://github.com/$Repository/releases/download/v$version/$zipName";sha256=$hash;size=(Get-Item $zip).Length}
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $dist 'update.json'), ($manifest | ConvertTo-Json) + "`n", $utf8)
$sums = @($zipName,'update.json') | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $dist $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ }
[IO.File]::WriteAllText((Join-Path $dist "SHA256SUMS-$version.txt"), ($sums -join "`n")+"`n", $utf8)
Write-Output "Packaged $zip"
