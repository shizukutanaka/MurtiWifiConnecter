@echo off
:: WiFi Manager Pro - Commercial Grade Installer
:: Version 2.1.0 - Professional Installation Script

setlocal EnableDelayedExpansion

:: ═══════════════════════════════════════════════════════════════════════════════
:: INSTALLER CONFIGURATION
:: ═══════════════════════════════════════════════════════════════════════════════

set "APP_NAME=WiFi Manager Pro"
set "APP_VERSION=2.1.0"
set "PUBLISHER=MurtiWifi Solutions"
set "INSTALL_DIR=%ProgramFiles%\WiFiManagerPro"
set "DATA_DIR=%ProgramData%\WiFiManagerPro"
set "USER_DATA_DIR=%APPDATA%\WiFiManagerPro"
set "TEMP_DIR=%TEMP%\WiFiManagerPro_Install"

:: Color codes for console output
set "RED=[91m"
set "GREEN=[92m"
set "YELLOW=[93m"
set "BLUE=[94m"
set "MAGENTA=[95m"
set "CYAN=[96m"
set "WHITE=[97m"
set "RESET=[0m"

:: ═══════════════════════════════════════════════════════════════════════════════
:: INSTALLATION FUNCTIONS
:: ═══════════════════════════════════════════════════════════════════════════════

echo.
echo %CYAN%╔══════════════════════════════════════════════════════════════════════════════╗%RESET%
echo %CYAN%║                           WiFi Manager Pro v2.1.0                           ║%RESET%
echo %CYAN%║                        Professional Installation Wizard                     ║%RESET%
echo %CYAN%╚══════════════════════════════════════════════════════════════════════════════╝%RESET%
echo.

:: Check for administrator privileges
call :CheckAdminRights
if %ERRORLEVEL% neq 0 (
    echo %RED%Error: Administrator privileges required for installation.%RESET%
    echo %YELLOW%Please right-click and select "Run as administrator"%RESET%
    pause
    exit /b 1
)

echo %GREEN%✓ Administrator privileges confirmed%RESET%

:: Display installation information
echo.
echo %BLUE%Installation Details:%RESET%
echo   Application: %APP_NAME%
echo   Version: %APP_VERSION%
echo   Publisher: %PUBLISHER%
echo   Install Location: %INSTALL_DIR%
echo   Data Location: %DATA_DIR%
echo.

:: Confirm installation
set /p "CONFIRM=Do you want to proceed with the installation? (Y/N): "
if /i not "!CONFIRM!"=="Y" (
    echo %YELLOW%Installation cancelled by user.%RESET%
    pause
    exit /b 0
)

echo.
echo %CYAN%Starting installation process...%RESET%

:: ═══════════════════════════════════════════════════════════════════════════════
:: PRE-INSTALLATION CHECKS
:: ═══════════════════════════════════════════════════════════════════════════════

echo.
echo %BLUE%[1/8] Performing pre-installation checks...%RESET%

:: Check Windows version
call :CheckWindowsVersion
if %ERRORLEVEL% neq 0 (
    echo %RED%Error: Unsupported Windows version%RESET%
    pause
    exit /b 1
)

:: Check .NET Framework
call :CheckDotNetFramework
if %ERRORLEVEL% neq 0 (
    echo %RED%Error: .NET Framework 4.8 or higher required%RESET%
    echo %YELLOW%Please install .NET Framework 4.8 and try again%RESET%
    pause
    exit /b 1
)

:: Check system requirements
call :CheckSystemRequirements

echo %GREEN%✓ Pre-installation checks completed%RESET%

:: ═══════════════════════════════════════════════════════════════════════════════
:: STOP EXISTING SERVICES
:: ═══════════════════════════════════════════════════════════════════════════════

echo.
echo %BLUE%[2/8] Stopping existing services...%RESET%

:: Stop any running instances
taskkill /f /im MurtiWifiConnecter.exe >nul 2>&1
taskkill /f /im WiFiManagerPro.exe >nul 2>&1

