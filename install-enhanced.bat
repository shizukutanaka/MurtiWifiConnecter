@echo off
setlocal enabledelayedexpansion

:: MurtiWifi Connector - Enhanced Installation Script
:: Version 2.0.0 - Commercial Release

title MurtiWifi Connector インストーラー

:: Check admin privileges
net file 1>nul 2>nul
if not '%errorlevel%' == '0' (
    echo ========================================
    echo   管理者権限が必要です
    echo ========================================
    echo.
    echo このインストーラーを実行するには管理者権限が必要です。
    echo 右クリックして「管理者として実行」を選択してください。
    echo.
    pause
    exit /b 1
)

echo ========================================
echo   MurtiWifi Connector インストーラー
echo   Version 2.0.0
echo ========================================
echo.

:: Set variables
set "APP_NAME=MurtiWifi Connector"
set "APP_VERSION=2.0.0"
set "INSTALL_DIR=%ProgramFiles%\MurtiWifiConnecter"
set "APPDATA_DIR=%APPDATA%\MurtiWifiConnecter"
set "TEMP_DIR=%TEMP%\MurtiWifiInstaller"
set "START_MENU=%ProgramData%\Microsoft\Windows\Start Menu\Programs"

:: Create temp directory
if not exist "%TEMP_DIR%" mkdir "%TEMP_DIR%"

echo [1/8] システム要件を確認中...

:: Check Windows version
for /f "tokens=4-7 delims=[.] " %%i in ('ver') do (
    if %%i LSS 6 (
        echo ❌ Windows 7 以降が必要です。
        goto :error
    )
    if %%i EQU 6 if %%j LSS 1 (
        echo ❌ Windows 7 SP1 以降が必要です。
        goto :error
    )
)
echo ✓ Windows バージョン: OK

:: Check architecture
if not "%PROCESSOR_ARCHITECTURE%"=="AMD64" (
    if not "%PROCESSOR_ARCHITEW6432%"=="AMD64" (
        echo ❌ 64-bit Windows が必要です。
        goto :error
    )
)
echo ✓ システムアーキテクチャ: OK

:: Check .NET Desktop Runtime
echo [2/8] .NET Runtime を確認中...
dotnet --list-runtimes | findstr "Microsoft.WindowsDesktop.App 6." >nul
if errorlevel 1 (
    echo ❌ .NET 6.0 Desktop Runtime が見つかりません。
    echo.
    echo 以下のURLから .NET 6.0 Desktop Runtime をダウンロードしてインストールしてください:
    echo https://dotnet.microsoft.com/download/dotnet/6.0
    echo.
    echo インストール後、このスクリプトを再実行してください。
    pause
    goto :error
) else (
    echo ✓ .NET 6.0 Desktop Runtime: OK
)

echo [3/8] 既存インストールを確認中...

:: Check if application is running
tasklist /FI "IMAGENAME eq MurtiWifiConnecter.exe" 2>NUL | find /I /N "MurtiWifiConnecter.exe">NUL
if not errorlevel 1 (
    echo アプリケーションが実行中です。終了してください。
    choice /C YN /M "アプリケーションを強制終了しますか？ (Y/N)"
    if errorlevel 2 (
        echo インストールを中止しました。
        goto :error
    )
    taskkill /F /IM MurtiWifiConnecter.exe >nul 2>&1
    timeout /t 2 >nul
    echo ✓ アプリケーションを終了しました
)

:: Remove existing installation
if exist "%INSTALL_DIR%" (
    echo 既存のインストールを削除中...
    rmdir /s /q "%INSTALL_DIR%" 2>nul
    if exist "%INSTALL_DIR%" (
        echo ❌ 既存のインストールを削除できませんでした。
        echo 手動で %INSTALL_DIR% を削除してから再試行してください。
        goto :error
    )
    echo ✓ 既存のインストールを削除しました
)

echo [4/8] アプリケーションファイルをインストール中...

