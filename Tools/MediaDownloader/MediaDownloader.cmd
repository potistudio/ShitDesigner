@echo off
where pyw.exe >nul 2>nul
if not errorlevel 1 (
	start "" pyw.exe "%~dp0MediaDownloader.py"
	exit /b
)

where pythonw.exe >nul 2>nul
if not errorlevel 1 (
	start "" pythonw.exe "%~dp0MediaDownloader.py"
	exit /b
)

echo Python 3 is required. Install Python, then run MediaDownloader.py.
pause
