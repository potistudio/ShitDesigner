@echo off
setlocal
set "CODEC=%~1"
if "%CODEC%"=="" set "CODEC=h264"
set "API=%~2"
if "%API%"=="" set "API=d3d12"
set "PLATFORM=%~3"
set "BUILD_SWITCH="
if /I "%PLATFORM%"=="build" (
  set "PLATFORM=windows"
  set "BUILD_SWITCH=-Build"
) else (
  if "%PLATFORM%"=="" set "PLATFORM=windows"
  if /I "%~4"=="build" set "BUILD_SWITCH=-Build"
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0RunStandaloneHarness.ps1" -Codec "%CODEC%" -GraphicsApi "%API%" -Platform "%PLATFORM%" %BUILD_SWITCH%
exit /b %ERRORLEVEL%