:: Create installation directory
mkdir "%INSTALL_DIR%" 2>nul
if not exist "%INSTALL_DIR%" (
    echo ❌ インストールディレクトリを作成できませんでした: %INSTALL_DIR%
    goto :error
)

:: Copy application files
if exist "bin\Release\net6.0-windows\*" (
    copy /Y "bin\Release\net6.0-windows\*" "%INSTALL_DIR%\" >nul
) else if exist "MurtiWifiConnecter.exe" (
    copy /Y "*.exe" "%INSTALL_DIR%\" >nul
    copy /Y "*.dll" "%INSTALL_DIR%\" >nul 2>nul
    copy /Y "*.json" "%INSTALL_DIR%\" >nul 2>nul
    copy /Y "*.config" "%INSTALL_DIR%\" >nul 2>nul
) else (
    echo ❌ アプリケーションファイルが見つかりません。
    goto :error
)

:: Copy configuration files
copy /Y "default_settings.json" "%INSTALL_DIR%\" >nul 2>nul
copy /Y "family_profiles.json" "%INSTALL_DIR%\" >nul 2>nul

:: Copy documentation
copy /Y "README.md" "%INSTALL_DIR%\" >nul 2>nul
copy /Y "CHANGELOG.md" "%INSTALL_DIR%\" >nul 2>nul

echo ✓ アプリケーションファイルをコピーしました

echo [5/8] ユーザーデータディレクトリを作成中...

:: Create user data directories
if not exist "%APPDATA_DIR%" mkdir "%APPDATA_DIR%"
if not exist "%APPDATA_DIR%\Logs" mkdir "%APPDATA_DIR%\Logs"
if not exist "%APPDATA_DIR%\Profiles" mkdir "%APPDATA_DIR%\Profiles"
if not exist "%APPDATA_DIR%\Backup" mkdir "%APPDATA_DIR%\Backup"

:: Set permissions for user data directory
icacls "%APPDATA_DIR%" /grant Users:F /T >nul 2>&1

echo ✓ ユーザーデータディレクトリを作成しました

echo [6/8] ショートカットを作成中...

:: Create Start Menu shortcuts
if not exist "%START_MENU%\MurtiWifi Connector" mkdir "%START_MENU%\MurtiWifi Connector"

:: Main application shortcut
powershell -command "& {$WshShell = New-Object -comObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%START_MENU%\MurtiWifi Connector\MurtiWifi Connector.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\MurtiWifiConnecter.exe'; $Shortcut.Description = '個人・家庭向け WiFi 管理'; $Shortcut.Save()}"

:: Settings shortcut
powershell -command "& {$WshShell = New-Object -comObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%START_MENU%\MurtiWifi Connector\設定.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\MurtiWifiConnecter.exe'; $Shortcut.Arguments = '--settings'; $Shortcut.Description = '設定画面を開く'; $Shortcut.Save()}"

:: Desktop shortcut (optional)
choice /C YN /M "デスクトップショートカットを作成しますか？ (Y/N)" /T 10 /D Y
if not errorlevel 2 (
    powershell -command "& {$WshShell = New-Object -comObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%USERPROFILE%\Desktop\MurtiWifi Connector.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\MurtiWifiConnecter.exe'; $Shortcut.Description = '個人・家庭向け WiFi 管理'; $Shortcut.Save()}"
    echo ✓ デスクトップショートカットを作成しました
)

echo ✓ ショートカットを作成しました

echo [7/8] レジストリ設定を構成中...

:: Register application
reg add "HKLM\SOFTWARE\MurtiSoft\MurtiWifiConnecter" /v "InstallPath" /t REG_SZ /d "%INSTALL_DIR%" /f >nul
reg add "HKLM\SOFTWARE\MurtiSoft\MurtiWifiConnecter" /v "Version" /t REG_SZ /d "%APP_VERSION%" /f >nul
reg add "HKLM\SOFTWARE\MurtiSoft\MurtiWifiConnecter" /v "Installed" /t REG_DWORD /d 1 /f >nul

