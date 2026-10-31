@echo off
setlocal EnableExtensions
title BlackTheo build

cd /d "%~dp0"

set "MODE=build"
set "FLAG="

if "%~1"=="" goto args_done
if /I "%~1"=="help" goto help
if /I "%~1"=="build" goto args_done
if /I "%~1"=="deploy" goto mode_deploy
if /I "%~1"=="hotreload" goto mode_hotreload

rem First argument is a Celeste path.
set "CELESTE_DIR=%~1"
goto args_done

:mode_deploy
set "MODE=deploy"
if not "%~2"=="" set "CELESTE_DIR=%~2"
goto args_done

:mode_hotreload
set "MODE=hotreload"
if not "%~2"=="" set "CELESTE_DIR=%~2"
goto args_done

:args_done
if /I "%MODE%"=="deploy" set "FLAG=-Deploy"
if /I "%MODE%"=="hotreload" set "FLAG=-HotReload"

echo ========================================
echo  BlackTheo build
echo  Mode: %MODE%
if defined CELESTE_DIR echo  Celeste: %CELESTE_DIR%
echo ========================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -CelestePath "%CELESTE_DIR%" %FLAG%
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Done.
if /I "%MODE%"=="hotreload" (
    echo Press F5 in Celeste to hot reload the level.
)
pause
exit /b 0

:help
echo Usage:
echo   build.bat                 Build the mod package.
echo   build.bat deploy          Build and deploy to Celeste/Mods.
echo   build.bat hotreload       Build and copy only BlackTheo.dll for F5 hot reload.
echo   build.bat ^<CelestePath^>   Build using the given Celeste folder.
echo   build.bat deploy ^<Path^>  Deploy using the given Celeste folder.
echo.
echo You can also set CELESTE_DIR before running.
exit /b 0