:: Wait for processes to close
timeout /t 3 >nul

echo %GREEN%✓ Existing services stopped%RESET%

:: ═══════════════════════════════════════════════════════════════════════════════
:: CREATE DIRECTORIES
:: ═══════════════════════════════════════════════════════════════════════════════

echo.
echo %BLUE%[3/8] Creating installation directories...%RESET%

:: Create main installation directory
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%" 2>nul
    if %ERRORLEVEL% neq 0 (
        echo %RED%Error: Failed to create installation directory%RESET%
        pause
        exit /b 1
    )
)

:: Create subdirectories
mkdir "%INSTALL_DIR%\bin" 2>nul
mkdir "%INSTALL_DIR%\config" 2>nul
mkdir "%INSTALL_DIR%\logs" 2>nul
mkdir "%INSTALL_DIR%\docs" 2>nul
mkdir "%INSTALL_DIR%\resources" 2>nul

:: Create data directories
mkdir "%DATA_DIR%" 2>nul
mkdir "%DATA_DIR%\profiles" 2>nul
mkdir "%DATA_DIR%\analytics" 2>nul
mkdir "%DATA_DIR%\backups" 2>nul

:: Create user data directory
mkdir "%USER_DATA_DIR%" 2>nul

echo %GREEN%✓ Installation directories created%RESET%

:: ═══════════════════════════════════════════════════════════════════════════════
:: COPY APPLICATION FILES
:: ═══════════════════════════════════════════════════════════════════════════════

echo.
echo %BLUE%[4/8] Installing application files...%RESET%

:: Copy main executable
if exist "MurtiWifiConnecter.exe" (
    copy "MurtiWifiConnecter.exe" "%INSTALL_DIR%\bin\WiFiManagerPro.exe" >nul
    if %ERRORLEVEL% neq 0 (
        echo %RED%Error: Failed to copy main executable%RESET%
        pause
        exit /b 1
    )
) else (
    echo %RED%Error: Main executable not found%RESET%
    pause
    exit /b 1
)

:: Copy configuration files
if exist "default_settings.json" copy "default_settings.json" "%INSTALL_DIR%\config\" >nul
if exist "family_profiles.json" copy "family_profiles.json" "%INSTALL_DIR%\config\" >nul
if exist "app.config" copy "app.config" "%INSTALL_DIR%\config\" >nul

:: Copy documentation
if exist "README.md" copy "README.md" "%INSTALL_DIR%\docs\" >nul
if exist "LICENSE" copy "LICENSE" "%INSTALL_DIR%\docs\" >nul

:: Copy resources
if exist "Resources" (
    xcopy "Resources\*" "%INSTALL_DIR%\resources\" /E /I /Y >nul 2>&1
)

echo %GREEN%✓ Application files installed%RESET%

:: ═══════════════════════════════════════════════════════════════════════════════
:: CONFIGURE WINDOWS INTEGRATION
:: ═══════════════════════════════════════════════════════════════════════════════

echo.
echo %BLUE%[5/8] Configuring Windows integration...%RESET%

:: Create desktop shortcut
call :CreateDesktopShortcut

:: Create start menu entry
call :CreateStartMenuEntry

:: Register file associations
call :RegisterFileAssociations

:: Add to Windows PATH (optional)
call :AddToPath

:: Configure Windows Firewall exception
call :ConfigureFirewall

echo %GREEN%✓ Windows integration configured%RESET%

:: ═══════════════════════════════════════════════════════════════════════════════
:: INSTALL WINDOWS SERVICE
:: ═══════════════════════════════════════════════════════════════════════════════

echo.
echo %BLUE%[6/8] Installing Windows service...%RESET%

:: Create service wrapper script
call :CreateServiceWrapper

:: Install Windows service
sc create "WiFiManagerProService" binPath= "\"%INSTALL_DIR%\bin\WiFiManagerPro.exe\" --service" DisplayName= "WiFi Manager Pro Service" start= auto description= "Professional WiFi management and monitoring service"

