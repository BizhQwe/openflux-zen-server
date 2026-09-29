#!/usr/bin/env bash
# ==============================================================================
# OpenFlux Zen Server - Build Pre-compiled Installers & Distribution Packages
# Supports: Linux x64, Linux ARM64, Windows x64, Windows ARM64
# ==============================================================================

set -euo pipefail

# ANSI Colors
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
GRAY='\033[0;90m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Check for .NET SDK
if ! command -v dotnet >/dev/null 2>&1; then
    echo -e "${RED}  [ERROR] .NET SDK (dotnet) is required to build installers.${NC}" >&2
    echo -e "  Please install .NET 10 SDK: https://dotnet.microsoft.com/download" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Targets argument or default
DEFAULT_TARGETS=("win-x64" "win-arm64" "linux-x64" "linux-arm64")
TARGET_INPUT="${1:-${TARGETS:-}}"
OUTPUT_DIR="${2:-${OUTPUT_DIR:-$REPO_ROOT/dist}}"

TARGETS=()
if [ -n "$TARGET_INPUT" ]; then
    IFS=',' read -ra ADDR <<< "$TARGET_INPUT"
    for t in "${ADDR[@]}"; do
        trimmed=$(echo "$t" | xargs)
        if [ -n "$trimmed" ]; then
            TARGETS+=("$trimmed")
        fi
    done
else
    TARGETS=("${DEFAULT_TARGETS[@]}")
fi

echo -e "${CYAN}==================================================================${NC}"
echo -e "${CYAN}  Building OpenFlux Zen Server Pre-compiled Installers${NC}"
echo -e "${CYAN}==================================================================${NC}"
echo "Output Directory: $OUTPUT_DIR"
echo "Targets: ${TARGETS[*]}"
echo ""

# Prepare clean output directory
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

INSTALLER_PROJ_DIR="$REPO_ROOT/src/OpenFlux.Zen.Server.Installer"
WEB_PROJ="$REPO_ROOT/src/OpenFlux.Zen.Server.Web/OpenFlux.Zen.Server.Web.csproj"
CLI_PROJ="$REPO_ROOT/src/OpenFlux.Zen.Server.Cli/OpenFlux.Zen.Server.Cli.csproj"
INSTALLER_PROJ="$INSTALLER_PROJ_DIR/OpenFlux.Zen.Server.Installer.csproj"
RUNTIMES_SRC="$REPO_ROOT/runtimes"

# Helper for zipping a directory
create_zip() {
    local source_dir="$1"
    local dest_zip="$2"

    if command -v zip >/dev/null 2>&1; then
        (cd "$source_dir" && zip -q -r "$dest_zip" .)
    elif command -v python3 >/dev/null 2>&1; then
        python3 -c "import shutil, sys; shutil.make_archive(sys.argv[1][:-4] if sys.argv[1].endswith('.zip') else sys.argv[1], 'zip', sys.argv[2])" "$dest_zip" "$source_dir"
    elif command -v python >/dev/null 2>&1; then
        python -c "import shutil, sys; shutil.make_archive(sys.argv[1][:-4] if sys.argv[1].endswith('.zip') else sys.argv[1], 'zip', sys.argv[2])" "$dest_zip" "$source_dir"
    else
        echo -e "${RED}  [ERROR] Neither 'zip' nor 'python3' was found. Please install zip: sudo apt install zip${NC}" >&2
        return 1
    fi
}

