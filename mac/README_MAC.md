# Transkript — Version Mac

Port macOS de l'application Windows, construit avec **Avalonia UI** et les APIs natives macOS.

## Prérequis

- **macOS 12+** (Monterey ou supérieur)
- **.NET 8 SDK** : https://dotnet.microsoft.com/download/dotnet/8.0
- **Xcode Command Line Tools** : `xcode-select --install`
- **create-dmg** (optionnel, pour DMG stylisé) : `brew install create-dmg`

## Structure du projet

```
mac/
├── TranskriptMac.csproj     # Projet Avalonia (.NET 8, osx-arm64/x64)
├── App.axaml / App.axaml.cs # Point d'entrée Avalonia
├── Platform/
│   ├── AudioRecorderMac.cs  # Capture audio via CoreAudio/AudioQueue
│   ├── GlobalHotkeyMac.cs   # Raccourcis globaux via Carbon API
│   ├── MenuBarMac.cs        # Icône menu bar via Avalonia TrayIcon
│   └── PasteHelperMac.cs    # Coller via NSPasteboard + CGEvent (Cmd+V)
├── Views/
│   ├── LoginWindow.axaml    # Fenêtre de connexion
│   ├── HomeWindow.axaml     # Fenêtre d'accueil
│   ├── SettingsWindow.axaml # Fenêtre paramètres
│   └── OverlayWindow.axaml  # Overlay d'enregistrement (pill flottante)
├── AuthService.cs           # Authentification Supabase
├── AppSettings.cs           # Paramètres (~/Library/Application Support/Transkript/)
├── Transcriber.cs           # Whisper.net (CPU sur Mac)
├── TextProcessor.cs         # Traitement du texte
├── HistoryManager.cs        # Historique des dictées
├── Logger.cs                # Logs
├── UpdateChecker.cs         # Vérification des mises à jour
├── Assets/
│   └── Transkript.icns      # Icône macOS (à placer ici)
├── build_mac.sh             # Script de build
├── make_installer_mac.sh    # Créateur de DMG
└── Transkript.entitlements  # Permissions macOS
```

## Compilation

### Build standard (Apple Silicon)
```bash
cd mac
./build_mac.sh arm64
```

### Build Intel
```bash
./build_mac.sh x64
```

### Build Universal (arm64 + x64)
```bash
./build_mac.sh universal
```

Le bundle `.app` est généré dans `dist/Transkript.app`.

## Créer l'installateur DMG

```bash
./make_installer_mac.sh arm64
```

Génère `dist/Transkript-1.0.0-arm64.dmg`.

## Premiers paramètres au lancement

L'app tourne dans la **barre des menus** (icône menu bar). Pas de fenêtre principale.

Au premier lancement :
1. Connectez-vous avec votre compte (plan Pro requis)
2. Le modèle Whisper small (~488 Mo) est téléchargé automatiquement
3. Maintenez **F13** (ou la touche configurée) pour dicter

## Permissions requises

- **Microphone** : demandé au premier enregistrement
- **Accessibilité** : pour simuler Cmd+V (Préférences Système → Sécurité → Accessibilité)

## Différences avec la version Windows

| Fonctionnalité | Windows | Mac |
|---------------|---------|-----|
| Framework UI  | WPF/XAML | Avalonia UI |
| Audio         | NAudio/WaveIn | CoreAudio/AudioQueue |
| Raccourci global | WH_KEYBOARD_LL | Carbon RegisterEventHotKey |
| Barre système | NotifyIcon (systray) | NSStatusItem (menu bar) |
| Coller texte  | keybd_event (Ctrl+V) | CGEvent (Cmd+V) |
| Stockage      | %APPDATA%\SuperTranscript\ | ~/Library/Application Support/Transkript/ |
| GPU           | CUDA (si dispo) | CPU (Metal auto via Whisper.net) |
| Installateur  | Inno Setup (.exe) | DMG (.dmg) |

## Données locales

Tout est stocké dans `~/Library/Application Support/Transkript/` :
- `auth.json` — session Supabase
- `settings.json` — préférences
- `history.jsonl` — historique des dictées
- `models/ggml-small.bin` — modèle Whisper (~488 Mo)
- `logs/transkript.log` — journal d'activité
