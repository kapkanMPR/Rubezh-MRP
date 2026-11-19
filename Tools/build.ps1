param(
    [ValidateSet('fast', 'release', 'developer', 'check')]
    [string]$Mode = 'fast',
    [string]$UnityPath = $env:UNITY_EDITOR
)

$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent

if ($Mode -eq 'check') {
    & python "$project/Tools/ProjectCheck/run.py"
    exit $LASTEXITCODE
}
if (-not $UnityPath) {
    $UnityPath = "$env:ProgramFiles/Unity/Hub/Editor/6000.3.16f1/Editor/Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw 'Unity not found. Set UNITY_EDITOR to the full Unity.exe path, or pass -UnityPath.'
}
if (Test-Path -LiteralPath "$project/Temp/UnityLockfile") {
    throw 'Project is open. Use its Rubezh build menu or close the Editor.'
}

$method = switch ($Mode) {
    developer { 'AndroidDeveloper' }
    release { 'AndroidRelease' }
    fast { 'AndroidFast' }
}
$log = "$project/Logs/build.log"
New-Item -ItemType Directory -Force "$project/Logs" | Out-Null
[System.IO.File]::WriteAllText($log, '')
$arguments = @(
    '-batchmode', '-nographics', '-quit',
    '-projectPath', ('"{0}"' -f $project),
    '-buildTarget', 'Android',
    '-executeMethod', "Rubezh.UnityPort.Editor.FastBuild.$method",
    '-logFile', ('"{0}"' -f $log)
)
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -Wait
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath $log -Tail 70
    exit $process.ExitCode
}

$marker = 'RUBEZH APK: '
$result = Select-String -LiteralPath $log -Pattern '^RUBEZH APK: ' -Encoding UTF8 |
    Select-Object -Last 1
if (-not $result) {
    throw "Unity finished without reporting an APK. See $log."
}
$apk = $result.Line.Substring($marker.Length)
if (-not (Test-Path -LiteralPath $apk -PathType Leaf)) {
    throw "Unity reported a missing APK. See $log."
}
Write-Host "APK: $apk"