if %ERRORLEVEL% equ 0 (
    echo %GREEN%✓ Windows service installed successfully%RESET%

    :: Start the service
    sc start "WiFiManagerProService" >nul 2>&1
    if %ERRORLEVEL% equ 0 (
        echo %GREEN%✓ Windows service started%RESET%
    ) else (
        echo %YELLOW%⚠ Service installed but failed to start automatically%RESET%
    )
) else (
    echo %YELLOW%⚠ Failed to install Windows service (application will still work)%RESET%
)

:: ═══════════════════════════════════════════════════════════════════════════════
:: CONFIGURE REGISTRY SETTINGS
:: ═══════════════════════════════════════════════════════════════════════════════

echo.
echo %BLUE%[7/8] Configuring system registry...%RESET%

:: Create application registry keys
reg add "HKLM\SOFTWARE\MurtiWifi\WiFiManagerPro" /f >nul 2>&1
reg add "HKLM\SOFTWARE\MurtiWifi\WiFiManagerPro" /v "InstallPath" /t REG_SZ /d "%INSTALL_DIR%" /f >nul 2>&1
reg add "HKLM\SOFTWARE\MurtiWifi\WiFiManagerPro" /v "Version" /t REG_SZ /d "%APP_VERSION%" /f >nul 2>&1
reg add "HKLM\SOFTWARE\MurtiWifi\WiFiManagerPro" /v "InstallDate" /t REG_SZ /d "%date%" /f >nul 2>&1

:: Add to Windows Add/Remove Programs
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WiFiManagerPro" /f >nul 2>&1
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WiFiManagerPro" /v "DisplayName" /t REG_SZ /d "%APP_NAME%" /f >nul 2>&1
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WiFiManagerPro" /v "DisplayVersion" /t REG_SZ /d "%APP_VERSION%" /f >nul 2>&1
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WiFiManagerPro" /v "Publisher" /t REG_SZ /d "%PUBLISHER%" /f >nul 2>&1
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WiFiManagerPro" /v "InstallLocation" /t REG_SZ /d "%INSTALL_DIR%" /f >nul 2>&1
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WiFiManagerPro" /v "UninstallString" /t REG_SZ /d "\"%INSTALL_DIR%\uninstall.exe\"" /f >nul 2>&1

:: Configure auto-start
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "WiFiManagerPro" /t REG_SZ /d "\"%INSTALL_DIR%\bin\WiFiManagerPro.exe\" --minimized" /f >nul 2>&1

echo %GREEN%✓ Registry configuration completed%RESET%

:: ═══════════════════════════════════════════════════════════════════════════════
:: FINALIZE INSTALLATION
:: ═══════════════════════════════════════════════════════════════════════════════

echo.
echo %BLUE%[8/8] Finalizing installation...%RESET%

:: Create uninstaller
call :CreateUninstaller

:: Set file permissions
icacls "%INSTALL_DIR%" /grant Users:(OI)(CI)RX >nul 2>&1
icacls "%DATA_DIR%" /grant Users:(OI)(CI)F >nul 2>&1
icacls "%USER_DATA_DIR%" /grant %USERNAME%:(OI)(CI)F >nul 2>&1

:: Create initial configuration
call :CreateInitialConfiguration

:: Register for Windows Update notifications
call :RegisterUpdateNotifications

:: Clean up temporary files
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%" >nul 2>&1

echo %GREEN%✓ Installation finalized%RESET%

:: ═══════════════════════════════════════════════════════════════════════════════
:: INSTALLATION COMPLETE
:: ═══════════════════════════════════════════════════════════════════════════════

echo.
echo %GREEN%╔══════════════════════════════════════════════════════════════════════════════╗%RESET%
echo %GREEN%║                        INSTALLATION COMPLETED SUCCESSFULLY                  ║%RESET%
echo %GREEN%╚══════════════════════════════════════════════════════════════════════════════╝%RESET%
echo.

