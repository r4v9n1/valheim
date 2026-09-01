@echo off
setlocal EnableExtensions
title PhysicalWater E3 Geometry Handshake Repair V2

echo.
echo ============================================================
echo   PhysicalWater E3 Geometry Handshake Repair V2
echo ============================================================
echo.
echo This runner will:
echo   1. Back up the current E3 source files.
echo   2. Patch the causal coverage ^> geometry-ready handoff.
echo   3. Bump the repair candidate to devE3.1 / assembly 0.6.0.15.
echo   4. Run an offline coverage reachability proof.
echo   5. Build PhysicalWater.
echo   6. Run the targeted Unity E3 validator when available.
echo   7. Offer to install the candidate only after offline checks pass.
echo.
echo It does NOT alter E1 solver math, E2 presentation math,
echo density control, Heightmap physics, vanilla-water settings,
echo swimming, ships, fish, or global water integration.
echo.
pause

where powershell.exe >nul 2>nul
if errorlevel 1 (
    echo ERROR: powershell.exe was not found.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0FIX-E3-GEOMETRY-HANDSHAKE-v2.ps1"
set ERR=%ERRORLEVEL%

echo.
if not "%ERR%"=="0" (
    echo ============================================================
    echo   REPAIR RUN FAILED - exit code %ERR%
    echo ============================================================
    echo Read the report path printed above.
) else (
    echo ============================================================
    echo   OFFLINE REPAIR RUN COMPLETED
    echo ============================================================
)
echo.
pause
exit /b %ERR%
