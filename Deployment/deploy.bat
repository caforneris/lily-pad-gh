@echo off
REM ═══════════════════════════════════════════════════════════════════════════
REM  LilyPadGH - Master Deployment Script
REM  Compatible with both VS Code and Visual Studio Professional
REM ═══════════════════════════════════════════════════════════════════════════

REM Parameters passed from MSBuild
set TARGET_DIR=%~1
set PROJECT_DIR=%~2
set CONFIG=%~3
set RHINO_VERSION=%~4

REM If no RHINO_VERSION provided, try to detect from configuration name
if "%RHINO_VERSION%"=="" (
    echo Detecting Rhino version from configuration: %CONFIG%
    
    if /I "%CONFIG%"=="Deploy Rhino 8" (
        set RHINO_VERSION=8
    ) else if /I "%CONFIG%"=="Deploy Rhino 7" (
        set RHINO_VERSION=7
    ) else (
        REM Default to Both for Debug/Release
        set RHINO_VERSION=Both
    )
)

echo ═══════════════════════════════════════════════════════════════════════════
echo Configuration: %CONFIG%
echo Target Rhino: %RHINO_VERSION%
echo Source: %TARGET_DIR%
echo ═══════════════════════════════════════════════════════════════════════════

REM Track if we should delete at the end (only delete after all deployments)
set SHOULD_DELETE=1

REM Deploy to Rhino 8
if "%RHINO_VERSION%"=="8" goto :deploy8
if "%RHINO_VERSION%"=="Both" goto :deploy8

:skipRhino8
REM Deploy to Rhino 7
if "%RHINO_VERSION%"=="7" goto :deploy7
if "%RHINO_VERSION%"=="Both" goto :deploy7

:cleanup
REM Only delete the GHA file after ALL deployments are complete
if "%SHOULD_DELETE%"=="1" (
    echo Cleaning up build output...
    del /Q "%TARGET_DIR%LilyPadGH.gha" 2>nul
)

:done
echo ═══════════════════════════════════════════════════════════════════════════
echo Deployment completed successfully
exit /b 0

REM ───────────────────────────────────────────────────────────────────────────
:deploy8
echo.
echo Deploying to Rhino 8...
REM Pass the RHINO_VERSION to let script know if it's part of Both deployment
call "%PROJECT_DIR%Deployment\deploy-rhino8.bat" "%TARGET_DIR%" "%PROJECT_DIR%" "%CONFIG%" "%RHINO_VERSION%"
if errorlevel 1 (
    echo ERROR: Rhino 8 deployment failed
    exit /b 1
)

if "%RHINO_VERSION%"=="8" goto :cleanup
goto :skipRhino8

REM ───────────────────────────────────────────────────────────────────────────
:deploy7
echo.
echo Deploying to Rhino 7...
REM Pass the RHINO_VERSION to let script know if it's part of Both deployment
call "%PROJECT_DIR%Deployment\deploy-rhino7.bat" "%TARGET_DIR%" "%PROJECT_DIR%" "%CONFIG%" "%RHINO_VERSION%"
if errorlevel 1 (
    echo ERROR: Rhino 7 deployment failed
    exit /b 1
)
goto :cleanup