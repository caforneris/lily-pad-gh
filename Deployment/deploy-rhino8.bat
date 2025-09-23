@echo off
REM ═══════════════════════════════════════════════════════════════════════════
REM  LilyPadGH - Rhino 8 Deployment Script
REM --- REVISIONS ---
REM - 2025-09-22: Added Julia package deployment for bundled distribution.
REM   - Integrated Julia runtime copying alongside existing Julia scripts.
REM   - Enhanced verification of Julia executable deployment.
REM ═══════════════════════════════════════════════════════════════════════════

REM Parameters passed from master script
set TARGET_DIR=%~1
set PROJECT_DIR=%~2
set CONFIG=%~3
set DEPLOYMENT_MODE=%~4

REM Deployment path
set DEPLOY_PATH=%appdata%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1

echo.
echo ═══ Deploying to Rhino 8 ═══════════════════════════════════════════════════
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

REM ─── Copy GHX template files ─────────────────────────────────────────────
echo Copying GHX template files...
if exist "%PROJECT_DIR%GHX\" (
    xcopy /Y /E /I "%PROJECT_DIR%GHX\*" "%DEPLOY_PATH%\GHX\" >nul
    if errorlevel 1 (
        echo WARNING: Some GHX files may not have copied
    ) else (
        echo ✓ GHX files copied successfully
    )
) else (
    echo WARNING: GHX directory not found at %PROJECT_DIR%GHX\
)

REM ─── Copy Julia script files ────────────────────────────────────────────
echo Copying Julia script files...
if exist "%TARGET_DIR%Julia\" (
    xcopy /Y /E /I "%TARGET_DIR%Julia\*" "%DEPLOY_PATH%\Julia\" >nul
    if errorlevel 1 (
        echo WARNING: Some Julia script files may not have copied
    ) else (
        echo ✓ Julia script files copied successfully
    )
) else (
    echo WARNING: Julia scripts directory not found at %TARGET_DIR%Julia\
)

REM ─── Copy Julia package (bundled runtime) ────────────────────────────────
echo Copying Julia runtime package...
if exist "%TARGET_DIR%JuliaPackage\" (
    REM Create Julia directory if it doesn't exist
    if not exist "%DEPLOY_PATH%\Julia\" (
        mkdir "%DEPLOY_PATH%\Julia"
    )
    
    echo Deploying bundled Julia runtime...
    xcopy /Y /E /I /Q "%TARGET_DIR%JuliaPackage\*" "%DEPLOY_PATH%\Julia\" >nul
    if errorlevel 1 (
        echo ERROR: Failed to copy Julia runtime package
        exit /b 1
    ) else (
        echo ✓ Julia runtime package deployed successfully
        
        REM Verify critical Julia executable exists
        set "JULIA_EXE=%DEPLOY_PATH%\Julia\julia-1.11.7-win64\bin\julia.exe"
        if exist "!JULIA_EXE!" (
            echo ✓ Julia executable verified at deployment location
        ) else (
            echo WARNING: Julia executable not found at expected location
        )
    )
) else (
    echo INFO: Julia runtime package not found at %TARGET_DIR%JuliaPackage\
    echo INFO: Component will attempt to use system Julia installation if available
)

REM ─── DO NOT DELETE if deploying to Both - master script handles cleanup ───
REM The master deploy.bat script will handle deletion after all deployments

echo.
echo ✓ Rhino 8 deployment complete
echo ═══════════════════════════════════════════════════════════════════════════