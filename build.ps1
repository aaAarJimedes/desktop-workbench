param([switch]$Test, [switch]$SkipDeploy)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $build | Out-Null
$marker = Join-Path $build 'tested-build.json'
if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll','/r:Microsoft.CSharp.dll')
& $compiler /nologo /target:exe /out:"$build\IconBuilder.exe" /r:System.Drawing.dll "$root\IconBuilder.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon compiler failed' }
& "$build\IconBuilder.exe" "$root\assets"
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed' }
& $compiler /nologo /target:library /out:"$build\Workbench.Contracts.dll" @refs "$root\Contracts.cs"
if ($LASTEXITCODE -ne 0) { throw 'Contract compilation failed' }
$src = @("$root\NativeProcess.cs","$root\DataLocation.cs","$root\Network.cs","$root\Monitor.cs","$root\CampusTool.cs","$root\Tunnels.cs","$root\TunnelTool.cs","$root\Runtime.cs","$root\Program.cs","$root\AssemblyInfo.cs")
& $compiler /nologo /target:winexe /out:"$build\Workbench.exe" /win32icon:"$root\assets\workbench.ico" "/resource:$root\assets\workbench.ico,Workbench.Icon.ico" /r:"$build\Workbench.Contracts.dll" @refs @src
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed' }
if ($Test) {
    & $compiler /nologo /target:exe /out:"$build\Tests.exe" @refs "$root\DataLocation.cs" "$root\Network.cs" "$root\Monitor.cs" "$root\Tests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & "$build\Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    & $compiler /nologo /target:exe /out:"$build\TunnelTests.exe" @refs "$root\DataLocation.cs" "$root\Network.cs" "$root\Monitor.cs" "$root\Tunnels.cs" "$root\TunnelTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Tunnel test compilation failed' }
    & "$build\TunnelTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tunnel tests failed' }
    & $compiler /nologo /target:exe /out:"$build\ResidentTests.exe" @refs "$root\DataLocation.cs" "$root\Runtime.cs" "$root\ResidentTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Residency test compilation failed' }
    & "$build\ResidentTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Residency tests failed' }
    & $compiler /nologo /target:exe /out:"$build\ToolViewTests.exe" /r:"$build\Workbench.Contracts.dll" @refs "$root\DataLocation.cs" "$root\Network.cs" "$root\Monitor.cs" "$root\Tunnels.cs" "$root\TunnelTool.cs" "$root\Runtime.cs" "$root\ToolViewTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Saved tunnel view test compilation failed' }
    & "$build\ToolViewTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Saved tunnel view tests failed' }
    $tested = [ordered]@{}
    foreach ($name in @('Workbench.exe','Workbench.Contracts.dll')) { $tested[$name] = (Get-FileHash -LiteralPath (Join-Path $build $name)).Hash }
    [IO.File]::WriteAllText($marker, ($tested | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    if (!$SkipDeploy -and (Test-Path -LiteralPath (Join-Path $root 'deploy.local.json'))) { & "$root\deploy.ps1" }
}
Write-Output "Built: $build\Workbench.exe"
