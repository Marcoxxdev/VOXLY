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

:build
if exist dist rmdir /s /q dist

echo [1/2] Budowanie klienta Windows...
dotnet publish Client\VoiceClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist\client
if errorlevel 1 goto :err

echo [2/2] Instalator serwera dla Linuksa...
where bash >nul 2>nul
if errorlevel 1 goto :nobash
bash make-linux-installer.sh
goto :done
:nobash
echo Brak bash, pomijam. Instalator serwera wygenerujesz w WSL lub Git Bash: ./make-linux-installer.sh

:done
echo.
echo GOTOWE. Wyniki w folderze dist:
echo   client\Voxly.exe                - program (gotowy do uruchomienia)
echo   voxly-server-install.sh         - instalator serwera Linux (jeden plik)
echo   Voxly-Server-linux.tar.gz       - instalator serwera Linux, w paczce
pause
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
