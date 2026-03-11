@echo off
setlocal
title Transkript — Build
cd /d "%~dp0"

echo.
echo  ╔═══════════════════════════════════╗
echo  ║      Transkript — Build dev       ║
echo  ╚═══════════════════════════════════╝
echo.

:: ── Tuer l'instance en cours ─────────────────────────────────────────────────
taskkill /f /im Transkript.exe >nul 2>&1
echo  [1/4] Processus arretes.

:: ── Supprimer les modèles en cache (forcer re-téléchargement propre) ──────────
if exist "%APPDATA%\SuperTranscript\models" (
    del /f /q "%APPDATA%\SuperTranscript\models\*.bin" >nul 2>&1
    echo  Modeles supprimes.
)

:: ── Vérifier .NET SDK ────────────────────────────────────────────────────────
where dotnet >nul 2>&1
if errorlevel 1 (
    echo  [ERREUR] .NET SDK introuvable. Installez .NET 8 SDK : https://dot.net/download
    pause & exit /b 1
)

:: ── Générer app.ico ──────────────────────────────────────────────────────────
echo  [2/4] Generation de l'icone...
powershell -NoProfile -ExecutionPolicy Bypass -File generate_icon.ps1 >nul 2>&1
if not exist app.ico (
    echo  [ERREUR] app.ico introuvable apres generation.
    pause & exit /b 1
)

:: ── Nettoyer et publier ───────────────────────────────────────────────────────
echo  [3/4] Build (self-contained win-x64)...
if exist publish rmdir /s /q publish

dotnet publish . -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o publish --nologo -v minimal > build_log.txt 2>&1
if errorlevel 1 (
    echo  [ERREUR] Build echoue. Details dans build_log.txt :
    echo.
    type build_log.txt
    echo.
    pause & exit /b 1
)
type build_log.txt

:: ── Vérifier et lancer ───────────────────────────────────────────────────────
echo  [4/4] Verification...
if not exist publish\Transkript.exe (
    echo  [ERREUR] publish\Transkript.exe introuvable.
    pause & exit /b 1
)

echo.
echo  ╔═══════════════════════════════════╗
echo  ║    Build OK — lancement...        ║
echo  ╚═══════════════════════════════════╝
echo.

echo  Lancement de l'application...
start "" publish\Transkript.exe
echo  Application demarree. Appuie sur une touche pour fermer cette fenetre.
pause
