@echo off
REM ========================================
REM FILE: deploy.bat
REM DESC: Deployment script for LilyPadGH component including Julia package distribution.
REM      Handles multiple Rhino versions and ensures Julia runtime is properly deployed.
REM --- REVISIONS ---
REM - 2025-09-22: Added Julia package deployment for bundled distribution.
REM   - Integrated Julia runtime copying to package directories.
REM   - Enhanced error handling for Julia package deployment.
REM   - Maintains compatibility with existing multi-version deployment.
REM ========================================

REM PART 1: Parameter Validation and Setup
REM Validates input parameters and sets up deployment environment variables.

set "TARGET_DIR=%~1"
set "PROJECT_DIR=%~2"
set "CONFIGURATION=%~3"
set "RHINO_VERSION=%~4"

if "%TARGET_DIR%"=="" (
    echo ERROR: Target directory not specified
    exit /b 1
)

if "%PROJECT_DIR%"=="" (
    echo ERROR: Project directory not specified
    exit /b 1
)

if "%CONFIGURATION%"=="" (
    set "CONFIGURATION=Debug"
)

if "%RHINO_VERSION%"=="" (
    set "RHINO_VERSION=Both"
)

echo.
echo ========================================
echo LilyPadGH Deployment Script
echo ========================================
echo Target Directory: %TARGET_DIR%
echo Project Directory: %PROJECT_DIR%
echo Configuration: %CONFIGURATION%
echo Rhino Version: %RHINO_VERSION%
echo.

REM PART 2: Rhino Package Directory Setup
REM Creates deployment paths for different Rhino versions.

set "RHINO8_PACKAGES=%APPDATA%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1"
set "RHINO7_PACKAGES=%APPDATA%\McNeel\Rhinoceros\packages\7.0\LilyPadGH\0.0.1"

REM PART 3: Main Component Deployment
REM Deploys the GHA file and associated resources to appropriate Rhino versions.

if /i "%RHINO_VERSION%"=="8" goto :DeployRhino8
if /i "%RHINO_VERSION%"=="7" goto :DeployRhino7
if /i "%RHINO_VERSION%"=="Both" goto :DeployBoth

:DeployBoth
echo Deploying to both Rhino 7 and 8...
call :DeployToRhino8
call :DeployToRhino7
goto :DeployJulia

:DeployRhino8
echo Deploying to Rhino 8 only...
call :DeployToRhino8
goto :DeployJulia

:DeployRhino7
echo Deploying to Rhino 7 only...
call :DeployToRhino7
goto :DeployJulia

:DeployToRhino8
echo.
echo --- Deploying to Rhino 8 ---
if not exist "%RHINO8_PACKAGES%" (
    echo Creating Rhino 8 package directory...
    mkdir "%RHINO8_PACKAGES%"
)

echo Copying GHA to Rhino 8...
copy /Y "%TARGET_DIR%LilyPadGH.gha" "%RHINO8_PACKAGES%\" >nul
if errorlevel 1 (
    echo ERROR: Failed to copy GHA to Rhino 8
    exit /b 1
)

echo Copying Julia scripts to Rhino 8...
if not exist "%RHINO8_PACKAGES%\Julia" mkdir "%RHINO8_PACKAGES%\Julia"
copy /Y "%TARGET_DIR%Julia\*.*" "%RHINO8_PACKAGES%\Julia\" >nul
if errorlevel 1 (
    echo WARNING: Julia scripts copy failed for Rhino 8
)

echo Rhino 8 deployment completed successfully.
goto :eof

:DeployToRhino7
echo.
echo --- Deploying to Rhino 7 ---
if not exist "%RHINO7_PACKAGES%" (
    echo Creating Rhino 7 package directory...
    mkdir "%RHINO7_PACKAGES%"
)

echo Copying GHA to Rhino 7...
copy /Y "%TARGET_DIR%LilyPadGH.gha" "%RHINO7_PACKAGES%\" >nul
if errorlevel 1 (
    echo ERROR: Failed to copy GHA to Rhino 7
    exit /b 1
)

echo Copying Julia scripts to Rhino 7...
if not exist "%RHINO7_PACKAGES%\Julia" mkdir "%RHINO7_PACKAGES%\Julia"
copy /Y "%TARGET_DIR%Julia\*.*" "%RHINO7_PACKAGES%\Julia\" >nul
if errorlevel 1 (
    echo WARNING: Julia scripts copy failed for Rhino 7
)

echo Rhino 7 deployment completed successfully.
goto :eof

REM PART 4: Julia Package Deployment
REM Deploys the bundled Julia runtime to enable team collaboration without manual Julia installation.

:DeployJulia
echo.
echo --- Deploying Julia Package ---

REM Check if Julia package exists in build output
set "JULIA_SOURCE=%TARGET_DIR%JuliaPackage"
if not exist "%JULIA_SOURCE%" (
    echo WARNING: Julia package not found in build output at %JULIA_SOURCE%
    echo Julia package deployment skipped.
    goto :DeploymentComplete
)

REM Deploy Julia package to each target Rhino version
if /i "%RHINO_VERSION%"=="8" (
    call :DeployJuliaToVersion "%RHINO8_PACKAGES%"
) else if /i "%RHINO_VERSION%"=="7" (
    call :DeployJuliaToVersion "%RHINO7_PACKAGES%"
) else (
    call :DeployJuliaToVersion "%RHINO8_PACKAGES%"
    call :DeployJuliaToVersion "%RHINO7_PACKAGES%"
)

goto :DeploymentComplete

:DeployJuliaToVersion
set "TARGET_JULIA_DIR=%~1\Julia"
echo Deploying Julia package to: %TARGET_JULIA_DIR%

if not exist "%TARGET_JULIA_DIR%" (
    echo Creating Julia directory...
    mkdir "%TARGET_JULIA_DIR%"
)

echo Copying Julia runtime package...
xcopy /E /I /Y /Q "%JULIA_SOURCE%" "%TARGET_JULIA_DIR%" >nul
if errorlevel 1 (
    echo ERROR: Failed to copy Julia package to %TARGET_JULIA_DIR%
    exit /b 1
) else (
    echo Julia package deployed successfully to %TARGET_JULIA_DIR%
)

REM Verify critical Julia executable exists after deployment
set "JULIA_EXE=%TARGET_JULIA_DIR%\julia-1.11.7-win64\bin\julia.exe"
if exist "%JULIA_EXE%" (
    echo Julia executable verified at: %JULIA_EXE%
) else (
    echo WARNING: Julia executable not found at expected location: %JULIA_EXE%
)

goto :eof

REM PART 5: Deployment Completion and Summary
REM Provides deployment status summary and cleanup.

:DeploymentComplete
echo.
echo ========================================
echo Deployment Summary
echo ========================================
echo Configuration: %CONFIGURATION%
echo Target Rhino Version(s): %RHINO_VERSION%

if /i "%RHINO_VERSION%"=="Both" (
    echo - Rhino 8 Package: %RHINO8_PACKAGES%
    echo - Rhino 7 Package: %RHINO7_PACKAGES%
) else if /i "%RHINO_VERSION%"=="8" (
    echo - Rhino 8 Package: %RHINO8_PACKAGES%
) else if /i "%RHINO_VERSION%"=="7" (
    echo - Rhino 7 Package: %RHINO7_PACKAGES%
)

echo.
echo LilyPadGH deployment completed successfully!
echo ========================================

exit /b 0