@echo off
setlocal
cd /d "%~dp0"
echo ===== VOXLY - budowanie (autor: Marcoxxdev) =====

rem --- .NET SDK: jesli brak, probujemy zainstalowac przez winget ---
where dotnet >nul 2>nul
if not errorlevel 1 goto :hasdotnet
echo Brak .NET SDK. Probuje zainstalowac przez winget...
where winget >nul 2>nul
if errorlevel 1 goto :nodotnet
winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
set "PATH=%PATH%;%ProgramFiles%\dotnet"
where dotnet >nul 2>nul
if errorlevel 1 goto :restart
:hasdotnet

rem --- Inno Setup nie jest czescia programu: sluzy tylko do zbudowania instalatora i NIE jest instalowany automatycznie ---
call :findiscc

:build
if exist dist rmdir /s /q dist

echo [1/3] Budowanie klienta Windows...
dotnet publish Client\VoiceClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist\client
if errorlevel 1 goto :err

echo [2/3] Instalator serwera dla Linuksa...
where bash >nul 2>nul
if errorlevel 1 goto :nobash
bash make-linux-installer.sh
goto :step3
:nobash
echo Brak bash, pomijam. Instalator serwera wygenerujesz w WSL lub Git Bash: ./make-linux-installer.sh

:step3
echo [3/3] Instalator klienta Windows...
if not defined ISCC goto :noiscc
"%ISCC%" Installer\Voxly.iss
if errorlevel 1 goto :err
goto :done

:noiscc
echo Nie znaleziono Inno Setup 6, wiec lokalny instalator klienta pominiety. Wydania z instalatorem buduje GitHub po wypchnieciu tagu.
echo Klient bez instalatora jest w dist\client\Voxly.exe

:done
echo.
echo GOTOWE. Wyniki w folderze dist:
echo   client\Voxly.exe                - program (gotowy do uruchomienia)
echo   Voxly-Setup.exe                 - instalator klienta, tylko jesli masz zainstalowany Inno Setup
echo   voxly-server-install.sh         - instalator serwera Linux (jeden plik)
echo   Voxly-Server-linux.tar.gz       - instalator serwera Linux, w paczce
pause
exit /b 0

:findiscc
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
exit /b 0

:nodotnet
echo Zainstaluj .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0 i uruchom ponownie.
pause
exit /b 1

:restart
echo Zainstalowano .NET SDK. Zamknij to okno i uruchom build-all.bat jeszcze raz.
pause
exit /b 0

:err
echo.
echo BLAD budowania. Przewin okno wyzej i sprawdz komunikaty.
pause
exit /b 1
