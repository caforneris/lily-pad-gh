@echo off
REM ============================================================
REM LilyPad-GH Full Installer
REM Installs plugin, Julia runtime, packages, and configures PATH
REM ============================================================
REM
REM DISTRIBUTION FOLDER STRUCTURE:
REM ------------------------------
REM LilyPadGH_Distribution/
REM +-- Install_LilyPadGH.bat     # This file - double-click to install
REM +-- README.txt                # Installation instructions
REM +-- Plugin/
REM |   +-- LilyPadGH.gha         # Grasshopper plugin
REM |   +-- Julia/                # Scripts + package installer
REM +-- JuliaPackage/             # Bundled Julia 1.11.7 runtime
REM +-- JuliaDepot/               # Pre-packaged packages (optional ~5GB)
REM
REM WHAT THIS INSTALLER DOES:
REM -------------------------
REM [1/4] Install plugin to Grasshopper packages folder
REM [2/4] Install bundled Julia runtime
REM [3/4] Add Julia to user PATH environment variable
REM [4/4] Install Julia packages (from depot or via script)
REM
REM REQUIRED JULIA PACKAGES:
REM   HTTP, JSON3, Plots, StaticArrays, WaterLily, ParametricBodies
REM
REM See INSTALL_GUIDE.md for full documentation
REM ============================================================
setlocal enabledelayedexpansion

echo.
echo ============================================================
echo    LilyPad-GH Installer for Rhino 8 / Grasshopper
echo ============================================================
echo.

REM Get the directory where this installer is located
set INSTALLER_DIR=%~dp0

REM Set deployment paths
set PLUGIN_PATH=%APPDATA%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1
set JULIA_DEPOT=%USERPROFILE%\.julia
set JULIA_BIN_PATH=%PLUGIN_PATH%\JuliaPackage\julia-1.11.7-win64\julia-1.11.7\bin

echo This installer will:
echo   1. Install LilyPad-GH plugin for Grasshopper
echo   2. Install bundled Julia runtime
echo   3. Add Julia to your PATH environment variable
echo   4. Install required Julia packages
echo.
echo Installation paths:
echo   Plugin: %PLUGIN_PATH%
echo   Julia packages: %JULIA_DEPOT%
echo.
echo ============================================================
echo.

REM Check if Rhino is running
tasklist /FI "IMAGENAME eq Rhino.exe" 2>NUL | find /I /N "Rhino.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo WARNING: Rhino appears to be running.
    echo Please close Rhino before continuing.
    echo.
    pause
)

echo Press any key to begin installation...
pause >nul
echo.

REM ============================================================
REM Step 1: Install Plugin
REM ============================================================
echo [1/4] Installing LilyPad-GH plugin...
echo.

REM Create plugin directory
if not exist "%PLUGIN_PATH%" (
    echo   Creating plugin directory...
    mkdir "%PLUGIN_PATH%"
)

REM Copy plugin files
echo   Copying plugin files...
if exist "%INSTALLER_DIR%Plugin\LilyPadGH.gha" (
    xcopy /Y /Q "%INSTALLER_DIR%Plugin\LilyPadGH.gha" "%PLUGIN_PATH%\" >nul
    echo   [OK] LilyPadGH.gha
) else (
    echo   [SKIP] LilyPadGH.gha not found
)

REM Copy Julia scripts
if exist "%INSTALLER_DIR%Plugin\Julia\" (
    echo   Copying Julia scripts...
    xcopy /Y /E /I /Q "%INSTALLER_DIR%Plugin\Julia\*" "%PLUGIN_PATH%\Julia\" >nul
    echo   [OK] Julia scripts
) else (
    echo   [SKIP] Julia scripts not found
)

echo.
echo   Plugin installation complete.
echo.

REM ============================================================
REM Step 2: Install Bundled Julia Runtime
REM ============================================================
echo [2/4] Installing Julia runtime...
echo.

if exist "%INSTALLER_DIR%JuliaPackage\" (
    echo   Copying Julia runtime (this may take a moment)...
    robocopy "%INSTALLER_DIR%JuliaPackage" "%PLUGIN_PATH%\JuliaPackage" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
    if errorlevel 8 (
        echo   [WARN] Some files may not have copied
    ) else (
        echo   [OK] Julia runtime installed
    )
) else (
    echo   [SKIP] Bundled Julia not found in installer
    echo   Users will need Julia installed on their system
)
echo.

REM ============================================================
REM Step 3: Add Julia to PATH
REM ============================================================
echo [3/4] Configuring Julia PATH...
echo.