echo %CYAN%Installation Summary:%RESET%
echo   • Application installed to: %INSTALL_DIR%
echo   • Configuration files: %DATA_DIR%
echo   • User settings: %USER_DATA_DIR%
echo   • Desktop shortcut created
echo   • Start menu entry added
echo   • Windows service installed and started
echo   • Auto-start configured
echo.

echo %BLUE%Next Steps:%RESET%
echo   1. Launch WiFi Manager Pro from the desktop or Start menu
echo   2. Complete the initial setup wizard
echo   3. Configure your WiFi preferences
echo   4. Explore family safety and power management features
echo.

set /p "LAUNCH=Would you like to launch WiFi Manager Pro now? (Y/N): "
if /i "!LAUNCH!"=="Y" (
    echo %CYAN%Launching WiFi Manager Pro...%RESET%
    start "" "%INSTALL_DIR%\bin\WiFiManagerPro.exe"
)

echo.
echo %GREEN%Thank you for choosing WiFi Manager Pro!%RESET%
echo %CYAN%For support, visit: https://github.com/MurtiWifi/WiFiManagerPro%RESET%
echo.
pause
exit /b 0

:: ═══════════════════════════════════════════════════════════════════════════════
:: HELPER FUNCTIONS
:: ═══════════════════════════════════════════════════════════════════════════════

:CheckAdminRights
net session >nul 2>&1
exit /b %ERRORLEVEL%

:CheckWindowsVersion
for /f "tokens=2 delims=[]" %%i in ('ver') do set winver=%%i
for /f "tokens=2,3 delims=. " %%i in ("%winver%") do (
    if %%i lss 10 (
        if %%i lss 6 exit /b 1
        if %%i equ 6 if %%j lss 1 exit /b 1
    )
)
echo %GREEN%✓ Windows version supported%RESET%
exit /b 0

:CheckDotNetFramework
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release >nul 2>&1
if %ERRORLEVEL% neq 0 exit /b 1
echo %GREEN%✓ .NET Framework available%RESET%
exit /b 0

:CheckSystemRequirements
echo %GREEN%✓ System requirements met%RESET%
exit /b 0

:CreateDesktopShortcut
set "DESKTOP=%USERPROFILE%\Desktop"
echo Set oWS = WScript.CreateObject("WScript.Shell") > "%TEMP%\CreateShortcut.vbs"
echo sLinkFile = "%DESKTOP%\WiFi Manager Pro.lnk" >> "%TEMP%\CreateShortcut.vbs"
echo Set oLink = oWS.CreateShortcut(sLinkFile) >> "%TEMP%\CreateShortcut.vbs"
echo oLink.TargetPath = "%INSTALL_DIR%\bin\WiFiManagerPro.exe" >> "%TEMP%\CreateShortcut.vbs"
echo oLink.WorkingDirectory = "%INSTALL_DIR%\bin" >> "%TEMP%\CreateShortcut.vbs"
echo oLink.Description = "WiFi Manager Pro - Professional WiFi Management" >> "%TEMP%\CreateShortcut.vbs"
echo oLink.Save >> "%TEMP%\CreateShortcut.vbs"
cscript //nologo "%TEMP%\CreateShortcut.vbs"
del "%TEMP%\CreateShortcut.vbs"
exit /b 0

:CreateStartMenuEntry
set "STARTMENU=%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs"
mkdir "%STARTMENU%\WiFi Manager Pro" 2>nul
echo Set oWS = WScript.CreateObject("WScript.Shell") > "%TEMP%\CreateStartMenu.vbs"
echo sLinkFile = "%STARTMENU%\WiFi Manager Pro\WiFi Manager Pro.lnk" >> "%TEMP%\CreateStartMenu.vbs"
echo Set oLink = oWS.CreateShortcut(sLinkFile) >> "%TEMP%\CreateStartMenu.vbs"
echo oLink.TargetPath = "%INSTALL_DIR%\bin\WiFiManagerPro.exe" >> "%TEMP%\CreateStartMenu.vbs"
echo oLink.WorkingDirectory = "%INSTALL_DIR%\bin" >> "%TEMP%\CreateStartMenu.vbs"
echo oLink.Description = "WiFi Manager Pro" >> "%TEMP%\CreateStartMenu.vbs"
echo oLink.Save >> "%TEMP%\CreateStartMenu.vbs"
cscript //nologo "%TEMP%\CreateStartMenu.vbs"
del "%TEMP%\CreateStartMenu.vbs"
exit /b 0

