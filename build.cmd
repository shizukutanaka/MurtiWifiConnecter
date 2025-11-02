@echo off
title MurtiWiFi Connector Build Script
echo MurtiWiFi Connector Build Script
echo ================================
echo.

REM Check if running as administrator
REM net session >nul 2>&1
REM if %errorlevel% neq 0 (
REM     echo This build script requires administrator privileges.
REM     echo Please run as administrator.
REM     pause
REM     exit /b 1
REM )

REM Check .NET SDK
echo Checking .NET SDK...
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo Error: .NET SDK not found!
    echo Please install .NET 8.0 or later from:
    echo https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo .NET SDK found.
echo.

REM Clean previous builds
echo Cleaning previous builds...
if exist "bin" rmdir /s /q "bin" 2>nul
if exist "obj" rmdir /s /q "obj" 2>nul
if exist "build.log" del "build.log" 2>nul
echo Clean completed.
echo.

REM Restore packages
echo Restoring NuGet packages...
dotnet restore >nul 2>&1
if %errorlevel% neq 0 (
    echo Package restore failed!
    pause
    exit /b 1
)
echo Packages restored.
echo.

REM Build the project
echo Building project...
echo Configuration: Release
echo Platform: Any CPU
echo.

dotnet build --configuration Release --verbosity minimal > build.log 2>&1
if %errorlevel% neq 0 (
    echo Build failed!
    echo Check build.log for details.
    type build.log
    pause
    exit /b 1
)

REM Check if executable was created
if not exist "bin\Release\net8.0\MurtiWifiConnecter.exe" (
    echo Build completed but executable not found!
    echo Expected location: bin\Release\net8.0\MurtiWifiConnecter.exe
    type build.log
    pause
    exit /b 1
)

echo Build successful!
echo.

REM Create publish directory
if not exist "publish" mkdir "publish" 2>nul

REM Publish as single file
echo Publishing as single file executable...
dotnet publish --configuration Release --runtime win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true -o ./publish >nul 2>&1
if %errorlevel% neq 0 (
    echo Single file publish failed!
    echo Falling back to regular publish...
    dotnet publish --configuration Release -o ./publish >nul 2>&1
)

REM Copy executable to root directory
echo Copying executable to project root...
copy "bin\Release\net8.0\MurtiWifiConnecter.exe" "." >nul 2>&1
if %errorlevel% neq 0 (
    echo Failed to copy executable to root directory.
) else (
    echo Executable copied to project root.
)

echo.
echo Build completed successfully!
echo.
echo Executable locations:
echo - %~dp0bin\Release\net8.0\MurtiWifiConnecter.exe (primary)
echo - %~dp0publish\MurtiWifiConnecter.exe (single file)
echo - %~dp0MurtiWifiConnecter.exe (copied)
echo.
echo You can now run the setup script or use the executable directly.
echo.

pause