REM Check if Julia bin exists
if exist "%JULIA_BIN_PATH%\julia.exe" (
    REM Check if already in PATH
    echo %PATH% | find /I "%JULIA_BIN_PATH%" >nul
    if errorlevel 1 (
        echo   Adding Julia to user PATH...

        REM Get current user PATH
        for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v PATH 2^>nul') do set "CURRENT_PATH=%%b"

        REM Check if PATH exists and add Julia
        if defined CURRENT_PATH (
            REM Check if Julia path already in registry PATH
            echo !CURRENT_PATH! | find /I "%JULIA_BIN_PATH%" >nul
            if errorlevel 1 (
                setx PATH "!CURRENT_PATH!;%JULIA_BIN_PATH%" >nul 2>&1
                echo   [OK] Julia added to PATH
                echo.
                echo   NOTE: You may need to restart your terminal/Rhino for PATH changes to take effect.
            ) else (
                echo   [OK] Julia already in PATH
            )
        ) else (
            REM No PATH exists, create it
            setx PATH "%JULIA_BIN_PATH%" >nul 2>&1
            echo   [OK] Julia PATH created
        )
    ) else (
        echo   [OK] Julia already in current session PATH
    )
) else (
    echo   [SKIP] Julia bin not found, skipping PATH configuration
)
echo.

REM ============================================================
REM Step 4: Install Julia Packages
REM ============================================================
echo [4/4] Installing Julia packages...
echo.

REM Check if pre-packaged depot exists
if exist "%INSTALLER_DIR%JuliaDepot\packages\" (
    echo   Found pre-packaged Julia packages.
    echo   Extracting to %JULIA_DEPOT%...

    REM Create .julia directory if needed
    if not exist "%JULIA_DEPOT%" mkdir "%JULIA_DEPOT%"

    REM Copy packages
    echo   Copying packages (this may take several minutes)...
    robocopy "%INSTALLER_DIR%JuliaDepot" "%JULIA_DEPOT%" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
    if errorlevel 8 (
        echo   [WARN] Some package files may not have copied
    ) else (
        echo   [OK] Julia packages installed
    )
    goto :install_complete
)

REM No pre-packaged depot - try to run installer script
echo   No pre-packaged packages found.
echo   Attempting to install packages via Julia...
echo.

REM Find Julia
set JULIA_EXE=

if exist "%PLUGIN_PATH%\JuliaPackage\julia-1.11.7-win64\julia-1.11.7\bin\julia.exe" (
    set JULIA_EXE=%PLUGIN_PATH%\JuliaPackage\julia-1.11.7-win64\julia-1.11.7\bin\julia.exe
    echo   Using bundled Julia
) else if exist "%LocalAppData%\Programs\Julia-1.11.7\bin\julia.exe" (
    set JULIA_EXE=%LocalAppData%\Programs\Julia-1.11.7\bin\julia.exe
    echo   Using system Julia 1.11.7
) else if exist "%LocalAppData%\Programs\Julia-1.11\bin\julia.exe" (
    set JULIA_EXE=%LocalAppData%\Programs\Julia-1.11\bin\julia.exe
    echo   Using system Julia 1.11
) else if exist "%LocalAppData%\Programs\Julia-1.10\bin\julia.exe" (
    set JULIA_EXE=%LocalAppData%\Programs\Julia-1.10\bin\julia.exe
    echo   Using system Julia 1.10
)

if "%JULIA_EXE%"=="" (
    echo.
    echo   [WARN] Julia not found!
    echo.
    echo   You will need to install Julia packages manually:
    echo     1. Download Julia from https://julialang.org/downloads/
    echo     2. Install Julia 1.10 or newer
    echo     3. Run the package installer at:
    echo        %PLUGIN_PATH%\Julia\Install_LilyPad_Packages.bat
    echo.
    goto :install_complete
)

REM Run package installer
if exist "%PLUGIN_PATH%\Julia\install_packages.jl" (
    echo   Installing packages (this may take 5-10 minutes)...
    echo.
    "%JULIA_EXE%" "%PLUGIN_PATH%\Julia\install_packages.jl"
) else (
    echo   [WARN] Package installer script not found
    echo   Please run Install_LilyPad_Packages.bat manually
)

:install_complete
echo.
echo ============================================================
echo    Installation Complete!
echo ============================================================
echo.
echo LilyPad-GH has been installed to:
echo   %PLUGIN_PATH%
echo.
echo Julia has been added to your PATH. You can now run 'julia'
echo from any command prompt (after restarting the terminal).
echo.
echo To use LilyPad-GH:
echo   1. Open Rhino 8
echo   2. Type "Grasshopper" to open Grasshopper
echo   3. Find LilyPad component in the toolbar
echo.
echo If you encounter issues:
echo   - Restart Rhino/terminal for PATH changes
echo   - Check that Julia packages are installed
echo   - Run: %PLUGIN_PATH%\Julia\Install_LilyPad_Packages.bat
echo.
echo ============================================================
echo.
pause
