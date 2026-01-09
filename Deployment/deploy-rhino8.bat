@echo off
REM ═══════════════════════════════════════════════════════════════════════════
REM  LilyPadGH - Rhino 8 Deployment Script
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
    echo WARNING: Failed to copy plugin - is Rhino running? Close Rhino and rebuild.
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

REM ─── Copy Bundled Julia Runtime ─────────────────────────────────────────
echo Copying bundled Julia runtime...
if not exist "%PROJECT_DIR%JuliaPackage\" goto :no_julia_package
echo   This may take a moment - large folder using robocopy...
robocopy "%PROJECT_DIR%JuliaPackage" "%DEPLOY_PATH%\JuliaPackage" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
REM robocopy returns 1 for success with copies, 0 for no copies needed, 2+ for issues
if errorlevel 8 (
    echo WARNING: Some Julia runtime files may not have copied
) else (
    echo SUCCESS: Bundled Julia runtime copied
)
goto :julia_package_done
:no_julia_package
echo WARNING: JuliaPackage not found at %PROJECT_DIR%JuliaPackage\
echo   Users will need Julia installed on their system
:julia_package_done

REM ─── DO NOT DELETE if deploying to Both - master script handles cleanup ───
REM The master deploy.bat script will handle deletion after all deployments

echo.
echo ✓ Rhino 8 deployment complete
echo ═══════════════════════════════════════════════════════════════════════════
exit /b 0