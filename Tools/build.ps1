param([ValidateSet('fast','release','developer','check')][string]$Mode='fast',[string]$UnityPath=$env:UNITY_EDITOR)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if($Mode -eq 'check') {
 & python "$project/Tools/ProjectCheck/run.py"
 exit $LASTEXITCODE
}
if(-not $UnityPath){$UnityPath="$env:ProgramFiles/Unity/Hub/Editor/6000.3.16f1/Editor/Unity.exe"}
if(-not (Test-Path $UnityPath)){throw 'Unity not found. Set UNITY_EDITOR to the full Unity.exe path, or pass -UnityPath.'}
if(Test-Path "$project/Temp/UnityLockfile"){throw 'Project is open in Unity. Use the Rubezh build menu in that editor, or close it before running this command.'}
New-Item -ItemType Directory -Force "$project/Logs" | Out-Null
$method=if($Mode -eq 'developer'){'AndroidDeveloper'}elseif($Mode -eq 'release'){'AndroidRelease'}else{'AndroidFast'}
$argsList=@('-batchmode','-nographics','-quit','-projectPath',('"'+$project+'"'),'-buildTarget','Android','-executeMethod',"Rubezh.UnityPort.Editor.FastBuild.$method",'-logFile',('"'+$project+'/Logs/build.log"'))
$proc=Start-Process -FilePath $UnityPath -ArgumentList $argsList -PassThru -Wait
if($proc.ExitCode -ne 0){Get-Content "$project/Logs/build.log" -Tail 70;exit $proc.ExitCode}
$flavor=if($Mode -eq 'release'){'Release'}else{'Dev'}
$apk=(Get-Content "$project/Validation/last-apk-path.txt" -Raw).Trim()
if(-not(Test-Path $apk)){throw 'Unity returned without creating the APK. Inspect Logs/build.log.'}
Write-Host "APK: $apk"
