@echo off
setlocal
cd /d "%~dp0"
echo ==============================================
echo   LightMyFire 0.5.4 - Silent Autofill Release Build
echo ==============================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-and-install-test.ps1"
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo BUILD FAILED - copy the error text and send it to ChatGPT.
) else (
  echo BUILD SUCCEEDED - LightMyFire 0.5.4 was built fresh and installed locally.
  echo Thunderstore ZIP: artifacts\R4V9N1-LightMyFire_Coal_Resin-0.5.4.zip
  echo Install the SAME 0.5.4 build on the server before connecting.
)
echo.
pause
exit /b %ERR%
