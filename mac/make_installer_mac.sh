#!/usr/bin/env bash
# ============================================================
# make_installer_mac.sh  —  Create a clean DMG installer for Transkript
# ============================================================
# Usage:
#   ./make_installer_mac.sh [arm64|x64|universal]
#
# Requirements:
#   • build_mac.sh must have been run first  (produces dist/Transkript.app)
#   • create-dmg (optional but recommended):
#       brew install create-dmg
#   • hdiutil (built into macOS — used as fallback)
#
# Output:
#   dist/Transkript-{version}-{arch}.dmg
# ============================================================

set -euo pipefail

ARCH="${1:-arm64}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST="$SCRIPT_DIR/../dist"
BUNDLE="$DIST/Transkript.app"
PROJ="$SCRIPT_DIR/TranskriptMac.csproj"
VERSION=$(grep '<Version>' "$PROJ" | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/' | tr -d ' ')
DMG_NAME="Transkript-${VERSION}-${ARCH}.dmg"
DMG_PATH="$DIST/$DMG_NAME"

echo ""
echo "╔══════════════════════════════════════════════════╗"
echo "║   Transkript Mac  —  Création du DMG v$VERSION  "
echo "╚══════════════════════════════════════════════════╝"
echo ""

# ── Pre-flight ───────────────────────────────────────────────────────────────
if [ ! -d "$BUNDLE" ]; then
    echo "✗ $BUNDLE introuvable — lancez d'abord ./build_mac.sh"
    exit 1
fi

rm -f "$DMG_PATH"

# ── Create DMG ────────────────────────────────────────────────────────────────
if command -v create-dmg &>/dev/null; then
    echo "► Création du DMG avec create-dmg (fenêtre stylisée)…"

    # Background image (optional — create a simple one if missing)
    BACKGROUND=""
    if [ -f "$SCRIPT_DIR/Assets/dmg_background.png" ]; then
        BACKGROUND="--background $SCRIPT_DIR/Assets/dmg_background.png"
    fi

    create-dmg \
        --volname "Transkript $VERSION" \
        --volicon "$BUNDLE/Contents/Resources/Transkript.icns" \
        --window-pos 200 140 \
        --window-size 660 400 \
        --icon-size 128 \
        --icon "Transkript.app" 180 180 \
        --hide-extension "Transkript.app" \
        --app-drop-link 480 180 \
        $BACKGROUND \
        "$DMG_PATH" \
        "$DIST/" 2>&1 | grep -v "^$" || true

else
    echo "► create-dmg non installé — utilisation de hdiutil (DMG simple)…"
    echo "  (Pour un DMG avec fenêtre stylisée : brew install create-dmg)"
    echo ""

    # Stage directory
    STAGE=$(mktemp -d)
    cp -R "$BUNDLE" "$STAGE/"
    # Add Applications symlink
    ln -s /Applications "$STAGE/Applications"

    # Calculate size needed (in MB), add 20% buffer
    SIZE_KB=$(du -sk "$STAGE" | cut -f1)
    SIZE_MB=$(( (SIZE_KB / 1024) * 12 / 10 + 20 ))

    echo "► hdiutil createfs (taille estimée : ${SIZE_MB} Mo)…"

    # Create a read-write DMG
    TEMP_DMG="$DIST/tmp_rw.dmg"
    hdiutil create \
        -srcfolder "$STAGE" \
        -volname "Transkript $VERSION" \
        -fs HFS+ \
        -fsargs "-c c=64,a=16,b=16" \
        -format UDRW \
        -size "${SIZE_MB}m" \
        "$TEMP_DMG"

    # Mount it
    MOUNT_DIR=$(mktemp -d)
    DEVICE=$(hdiutil attach -readwrite -noverify -noautoopen "$TEMP_DMG" | \
        egrep '^/dev/' | sed 1q | awk '{print $1}')

    # Unmount
    sync
    hdiutil detach "$DEVICE"
    rm -rf "$MOUNT_DIR"

    # Convert to compressed, read-only DMG
    hdiutil convert "$TEMP_DMG" \
        -format UDZO \
        -imagekey zlib-level=9 \
        -o "$DMG_PATH"

    rm -f "$TEMP_DMG"
    rm -rf "$STAGE"
fi

echo ""
echo "✅ DMG créé : $DMG_PATH"
echo ""

# ── Verify ───────────────────────────────────────────────────────────────────
echo "► Vérification du DMG…"
hdiutil verify "$DMG_PATH" 2>&1 | tail -1
SIZE=$(du -sh "$DMG_PATH" | cut -f1)
echo "   Taille : $SIZE"

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  Installation :"
echo "  1. Ouvrez $DMG_NAME"
echo "  2. Glissez Transkript.app dans Applications"
echo "  3. Ouvrez Transkript depuis le Launchpad"
echo "     (Ctrl+clic → Ouvrir pour la 1ère fois)"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