for rid in "${TARGETS[@]}"; do
    echo -e "${YELLOW}------------------------------------------------------------------${NC}"
    echo -e "${YELLOW}>>> Building Target: $rid${NC}"
    echo -e "${YELLOW}------------------------------------------------------------------${NC}"

    TEMP_STAGE="$(mktemp -d "${TMPDIR:-/tmp}/openflux-stage-${rid}-XXXXXX")"
    TEMP_INSTALLER="$OUTPUT_DIR/temp-installer-$rid"
    EMBEDDED_ZIP="$INSTALLER_PROJ_DIR/payload.zip"

    cleanup() {
        rm -rf "$TEMP_STAGE" "$TEMP_INSTALLER" 2>/dev/null || true
        rm -f "$EMBEDDED_ZIP" 2>/dev/null || true
    }
    trap cleanup EXIT

    # 1. Publish Web application
    echo -e "${GRAY}  [1/4] Publishing Web Panel for $rid...${NC}"
    dotnet publish "$WEB_PROJ" -c Release -r "$rid" --self-contained -p:PublishSingleFile=false -o "$TEMP_STAGE" >/dev/null

    # 2. Publish CLI
    echo -e "${GRAY}  [2/4] Publishing CLI for $rid...${NC}"
    dotnet publish "$CLI_PROJ" -c Release -r "$rid" --self-contained -p:PublishSingleFile=false -o "$TEMP_STAGE" >/dev/null

    # 3. Packaging runtimes directory and templates
    # NOTE: OpenFlux core engine is intentionally NOT bundled into the installer.
    # The server automatically downloads the appropriate official engine on startup.
    echo -e "${GRAY}  [3/4] Preparing runtimes structure (core downloaded by server on startup)...${NC}"
    RUNTIMES_DST="$TEMP_STAGE/runtimes"
    mkdir -p "$RUNTIMES_DST"

    # Copy cookie jar templates/seeds if present
    if [ -d "$RUNTIMES_SRC" ]; then
        shopt -s nullglob
        for cookie in "$RUNTIMES_SRC"/cookies-*.json; do
            if [ -f "$cookie" ]; then
                cp -f "$cookie" "$RUNTIMES_DST/"
            fi
        done
        shopt -u nullglob
    fi

    # 4. Pack payload.zip for distribution and embedding
    ZIP_DIST="$OUTPUT_DIR/openflux-zen-server-$rid.zip"
    echo -e "${GRAY}  [4/4] Packing payload archive and building Fat Installer...${NC}"
    create_zip "$TEMP_STAGE" "$ZIP_DIST"

    # Copy payload.zip into Installer project directory for embedded resource build
    cp -f "$ZIP_DIST" "$EMBEDDED_ZIP"

    # Publish Installer single-file binary with embedded payload
    mkdir -p "$TEMP_INSTALLER"
    dotnet publish "$INSTALLER_PROJ" -c Release -r "$rid" --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$TEMP_INSTALLER" >/dev/null

    BINARY_EXT=""
    if [[ "$rid" == win* ]]; then
        BINARY_EXT=".exe"
    fi

    FINAL_INSTALLER_NAME="openflux-installer-$rid$BINARY_EXT"
    BUILT_BINARY="$TEMP_INSTALLER/openflux-installer$BINARY_EXT"
    DEST_BINARY="$OUTPUT_DIR/$FINAL_INSTALLER_NAME"

    mv -f "$BUILT_BINARY" "$DEST_BINARY"
    if [[ "$rid" != win* ]]; then
        chmod +x "$DEST_BINARY" 2>/dev/null || true
    fi

    rm -rf "$TEMP_INSTALLER" "$TEMP_STAGE" "$EMBEDDED_ZIP"

    if [ -f "$DEST_BINARY" ]; then
        SIZE_BYTES=$(wc -c < "$DEST_BINARY")
        SIZE_MB=$(awk "BEGIN {printf \"%.2f\", $SIZE_BYTES/1048576}")
        echo -e "${GREEN}  [OK] Built: $FINAL_INSTALLER_NAME ($SIZE_MB MB)${NC}"
    fi
done

trap - EXIT

echo ""
echo -e "${GREEN}==================================================================${NC}"
echo -e "${GREEN}  Build Completed Successfully!${NC}"
echo -e "${GREEN}==================================================================${NC}"

for f in "$OUTPUT_DIR"/*; do
    if [ -f "$f" ]; then
        FNAME=$(basename "$f")
        SIZE_BYTES=$(wc -c < "$f")
        SIZE_MB=$(awk "BEGIN {printf \"%.2f\", $SIZE_BYTES/1048576}")
        printf "  %-40s %8s MB\n" "$FNAME" "$SIZE_MB"
    fi
done
