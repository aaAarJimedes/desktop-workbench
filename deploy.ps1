param([string]$TargetPath, [string]$DeliveryPath, [switch]$Register)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
function Start-Resident([string]$exe) {
    # The task service owns the launcher lifetime, not the development terminal's job.
    $taskName = 'DesktopWorkbench.Launch'
    $prior = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($prior -and @($prior.Actions | Where-Object {$_.Execute -notin $allowed}).Count) { throw 'Launch task belongs to another installation.' }
    $action = New-ScheduledTaskAction -Execute $exe -Argument '--native-context --tray' -WorkingDirectory (Split-Path -Parent $exe)
    $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value) -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Description 'Launch Desktop Workbench independently of the development terminal; on demand only.' -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $worker = @(Get-CimInstance Win32_Process -Filter "Name='Workbench.exe'" | Where-Object {$_.ExecutablePath -eq $exe -and $_.CommandLine -match '--worker'})
        if ($worker.Count -eq 1) { return }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The daily application did not start; inspect DesktopWorkbench.Launch and runtime.log.'
}
$configPath = Join-Path $root 'deploy.local.json'
if (!$TargetPath) {
    if (!(Test-Path -LiteralPath $configPath)) { throw 'Specify -TargetPath and -Register for initial setup.' }
    $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
    $TargetPath = $config.TargetPath
    $DeliveryPath = $config.DeliveryPath
}
$TargetPath = [IO.Path]::GetFullPath($TargetPath).TrimEnd('\')
$targets = @($TargetPath)
if ($DeliveryPath) { $DeliveryPath = [IO.Path]::GetFullPath($DeliveryPath).TrimEnd('\'); $targets += $DeliveryPath }
foreach ($target in $targets) {
    if ($target.Length -lt 8 -or $target -eq $root -or $root.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe deployment target.' }
    for ($parent=$target; $parent; $parent=Split-Path -Parent $parent) {
        if ((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked deployment paths are unsupported.' }
    }
}
$markerPath = Join-Path $root 'build\tested-build.json'
if (!(Test-Path -LiteralPath $markerPath)) { throw 'Run build.ps1 -Test before deployment.' }
$marker = Get-Content -Raw -LiteralPath $markerPath | ConvertFrom-Json
foreach ($name in @('Workbench.exe','Workbench.Contracts.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path "$root\build" $name)).Hash -ne $marker.$name) { throw 'Binary changed since tests. Run build.ps1 -Test again.' }
}
$files = @(@('build\Workbench.exe','Workbench.exe'), @('build\Workbench.Contracts.dll','Workbench.Contracts.dll'), @('README.md','README.md'), @('assets\workbench.png','assets\workbench.png'))
$allowed = @((Join-Path $root 'build\Workbench.exe')) + @($targets | ForEach-Object {Join-Path $_ 'Workbench.exe'})
$running = @(Get-CimInstance Win32_Process -Filter "Name='Workbench.exe'" | Where-Object {$_.SessionId -eq (Get-Process -Id $PID).SessionId})
if (@($running | Where-Object {$_.ExecutablePath -notin $allowed}).Count) { throw 'Another Workbench installation is running; stop it before deploying.' }
$restart = $Register -or $running.Count -gt 0
$oldExe = if($running.Count) {$running[0].ExecutablePath} else {$null}
if ($oldExe) {
    $quitter = Start-Process -FilePath $oldExe -ArgumentList '--quit' -WindowStyle Hidden -PassThru
    if (!$quitter.WaitForExit(10000)) { throw 'Stop command timed out; no files replaced.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do { $live = @($running | Where-Object {Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue}); if (!$live.Count) {break}; Start-Sleep -Milliseconds 250 } while ([DateTime]::UtcNow -lt $deadline)
    if ($live.Count) { throw 'Workbench is still running; no files replaced.' }
}
$dataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DesktopWorkbench'
# Migration must run outside the packaged development host's filesystem virtualization.
$migrator = Start-Process -FilePath (Join-Path "$root\build" 'Workbench.exe') -ArgumentList '--migrate-data' -WindowStyle Hidden -PassThru
if (!$migrator.WaitForExit(45000) -or $migrator.ExitCode -ne 0) { throw 'Native data migration failed; no program files replaced.' }
$dataHashes = @{}
foreach ($name in @('credential.dat','settings.json','tunnels.json','optimization.json','organizer.json')) { $p=Join-Path $dataRoot $name; if(Test-Path -LiteralPath $p){$dataHashes[$name]=(Get-FileHash -LiteralPath $p).Hash} }
$backup = Join-Path "$root\build" ('deploy-backup-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup | Out-Null
$written = @()
$succeeded = $false
try {
    for ($i=0; $i -lt $targets.Count; $i++) {
        $target=$targets[$i]
        foreach ($entry in $files) {
            $from=Join-Path $root $entry[0]; $to=Join-Path $target $entry[1]
            if ((Test-Path -LiteralPath $to) -and ((Get-Item -LiteralPath $to).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw 'Linked target file is unsupported.'}
            $save=Join-Path $backup ($i.ToString()+'-'+[IO.Path]::GetFileName($to))
            $existed=Test-Path -LiteralPath $to
            if($existed){Copy-Item -LiteralPath $to -Destination $save}
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $to) | Out-Null
            $written += [pscustomobject]@{Path=$to;Backup=$save;Existed=$existed}
            Copy-Item -LiteralPath $from -Destination $to -Force
            if((Get-FileHash -LiteralPath $from).Hash -ne (Get-FileHash -LiteralPath $to).Hash){throw 'Deployed file hash mismatch.'}
        }
        New-Item -ItemType Directory -Force -Path (Join-Path $target 'plugins') | Out-Null
        $sums=$files | ForEach-Object {(Get-FileHash -LiteralPath (Join-Path $target $_[1])).Hash.ToLowerInvariant()+'  '+$_[1].Replace('\','/')}
        [IO.File]::WriteAllText((Join-Path $target 'SHA256SUMS.txt'),($sums -join "`n")+"`n",(New-Object Text.UTF8Encoding($false)))
    }
    foreach($name in $dataHashes.Keys){if((Get-FileHash -LiteralPath (Join-Path $dataRoot $name)).Hash -ne $dataHashes[$name]){throw 'User data changed during deployment.'}}
    $dailyExe=Join-Path $TargetPath 'Workbench.exe'
    $shell=New-Object -ComObject WScript.Shell
    $shortcutPath=Join-Path ([Environment]::GetFolderPath('Desktop')) '工具工作台.lnk'
    $shortcut=$shell.CreateShortcut($shortcutPath)
    if((Test-Path -LiteralPath $shortcutPath) -and $shortcut.TargetPath -notin $allowed){throw 'Desktop shortcut belongs to another installation.'}
    $shortcut.TargetPath=$dailyExe; $shortcut.WorkingDirectory=$TargetPath; $shortcut.IconLocation=$dailyExe+',0'; $shortcut.Description='工具工作台'; $shortcut.Save()
    $startup='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if(Get-ItemProperty -LiteralPath $startup -Name DesktopWorkbench -ErrorAction SilentlyContinue){Set-ItemProperty -LiteralPath $startup -Name DesktopWorkbench -Value ('"'+$dailyExe+'" --tray')}
    if($Register){[IO.File]::WriteAllText($configPath,([ordered]@{TargetPath=$TargetPath;DeliveryPath=$DeliveryPath}|ConvertTo-Json)+"`n",(New-Object Text.UTF8Encoding($false)))}
    $succeeded=$true
} catch {
    foreach($item in $written){if($item.Existed){Copy-Item -LiteralPath $item.Backup -Destination $item.Path -Force}else{Remove-Item -LiteralPath $item.Path -ErrorAction SilentlyContinue}}
    if($oldExe){Start-Resident $oldExe}
    throw
} finally {
    if($succeeded){Get-ChildItem -LiteralPath $backup -File | ForEach-Object {Remove-Item -LiteralPath $_.FullName}; Remove-Item -LiteralPath $backup}
}
if($restart){Start-Resident (Join-Path $TargetPath 'Workbench.exe')}
Write-Output "Deployed tested build to $TargetPath; user data preserved; desktop shortcut updated."
