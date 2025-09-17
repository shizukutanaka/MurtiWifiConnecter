@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ==================================================
:: MurtiWiFiConnecter インストーラー v2.0.0
:: ==================================================

echo.
echo ============================================
echo  MurtiWiFiConnecter インストーラー v2.0.0
echo ============================================
echo.
echo 個人・家庭向けWiFi管理ツール
echo 簡単、安全、軽量な設計
echo.

:: 管理者権限チェック
net session >nul 2>&1
if %errorLevel% == 0 (
    echo ✓ 管理者権限で実行中
) else (
    echo ❌ 管理者権限が必要です
    echo.
    echo 右クリックして「管理者として実行」を選択してください。
    pause
    exit /b 1
)

:: .NET 6.0 ランタイムチェック
echo.
echo === 必要要件の確認 ===
echo .NET 6.0 ランタイムを確認中...

dotnet --version >nul 2>&1
if %errorLevel% == 0 (
    echo ✓ .NET ランタイムが見つかりました
) else (
    echo ❌ .NET 6.0 ランタイムが見つかりません
    echo.
    echo 以下のURLから.NET 6.0 Runtimeをダウンロードしてインストールしてください：
    echo https://dotnet.microsoft.com/download/dotnet/6.0
    echo.
    pause
    exit /b 1
)

:: インストール先ディレクトリ設定
set "INSTALL_DIR=%ProgramFiles%\MurtiWiFiConnecter"
set "DATA_DIR=%APPDATA%\MurtiWiFiConnecter"
set "DESKTOP_SHORTCUT=%PUBLIC%\Desktop\MurtiWiFiConnecter.lnk"
set "STARTMENU_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\MurtiWiFiConnecter.lnk"

echo.
echo === インストール設定 ===
echo インストール先: %INSTALL_DIR%
echo データフォルダ: %DATA_DIR%
echo.

:: インストール確認
choice /C YN /M "インストールを開始しますか？"
if errorlevel 2 (
    echo インストールがキャンセルされました。
    pause
    exit /b 0
)

echo.
echo === インストール開始 ===

:: インストールディレクトリ作成
echo ディレクトリを作成中...
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%"
    if !errorLevel! neq 0 (
        echo ❌ インストールディレクトリの作成に失敗
        goto :error
    )
)

:: データディレクトリ作成
if not exist "%DATA_DIR%" (
    mkdir "%DATA_DIR%"
    mkdir "%DATA_DIR%\Logs"
    mkdir "%DATA_DIR%\Profiles"
    mkdir "%DATA_DIR%\Backups"
    if !errorLevel! neq 0 (
        echo ❌ データディレクトリの作成に失敗
        goto :error
    )
)

:: ファイルのコピー
echo ファイルをコピー中...

:: メイン実行ファイル
if exist "MurtiWifiConnecter.exe" (
    copy "MurtiWifiConnecter.exe" "%INSTALL_DIR%\" >nul
    if !errorLevel! neq 0 (
        echo ❌ 実行ファイルのコピーに失敗
        goto :error
    )
    echo ✓ MurtiWifiConnecter.exe
) else (
    echo ❌ MurtiWifiConnecter.exe が見つかりません
    echo ビルドされた実行ファイルが必要です
    goto :error
)

:: DLLファイル
for %%f in (*.dll) do (
    copy "%%f" "%INSTALL_DIR%\" >nul
    echo ✓ %%f
)

:: 設定ファイル
if exist "default_settings.json" (
    copy "default_settings.json" "%DATA_DIR%\settings.json" >nul
    echo ✓ デフォルト設定
)

if exist "family_profiles.json" (
    copy "family_profiles.json" "%DATA_DIR%\profiles.json" >nul
    echo ✓ 家族プロファイル
)

:: ドキュメント
if exist "README.md" (
    copy "README.md" "%INSTALL_DIR%\" >nul
    echo ✓ README.md
)

:: ショートカット作成
echo.
echo ショートカットを作成中...

:: PowerShellでショートカット作成
powershell -Command "& {$WshShell = New-Object -comObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%DESKTOP_SHORTCUT%'); $Shortcut.TargetPath = '%INSTALL_DIR%\MurtiWifiConnecter.exe'; $Shortcut.WorkingDirectory = '%INSTALL_DIR%'; $Shortcut.Description = 'MurtiWiFiConnecter - 個人向けWiFi管理ツール'; $Shortcut.Save()}"

powershell -Command "& {$WshShell = New-Object -comObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%STARTMENU_SHORTCUT%'); $Shortcut.TargetPath = '%INSTALL_DIR%\MurtiWifiConnecter.exe'; $Shortcut.WorkingDirectory = '%INSTALL_DIR%'; $Shortcut.Description = 'MurtiWiFiConnecter - 個人向けWiFi管理ツール'; $Shortcut.Save()}"

if exist "%DESKTOP_SHORTCUT%" (
    echo ✓ デスクトップショートカット
) else (
    echo ⚠ デスクトップショートカットの作成に失敗
)

if exist "%STARTMENU_SHORTCUT%" (
    echo ✓ スタートメニューショートカット
) else (
    echo ⚠ スタートメニューショートカットの作成に失敗
)

:: Windows Defender除外設定（オプション）
echo.
choice /C YN /M "Windows Defender除外設定を追加しますか？（推奨）"
if not errorlevel 2 (
    echo Windows Defender除外設定を追加中...
    powershell -Command "Add-MpPreference -ExclusionPath '%INSTALL_DIR%'" 2>nul
    if !errorLevel! == 0 (
        echo ✓ Defender除外設定完了
    ) else (
        echo ⚠ Defender除外設定に失敗（手動で設定してください）
    )
)

:: 自動起動設定（オプション）
echo.
choice /C YN /M "Windows起動時に自動開始しますか？"
if not errorlevel 2 (
    echo 自動起動を設定中...
    reg add "HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run" /v "MurtiWiFiConnecter" /t REG_SZ /d "\"%INSTALL_DIR%\MurtiWifiConnecter.exe\" --minimized" /f >nul
    if !errorLevel! == 0 (
        echo ✓ 自動起動設定完了
    ) else (
        echo ⚠ 自動起動設定に失敗
    )
)

:: インストール完了
echo.
echo ============================================
echo  インストール完了！
echo ============================================
echo.
echo インストール場所: %INSTALL_DIR%
echo データフォルダ: %DATA_DIR%
echo.
echo デスクトップまたはスタートメニューから起動できます。
echo 初回起動時に3ステップの簡単セットアップが開始されます。
echo.

:: 初回起動確認
choice /C YN /M "今すぐ起動しますか？"
if not errorlevel 2 (
    echo.
    echo MurtiWiFiConnecter を起動中...
    start "" "%INSTALL_DIR%\MurtiWifiConnecter.exe"
)

echo.
echo インストールが完了しました。
echo 家族みんなで安全なWiFiライフをお楽しみください！
echo.
pause
exit /b 0

:error
echo.
echo ❌ インストール中にエラーが発生しました
echo 管理者権限で実行していることを確認してください
echo.
pause
exit /b 1