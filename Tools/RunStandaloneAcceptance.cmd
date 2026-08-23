@echo off
setlocal
set "API=%~1"
if "%API%"=="" set "API=d3d12"
set "FIXTURES=%~2"
if "%FIXTURES%"=="" set "FIXTURES=Assets\ShitDesigner\Scripts\Tests\Media\Fixtures"
set "BUILD_SWITCH="
if /I "%~3"=="build" set "BUILD_SWITCH=-Build"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0RunStandaloneAcceptance.ps1" -GraphicsApi "%API%" -Platform windows -FixtureRoot "%FIXTURES%" %BUILD_SWITCH%
exit /b %ERRORLEVEL%
