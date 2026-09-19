@echo off
cd /d "%~dp0"
if exist "OMG AD.exe" (
  start "" "OMG AD.exe"
  exit /b
)
echo Missing "OMG AD.exe". Rebuild the standalone release.
pause
exit /b 1
