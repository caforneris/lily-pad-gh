@echo off
REM ═══════════════════════════════════════════════════════════════════════════
REM  LilyPadGH - Rhino 7 Deployment Script
REM ═══════════════════════════════════════════════════════════════════════════

REM Parameters passed from master script
set TARGET_DIR=%~1
set PROJECT_DIR=%~2
set CONFIG=%~3
set DEPLOYMENT_MODE=%~4

REM Deployment path
set DEPLOY_PATH=%appdata%\McNeel\Rhinoceros\packages\7.0\LilyPadGH\0.0.1

echo.
echo ═══ Deploying to Rhino 7 ═══════════════════════════════════════════════════
echo Target: %DEPLOY_PATH%
echo.

REM Create deployment directory if it doesn't exist
if not exist "%DEPLOY_PATH%" (
    echo Creating deployment directory...
    mkdir "%DEPLOY_PATH%"
)

REM ─── Copy main plugin ─────────────────────────────────────────────────────
echo Copying plugin...
xcopy /Y /R /C "%TARGET_DIR%LilyPadGH.gha" "%DEPLOY_PATH%\" >nul
if errorlevel 1 (
    echo ERROR: Failed to copy plugin
    exit /b 1
)

REM ─── Copy Julia files ────────────────────────────────────────────────────
echo Copying Julia files...
if exist "%TARGET_DIR%Julia\" (
    xcopy /Y /E /I "%TARGET_DIR%Julia\*" "%DEPLOY_PATH%\Julia\" >nul
    if errorlevel 1 (
        echo WARNING: Some Julia files may not have copied
    ) else (
        echo ✓ Julia files copied successfully
    )
) else (
    echo WARNING: Julia directory not found at %TARGET_DIR%Julia\
)


REM ─── DO NOT DELETE if deploying to Both - master script handles cleanup ───
REM The master deploy.bat script will handle deletion after all deployments

echo.
echo ✓ Rhino 7 deployment complete
echo ═══════════════════════════════════════════════════════════════════════════
exit /b 0