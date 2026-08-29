@echo off
setlocal
cd /d "%~dp0"
echo ==============================================
echo   BrennivinProtection 0.1.3 - Local Build
echo ==============================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo BUILD FAILED - copy the error text and send it to ChatGPT.
) else (
  echo BUILD SUCCEEDED - BrennivinProtection 0.1.3 was built and installed locally.
)
echo.
pause
exit /b %ERR%
