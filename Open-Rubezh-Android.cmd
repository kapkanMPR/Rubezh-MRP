@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"
if not exist "ProjectSettings\ProjectVersion.txt" goto wrong_folder
set "version="
for /f "tokens=2" %%V in ('findstr /b "m_EditorVersion:" ProjectSettings\ProjectVersion.txt') do set "version=%%V"
if not defined version goto wrong_folder
set "editor=%ProgramFiles%\Unity\Hub\Editor\%version%\Editor\Unity.exe"
if exist "%editor%" goto launch
echo Select Unity.exe for Unity %version%.
for /f "usebackq delims=" %%E in (`powershell -NoProfile -STA -Command "Add-Type -AssemblyName System.Windows.Forms; $d=New-Object System.Windows.Forms.OpenFileDialog; $d.Filter='Unity Editor (Unity.exe)|Unity.exe'; if($d.ShowDialog() -eq 'OK'){$d.FileName}"`) do set "editor=%%E"
if not exist "%editor%" exit /b 1
:launch
for %%E in ("%editor%") do if not exist "%%~dpEData\PlaybackEngines\AndroidPlayer\" goto missing_module
start "" "%editor%" -projectPath "%CD%" -buildTarget Android
exit /b
:wrong_folder
echo Place this file next to the Assets and ProjectSettings folders.
pause
exit /b 1
:missing_module
echo Install Android Build Support for Unity %version% in Unity Hub.
pause
exit /b 1
