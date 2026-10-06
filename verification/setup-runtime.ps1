$ErrorActionPreference = 'Stop'
$gameDir = 'E:\SteamLibrary\steamapps\common\Apocalypter'
$verifyDir = Split-Path -Parent $PSCommandPath
$runtimeDir = Join-Path $verifyDir 'runtime'
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
foreach ($folder in @('Apocalypter_Data', 'MonoBleedingEdge')) {
    $linkPath = Join-Path $runtimeDir $folder
    if (-not (Test-Path -LiteralPath $linkPath)) { New-Item -ItemType Junction -Path $linkPath -Target (Join-Path $gameDir $folder) | Out-Null }
}
foreach ($file in @('Apocalypter.exe', 'UnityPlayer.dll', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version')) {
    Copy-Item -LiteralPath (Join-Path $gameDir $file) -Destination $runtimeDir -Force
}
$bepDir = Join-Path $runtimeDir 'BepInEx'
New-Item -ItemType Directory -Path $bepDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $gameDir 'BepInEx\core') -Destination $bepDir -Recurse -Force
$pluginsDir = Join-Path $bepDir 'plugins'
New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $gameDir 'BepInEx\plugins\Apocasetter') -Destination $pluginsDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $gameDir 'BepInEx\plugins\Gunplay') -Destination $pluginsDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $verifyDir '..\bin\Release\Apocaspawner.dll') -Destination $pluginsDir -Force
Copy-Item -LiteralPath (Join-Path $verifyDir 'bin\Release\ApocaspawnerNativeVerifier.dll') -Destination $pluginsDir -Force
$outputDir = Join-Path $verifyDir 'output'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$process = Start-Process -FilePath (Join-Path $runtimeDir 'Apocalypter.exe') -WorkingDirectory $runtimeDir -WindowStyle Hidden -ArgumentList @('-screen-fullscreen','0','-screen-width','1440','-screen-height','960',"-spawner-output=$outputDir",'-logFile',(Join-Path $outputDir 'unity.log')) -PassThru
Write-Output "Verifier PID=$($process.Id)"