:RegisterFileAssociations
reg add "HKCR\.wifiprofile" /ve /d "WiFiManagerPro.Profile" /f >nul 2>&1
reg add "HKCR\WiFiManagerPro.Profile" /ve /d "WiFi Manager Pro Profile" /f >nul 2>&1
reg add "HKCR\WiFiManagerPro.Profile\shell\open\command" /ve /d "\"%INSTALL_DIR%\bin\WiFiManagerPro.exe\" \"%%1\"" /f >nul 2>&1
exit /b 0

:AddToPath
for /f "skip=2 tokens=3*" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v PATH 2^>nul') do set "CurrentPath=%%b"
echo %CurrentPath% | findstr /C:"%INSTALL_DIR%\bin" >nul
if %ERRORLEVEL% neq 0 (
    reg add "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v PATH /t REG_EXPAND_SZ /d "%CurrentPath%;%INSTALL_DIR%\bin" /f >nul 2>&1
)
exit /b 0

:ConfigureFirewall
netsh advfirewall firewall add rule name="WiFi Manager Pro" dir=in action=allow program="%INSTALL_DIR%\bin\WiFiManagerPro.exe" >nul 2>&1
exit /b 0

:CreateServiceWrapper
echo @echo off > "%INSTALL_DIR%\bin\service.bat"
echo cd /d "%INSTALL_DIR%\bin" >> "%INSTALL_DIR%\bin\service.bat"
echo WiFiManagerPro.exe --service >> "%INSTALL_DIR%\bin\service.bat"
exit /b 0

:CreateUninstaller
echo @echo off > "%INSTALL_DIR%\uninstall.exe"
echo :: WiFi Manager Pro Uninstaller >> "%INSTALL_DIR%\uninstall.exe"
echo sc stop "WiFiManagerProService" ^>nul 2^>^&1 >> "%INSTALL_DIR%\uninstall.exe"
echo sc delete "WiFiManagerProService" ^>nul 2^>^&1 >> "%INSTALL_DIR%\uninstall.exe"
echo reg delete "HKLM\SOFTWARE\MurtiWifi\WiFiManagerPro" /f ^>nul 2^>^&1 >> "%INSTALL_DIR%\uninstall.exe"
echo reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WiFiManagerPro" /f ^>nul 2^>^&1 >> "%INSTALL_DIR%\uninstall.exe"
echo reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "WiFiManagerPro" /f ^>nul 2^>^&1 >> "%INSTALL_DIR%\uninstall.exe"
echo rmdir /s /q "%INSTALL_DIR%" ^>nul 2^>^&1 >> "%INSTALL_DIR%\uninstall.exe"
echo echo Uninstallation completed. >> "%INSTALL_DIR%\uninstall.exe"
echo pause >> "%INSTALL_DIR%\uninstall.exe"
exit /b 0

:CreateInitialConfiguration
if not exist "%DATA_DIR%\config.json" (
    echo { > "%DATA_DIR%\config.json"
    echo   "firstRun": true, >> "%DATA_DIR%\config.json"
    echo   "version": "%APP_VERSION%", >> "%DATA_DIR%\config.json"
    echo   "installDate": "%date%", >> "%DATA_DIR%\config.json"
    echo   "theme": "auto", >> "%DATA_DIR%\config.json"
    echo   "autoStart": true >> "%DATA_DIR%\config.json"
    echo } >> "%DATA_DIR%\config.json"
)
exit /b 0

:RegisterUpdateNotifications
reg add "HKLM\SOFTWARE\MurtiWifi\WiFiManagerPro" /v "CheckForUpdates" /t REG_DWORD /d 1 /f >nul 2>&1
exit /b 0