:: Add to Add/Remove Programs
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MurtiWifiConnecter" /v "DisplayName" /t REG_SZ /d "%APP_NAME%" /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MurtiWifiConnecter" /v "DisplayVersion" /t REG_SZ /d "%APP_VERSION%" /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MurtiWifiConnecter" /v "Publisher" /t REG_SZ /d "MurtiSoft" /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MurtiWifiConnecter" /v "InstallLocation" /t REG_SZ /d "%INSTALL_DIR%" /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MurtiWifiConnecter" /v "DisplayIcon" /t REG_SZ /d "%INSTALL_DIR%\MurtiWifiConnecter.exe,0" /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MurtiWifiConnecter" /v "UninstallString" /t REG_SZ /d "%INSTALL_DIR%\uninstall.bat" /f >nul

:: Create uninstaller
(
echo @echo off
echo title MurtiWifi Connector アンインストーラー
echo echo MurtiWifi Connector をアンインストールしています...
echo.
echo :: Stop application
echo taskkill /F /IM MurtiWifiConnecter.exe ^>nul 2^>^&1
echo.
echo :: Remove startup entry
echo reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "MurtiWifiConnecter" /f ^>nul 2^>^&1
echo.
echo :: Remove registry entries
echo reg delete "HKLM\SOFTWARE\MurtiSoft\MurtiWifiConnecter" /f ^>nul 2^>^&1
echo reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MurtiWifiConnecter" /f ^>nul 2^>^&1
echo.
echo :: Remove files and directories
echo rmdir /s /q "%INSTALL_DIR%" ^>nul 2^>^&1
echo rmdir /s /q "%%START_MENU%%\MurtiWifi Connector" ^>nul 2^>^&1
echo del "%%USERPROFILE%%\Desktop\MurtiWifi Connector.lnk" ^>nul 2^>^&1
echo.
echo echo アンインストールが完了しました。
echo pause
) > "%INSTALL_DIR%\uninstall.bat"

echo ✓ レジストリ設定を完了しました

:: Startup configuration
choice /C YN /M "Windows起動時に自動実行しますか？ (Y/N)" /T 10 /D Y
if not errorlevel 2 (
    reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "MurtiWifiConnecter" /t REG_SZ /d "\"%INSTALL_DIR%\MurtiWifiConnecter.exe\" --startup" /f >nul
    echo ✓ スタートアップに登録しました
)

echo [8/8] インストール完了処理中...

:: Create first run indicator
echo %date% %time% > "%APPDATA_DIR%\first_run.txt"

:: Windows Firewall exception
echo Windows ファイアウォール例外を作成しています...
netsh advfirewall firewall add rule name="MurtiWifi Connector" dir=in action=allow program="%INSTALL_DIR%\MurtiWifiConnecter.exe" >nul 2>&1

:: Clean up temp directory
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%" >nul 2>&1

echo.
echo ========================================
echo   インストールが完了しました！
echo ========================================
echo.
echo インストール先: %INSTALL_DIR%
echo バージョン: %APP_VERSION%
echo.
echo スタートメニューから「MurtiWifi Connector」を起動できます。
echo 初回起動時には設定ウィザードが表示されます。
echo.

choice /C YN /M "今すぐアプリケーションを起動しますか？ (Y/N)" /T 10 /D Y
if not errorlevel 2 (
    echo アプリケーションを起動しています...
    start "" "%INSTALL_DIR%\MurtiWifiConnecter.exe"
)

echo.
echo インストーラーを終了します。
timeout /t 3 >nul
goto :end

:error
echo.
echo ========================================
echo   インストールに失敗しました
echo ========================================
echo.
echo エラーが発生しました。インストールを中止します。
echo 問題が解決しない場合は、サポートまでお問い合わせください。
echo.
pause
exit /b 1

:end
endlocal
exit /b 0