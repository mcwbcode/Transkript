#!/usr/bin/env bash
# ============================================================
# build_mac.sh  —  Build Transkript for macOS
# ============================================================
# Usage:
#   ./build_mac.sh [arm64|x64|universal]
#
# Requirements:
#   • .NET 8 SDK  (https://dotnet.microsoft.com/download)
#   • Xcode Command Line Tools: xcode-select --install
#
# Output:
#   dist/Transkript.app   — macOS application bundle
# ============================================================

set -euo pipefail

ARCH="${1:-arm64}"   # Default: Apple Silicon
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$SCRIPT_DIR/TranskriptMac.csproj"
DIST="$SCRIPT_DIR/../dist"
APP_NAME="Transkript"
BUNDLE="$DIST/$APP_NAME.app"
VERSION=$(grep '<Version>' "$PROJ" | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/' | tr -d ' ')

echo ""
echo "╔══════════════════════════════════════════╗"
echo "║   Transkript Mac  —  Build v$VERSION     "
echo "╚══════════════════════════════════════════╝"
echo ""

# ── Clean ────────────────────────────────────────────────────────────────────
echo "► Nettoyage…"
rm -rf "$DIST"
mkdir -p "$DIST"

# ── Build ─────────────────────────────────────────────────────────────────────
build_arch() {
    local rid="$1"
    local out="$SCRIPT_DIR/bin/Release/net8.0/$rid/publish"
    echo "► Compilation ($rid)…"
    dotnet publish "$PROJ" \
        --configuration Release \
        --runtime "$rid" \
        --self-contained true \
        --output "$out" \
        -p:PublishSingleFile=false \
        -p:PublishReadyToRun=true \
        -p:TrimmerRootAssembly=Transkript \
        -p:UseAppHost=true
    echo "   ✓ Binaires générés → $out"
}

if [ "$ARCH" = "universal" ]; then
    build_arch "osx-arm64"
    build_arch "osx-x64"
    ARM_OUT="$SCRIPT_DIR/bin/Release/net8.0/osx-arm64/publish"
    X64_OUT="$SCRIPT_DIR/bin/Release/net8.0/osx-x64/publish"
    PUBLISH_DIR="$SCRIPT_DIR/bin/Release/net8.0/osx-universal/publish"
    echo "► Fusion Universal Binary (lipo)…"
    mkdir -p "$PUBLISH_DIR"
    # Copy all files from arm64 first
    cp -R "$ARM_OUT/." "$PUBLISH_DIR/"
    # Lipo merge the main executable and native dylibs
    for f in "$ARM_OUT"/Transkript "$ARM_OUT"/*.dylib; do
        [ -f "$f" ] || continue
        fname=$(basename "$f")
        arm64_f="$ARM_OUT/$fname"
        x64_f="$X64_OUT/$fname"
        dest="$PUBLISH_DIR/$fname"
        if [ -f "$arm64_f" ] && [ -f "$x64_f" ]; then
            lipo -create "$arm64_f" "$x64_f" -output "$dest"
            echo "   lipo: $fname"
        fi
    done
else
    build_arch "osx-$ARCH"
    PUBLISH_DIR="$SCRIPT_DIR/bin/Release/net8.0/osx-$ARCH/publish"
fi

# ── Create .app bundle ────────────────────────────────────────────────────────
echo "► Création du bundle .app…"

CONTENTS="$BUNDLE/Contents"
MACOS="$CONTENTS/MacOS"
RESOURCES="$CONTENTS/Resources"

mkdir -p "$MACOS" "$RESOURCES"

# Copy binaries
cp -R "$PUBLISH_DIR/." "$MACOS/"

# Main executable must be directly in MacOS/ and be executable
chmod +x "$MACOS/Transkript"

# ── Info.plist ───────────────────────────────────────────────────────────────
echo "► Génération de Info.plist…"
cat > "$CONTENTS/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
    "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Transkript</string>
    <key>CFBundleDisplayName</key>
    <string>Transkript</string>
    <key>CFBundleIdentifier</key>
    <string>com.transkript.app</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleExecutable</key>
    <string>Transkript</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleIconFile</key>
    <string>Transkript</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
    <key>NSMicrophoneUsageDescription</key>
    <string>Transkript utilise le microphone pour transcrire votre voix en texte.</string>
    <key>NSAppleEventsUsageDescription</key>
    <string>Transkript utilise l'accessibilité pour coller le texte transcrit.</string>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
PLIST

# ── Helper: generate default icon ────────────────────────────────────────────
generate_default_icon() {
    local dest_dir="$1"
    if ! command -v sips &>/dev/null; then return; fi

    local iconset="$TMPDIR/Transkript.iconset"
    mkdir -p "$iconset"

    # Create a simple PNG with ImageMagick if available, otherwise skip
    if command -v convert &>/dev/null; then
        convert -size 1024x1024 xc:'#0D0D0D' \
            -fill white -font Helvetica-Bold -pointsize 400 \
            -gravity center -annotate 0 "T" \
            "$iconset/icon_1024x1024.png" 2>/dev/null

        for size in 16 32 64 128 256 512 1024; do
            sips -z $size $size "$iconset/icon_1024x1024.png" \
                --out "$iconset/icon_${size}x${size}.png" &>/dev/null
            if [ $size -le 512 ]; then
                local s2=$((size*2))
                sips -z $s2 $s2 "$iconset/icon_1024x1024.png" \
                    --out "$iconset/icon_${size}x${size}@2x.png" &>/dev/null
            fi
        done

        iconutil -c icns "$iconset" -o "$dest_dir/Transkript.icns" 2>/dev/null || true
    fi
}

# ── Icon ─────────────────────────────────────────────────────────────────────
ICON_SRC="$SCRIPT_DIR/Assets/Transkript.icns"
if [ -f "$ICON_SRC" ]; then
    cp "$ICON_SRC" "$RESOURCES/Transkript.icns"
    echo "   ✓ Icône copiée"
else
    echo "   ⚠ Icône non trouvée ($ICON_SRC) — génération d'une icône par défaut…"
    generate_default_icon "$RESOURCES"
fi

# ── Whisper model ────────────────────────────────────────────────────────────
MODEL_FILE="ggml-small.bin"
MODEL_CACHE="$SCRIPT_DIR/Assets/models/$MODEL_FILE"
MODEL_DEST="$MACOS/models/$MODEL_FILE"
mkdir -p "$(dirname "$MODEL_DEST")"

# Check the user-level cache first (fast path — already downloaded)
USER_MODEL="$HOME/Library/Application Support/Transkript/models/$MODEL_FILE"

if [ -f "$MODEL_DEST" ]; then
    echo "   ✓ Modèle déjà dans le bundle"
elif [ -f "$MODEL_CACHE" ]; then
    echo "► Modèle trouvé dans le cache Assets — copie…"
    cp "$MODEL_CACHE" "$MODEL_DEST"
    echo "   ✓ Modèle copié ($(du -sh "$MODEL_DEST" | cut -f1))"
elif [ -f "$USER_MODEL" ]; then
    echo "► Modèle trouvé dans le cache utilisateur — copie…"
    cp "$USER_MODEL" "$MODEL_DEST"
    mkdir -p "$(dirname "$MODEL_CACHE")"
    cp "$USER_MODEL" "$MODEL_CACHE"
    echo "   ✓ Modèle copié ($(du -sh "$MODEL_DEST" | cut -f1))"
else
    echo "► Téléchargement du modèle Whisper Small (~488 Mo)…"
    MODEL_URL="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"
    mkdir -p "$(dirname "$MODEL_CACHE")"
    if curl -L --progress-bar -o "$MODEL_CACHE" "$MODEL_URL"; then
        cp "$MODEL_CACHE" "$MODEL_DEST"
        echo "   ✓ Modèle téléchargé et copié ($(du -sh "$MODEL_DEST" | cut -f1))"
    else
        echo "   ⚠ Téléchargement échoué — le modèle sera téléchargé au premier lancement"
        rm -f "$MODEL_CACHE"
    fi
fi

# ── PkgInfo ───────────────────────────────────────────────────────────────────
printf 'APPL????' > "$CONTENTS/PkgInfo"

# ── Sign (ad-hoc) ─────────────────────────────────────────────────────────────
echo "► Signature ad-hoc (non distribuable via App Store)…"
if command -v codesign &>/dev/null; then
    codesign --force --deep --sign - \
        --entitlements "$SCRIPT_DIR/Transkript.entitlements" \
        "$BUNDLE" 2>&1 | grep -v "replacing existing signature" || true
    echo "   ✓ Signature terminée"
else
    echo "   ⚠ codesign non disponible — ignoré"
fi

echo ""
echo "✅ Bundle créé : $BUNDLE"
echo ""

# ── Quick sanity check ────────────────────────────────────────────────────────
echo "► Vérification du bundle…"
if [ -f "$MACOS/Transkript" ]; then
    echo "   ✓ Exécutable présent"
    file "$MACOS/Transkript"
else
    echo "   ✗ Exécutable MANQUANT — vérifiez la compilation"
    exit 1
fi

echo ""
echo "Pour tester : open \"$BUNDLE\""
echo "Pour créer un DMG  : ./make_installer_mac.sh"
echo ""
