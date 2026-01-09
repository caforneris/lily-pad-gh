@echo off
REM ============================================================
REM LilyPad-GH Distribution Packager
REM Run this to create a distribution package for beta testers
REM ============================================================
setlocal

echo.
echo ============================================================
echo    LilyPad-GH Distribution Packager
echo ============================================================
echo.

REM Get paths
set SCRIPT_DIR=%~dp0
set PROJECT_DIR=%SCRIPT_DIR%..
set DIST_DIR=%SCRIPT_DIR%LilyPadGH_Distribution

echo This will create a distribution package at:
echo   %DIST_DIR%
echo.
echo Press any key to continue...
pause >nul
echo.

REM Clean and create distribution folder
if exist "%DIST_DIR%" (
    echo Cleaning existing distribution folder...
    rmdir /S /Q "%DIST_DIR%"
)
mkdir "%DIST_DIR%"
mkdir "%DIST_DIR%\Plugin"
mkdir "%DIST_DIR%\Plugin\Julia"

REM ============================================================
echo [1/4] Copying plugin files...
REM ============================================================

REM Copy from deployed location (already built)
set DEPLOY_PATH=%APPDATA%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1

if exist "%DEPLOY_PATH%\LilyPadGH.gha" (
    copy "%DEPLOY_PATH%\LilyPadGH.gha" "%DIST_DIR%\Plugin\" >nul
    echo   [OK] LilyPadGH.gha
) else (
    echo   [WARN] Plugin not found - build the project first!
)

REM ============================================================
echo [2/4] Copying Julia scripts...
REM ============================================================

if exist "%DEPLOY_PATH%\Julia\" (
    xcopy /Y /E /I /Q "%DEPLOY_PATH%\Julia\*" "%DIST_DIR%\Plugin\Julia\" >nul
    echo   [OK] Julia scripts
) else (
    echo   [WARN] Julia scripts not found
)

REM ============================================================
echo [3/4] Copying bundled Julia runtime...
REM ============================================================

if exist "%PROJECT_DIR%\JuliaPackage\" (
    echo   Copying Julia runtime (this may take a few minutes)...
    robocopy "%PROJECT_DIR%\JuliaPackage" "%DIST_DIR%\JuliaPackage" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
    echo   [OK] Julia runtime
) else (
    echo   [WARN] JuliaPackage not found
)

REM ============================================================
echo [4/4] Copying Julia depot (packages)...
REM ============================================================

set JULIA_DEPOT=%USERPROFILE%\.julia

echo.
echo Do you want to include pre-packaged Julia packages?
echo This makes installation easier but adds ~5GB to the distribution.
echo.
set /p INCLUDE_DEPOT="Include packages? (Y/N): "

if /I "%INCLUDE_DEPOT%"=="Y" (
    if exist "%JULIA_DEPOT%\packages\" (
        echo   Copying Julia packages (this may take several minutes)...
        mkdir "%DIST_DIR%\JuliaDepot"

        REM Copy essential folders
        if exist "%JULIA_DEPOT%\packages" (
            robocopy "%JULIA_DEPOT%\packages" "%DIST_DIR%\JuliaDepot\packages" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
        )
        if exist "%JULIA_DEPOT%\compiled" (
            robocopy "%JULIA_DEPOT%\compiled" "%DIST_DIR%\JuliaDepot\compiled" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
        )
        if exist "%JULIA_DEPOT%\artifacts" (
            robocopy "%JULIA_DEPOT%\artifacts" "%DIST_DIR%\JuliaDepot\artifacts" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
        )
        if exist "%JULIA_DEPOT%\registries" (
            robocopy "%JULIA_DEPOT%\registries" "%DIST_DIR%\JuliaDepot\registries" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
        )

        echo   [OK] Julia packages copied
    ) else (
        echo   [WARN] Julia depot not found
    )
) else (
    echo   [SKIP] Packages not included - users will run installer
)

REM ============================================================
echo.
echo Copying installer...
REM ============================================================

copy "%SCRIPT_DIR%Install_LilyPadGH.bat" "%DIST_DIR%\" >nul
echo   [OK] Installer

REM Create README
echo Creating README...
(
echo ============================================================
echo    LilyPad-GH Installation Instructions
echo ============================================================
echo.
echo QUICK INSTALL:
echo   1. Run Install_LilyPadGH.bat as Administrator
echo   2. Follow the prompts
echo   3. Open Rhino 8 and Grasshopper
echo.
echo WHAT'S INCLUDED:
echo   - LilyPad-GH Grasshopper plugin
echo   - Julia 1.11.7 runtime
echo   - Required Julia packages ^(if pre-packaged^)
echo.
echo MANUAL INSTALLATION ^(if installer fails^):
echo   1. Copy Plugin\LilyPadGH.gha to:
echo      %%APPDATA%%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1\
echo   2. Copy Plugin\Julia\ folder to same location
echo   3. Copy JuliaPackage\ folder to same location
echo   4. Run Plugin\Julia\Install_LilyPad_Packages.bat
echo.
echo REQUIREMENTS:
echo   - Rhino 8 with Grasshopper
echo   - Windows 10/11
echo.
echo TROUBLESHOOTING:
echo   - If Julia packages fail to install, run:
echo     Plugin\Julia\Install_LilyPad_Packages.bat
echo   - Check Julia is in PATH: open cmd and type 'julia'
echo   - Restart Rhino after installation
echo.
echo ============================================================
) > "%DIST_DIR%\README.txt"
echo   [OK] README.txt

echo.
echo ============================================================
echo    Distribution Package Created!
echo ============================================================
echo.
echo Location: %DIST_DIR%
echo.
echo Contents:
dir /B "%DIST_DIR%"
echo.
echo To distribute:
echo   1. Zip the entire LilyPadGH_Distribution folder
echo   2. Share with beta testers
echo   3. Users run Install_LilyPadGH.bat
echo.
echo ============================================================
pause
