param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $build | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll')
& $compiler /nologo /target:exe /out:"$build\IconBuilder.exe" /r:System.Drawing.dll "$root\IconBuilder.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon compiler failed' }
& "$build\IconBuilder.exe" "$root\assets"
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed' }
& $compiler /nologo /target:library /out:"$build\Workbench.Contracts.dll" @refs "$root\Contracts.cs"
if ($LASTEXITCODE -ne 0) { throw 'Contract compilation failed' }
$src = @("$root\Network.cs","$root\Monitor.cs","$root\CampusTool.cs","$root\Tunnels.cs","$root\TunnelTool.cs","$root\Runtime.cs","$root\Program.cs","$root\AssemblyInfo.cs")
& $compiler /nologo /target:winexe /out:"$build\Workbench.exe" /win32icon:"$root\assets\workbench.ico" "/resource:$root\assets\workbench.ico,Workbench.Icon.ico" /r:"$build\Workbench.Contracts.dll" @refs @src
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed' }
if ($Test) {
    & $compiler /nologo /target:exe /out:"$build\Tests.exe" @refs "$root\Network.cs" "$root\Monitor.cs" "$root\Tests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & "$build\Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    & $compiler /nologo /target:exe /out:"$build\TunnelTests.exe" @refs "$root\Network.cs" "$root\Monitor.cs" "$root\Tunnels.cs" "$root\TunnelTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Tunnel test compilation failed' }
    & "$build\TunnelTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tunnel tests failed' }
    & $compiler /nologo /target:exe /out:"$build\ResidentTests.exe" @refs "$root\Runtime.cs" "$root\ResidentTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Residency test compilation failed' }
    & "$build\ResidentTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Residency tests failed' }
}
Write-Output "Built: $build\Workbench.exe"
