@echo off
setlocal enabledelayedexpansion
title Transkript — Build installateur
cd /d "%~dp0"

echo.
echo  ╔══════════════════════════════════════════╗
echo  ║    Transkript — Build installateur       ║
echo  ╚══════════════════════════════════════════╝
echo.

:: ── 1. Tuer l'instance en cours + nettoyer les modèles ──────────────────────
echo  [1/5] Arret des instances + nettoyage des modeles...
taskkill /f /im Transkript.exe >nul 2>&1
if exist "%APPDATA%\SuperTranscript\models" (
    del /f /q "%APPDATA%\SuperTranscript\models\*.bin" >nul 2>&1
)
echo  OK.
echo.

:: ── 2. Vérifier l'environnement ──────────────────────────────────────────────
echo  [2/5] Verification de l'environnement...

where dotnet >nul 2>&1
if errorlevel 1 (
    echo  [ERREUR] .NET SDK introuvable. Installez .NET 8 SDK : https://dot.net/download
    pause & exit /b 1
)
for /f "tokens=*" %%v in ('dotnet --version 2^>^&1') do echo  .NET SDK : %%v

set ISCC=
if exist "C:\Program Files (x86)\Inno Setup 6\iscc.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\iscc.exe"
if exist "C:\Program Files\Inno Setup 6\iscc.exe"       set "ISCC=C:\Program Files\Inno Setup 6\iscc.exe"
if "%ISCC%"=="" ( where iscc >nul 2>&1 && set "ISCC=iscc" )
if "%ISCC%"=="" (
    echo  [ERREUR] Inno Setup 6 introuvable.
    echo  Telechargez : https://jrsoftware.org/isdl.php
    pause & exit /b 1
)
echo  Inno Setup : %ISCC%
echo.

:: ── 3. Générer app.ico ───────────────────────────────────────────────────────
echo  [3/5] Generation de l'icone...
powershell -NoProfile -ExecutionPolicy Bypass -File generate_icon.ps1 >nul 2>&1
if not exist app.ico (
    echo  [ERREUR] app.ico introuvable apres generation.
    pause & exit /b 1
)
echo  OK.
echo.

:: ── 4. Build .NET ─────────────────────────────────────────────────────────────
echo  [4/5] Build .NET (self-contained win-x64)...
if exist publish rmdir /s /q publish

dotnet publish . ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishTrimmed=false ^
  -o publish ^
  --nologo -v minimal > build_log.txt 2>&1

if errorlevel 1 (
    echo  [ERREUR] Build .NET echoue. Details :
    echo.
    type build_log.txt
    echo.
    pause & exit /b 1
)
type build_log.txt
if not exist publish\Transkript.exe (
    echo  [ERREUR] publish\Transkript.exe introuvable apres build.
    pause & exit /b 1
)
echo  OK : publish\Transkript.exe
echo.

:: ── 5. Compiler l'installateur Inno Setup ────────────────────────────────────
echo  [5/5] Compilation de l'installateur...
if exist publish\Transkript-Setup.exe del /f publish\Transkript-Setup.exe

"%ISCC%" SuperTranscript.iss
if errorlevel 1 (
    echo  [ERREUR] Inno Setup a echoue.
    pause & exit /b 1
)
if not exist publish\Transkript-Setup.exe (
    echo  [ERREUR] Transkript-Setup.exe introuvable apres compilation.
    pause & exit /b 1
)

:: ── Résultat ──────────────────────────────────────────────────────────────────
for %%f in (publish\Transkript-Setup.exe) do set SIZE=%%~zf
set /a SIZE_MB=%SIZE% / 1048576

echo.
echo  ╔══════════════════════════════════════════╗
echo  ║    Installateur pret !                   ║
echo  ║    publish\Transkript-Setup.exe          ║
echo  ║    Taille : %SIZE_MB% Mo
echo  ╚══════════════════════════════════════════╝
echo.

start "" explorer.exe publish
pause
