@echo off
setlocal enabledelayedexpansion

:: MurtiWifiConnecter インストーラー
title MurtiWifiConnecter インストーラー

echo ================================================
echo  MurtiWifiConnecter - 家族向けWiFi管理ツール
echo ================================================
echo.

:: 管理者権限チェック
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo エラー: このインストーラーは管理者権限で実行してください。
    echo install.bat を右クリックして「管理者として実行」を選択してください。
    echo.
    pause
    exit /b 1
)

:: インストール先の選択
set "INSTALL_PATH=C:\Program Files\MurtiWifiConnecter"
echo デフォルトインストール先: %INSTALL_PATH%
echo 別の場所を指定しますか? (y/N): 
set /p CHANGE_PATH=
if /i "!CHANGE_PATH!"=="y" (
    echo インストール先を入力してください:
    set /p INSTALL_PATH=
)

:: インストール先ディレクトリ作成
echo.
echo インストール先を準備中: %INSTALL_PATH%
if not exist "%INSTALL_PATH%" (
    mkdir "%INSTALL_PATH%" 2>nul
    if !errorlevel! neq 0 (
        echo エラー: インストール先ディレクトリを作成できませんでした。
        pause
        exit /b 1
    )
)

:: ファイルコピー
echo ファイルをコピー中...
xcopy "bin\*" "%INSTALL_PATH%\" /E /I /Y >nul 2>&1
if !errorlevel! neq 0 (
    echo エラー: ファイルのコピーに失敗しました。
    pause
    exit /b 1
)

xcopy "config\*" "%INSTALL_PATH%\config\" /E /I /Y >nul 2>&1
if !errorlevel! neq 0 (
    echo 警告: 設定ファイルのコピーに失敗しました。
)

:: デスクトップショートカット作成
echo ショートカット作成中...
set "SHORTCUT_PATH=%USERPROFILE%\Desktop\MurtiWifiConnecter.lnk"
powershell -Command "& {$WshShell = New-Object -comObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%SHORTCUT_PATH%'); $Shortcut.TargetPath = '%INSTALL_PATH%\MurtiWifiConnecter.exe'; $Shortcut.WorkingDirectory = '%INSTALL_PATH%'; $Shortcut.Description = '家族向けWiFi管理ツール'; $Shortcut.Save()}" >nul 2>&1

:: スタートメニューショートカット作成
set "START_PATH=%APPDATA%\Microsoft\Windows\Start Menu\Programs\MurtiWifiConnecter.lnk"
powershell -Command "& {$WshShell = New-Object -comObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%START_PATH%'); $Shortcut.TargetPath = '%INSTALL_PATH%\MurtiWifiConnecter.exe'; $Shortcut.WorkingDirectory = '%INSTALL_PATH%'; $Shortcut.Description = '家族向けWiFi管理ツール'; $Shortcut.Save()}" >nul 2>&1

:: .NET Runtime チェック
echo.
echo .NET Runtime 6.0 の確認中...
dotnet --version >nul 2>&1
if !errorlevel! neq 0 (
    echo 警告: .NET 6.0 Runtime が見つかりません。
    echo Microsoft公式サイトから.NET 6.0 Runtimeをダウンロードしてインストールしてください。
    echo https://dotnet.microsoft.com/download/dotnet/6.0
    echo.
) else (
    echo .NET Runtime が確認されました。
)

:: インストール完了
echo.
echo ================================================
echo  インストールが完了しました！
echo ================================================
echo.
echo インストール場所: %INSTALL_PATH%
echo デスクトップショートカット: 作成済み
echo スタートメニュー: 作成済み
echo.
echo 今すぐ起動しますか? (Y/n): 
set /p START_NOW=
if /i not "!START_NOW!"=="n" (
    echo アプリケーションを起動中...
    start "" "%INSTALL_PATH%\MurtiWifiConnecter.exe"
)

echo.
echo インストールが正常に完了しました。
echo ご利用ありがとうございます！
echo.
pause