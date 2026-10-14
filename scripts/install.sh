#!/bin/sh
# OpenFlux Zen Server: download and run the latest Linux installer.
# All arguments are passed to the installer, e.g. --no-service --dir ./zen.
set -eu

repository=${OPENFLUX_REPOSITORY:-BizhQwe/openflux-zen-server}
case "$repository" in
    ''|*[!A-Za-z0-9_./-]*) echo 'OPENFLUX_REPOSITORY must be a GitHub owner/repository.' >&2; exit 1 ;;
esac
owner=${repository%%/*}
repo=${repository#*/}
case "$repo" in
    ''|*/*) echo 'OPENFLUX_REPOSITORY must be a GitHub owner/repository.' >&2; exit 1 ;;
esac
if [ -z "$owner" ] || [ "$owner" = "$repository" ]; then
    echo 'OPENFLUX_REPOSITORY must be a GitHub owner/repository.' >&2
    exit 1
fi
if [ "$(uname -s)" != Linux ]; then
    echo 'This script supports Linux only.' >&2
    exit 1
fi
machine=$(uname -m)
case "$machine" in
    x86_64|amd64) arch=amd64 ;;
    aarch64|arm64) arch=arm64 ;;
    i386|i486|i586|i686|x86) arch=386 ;;
    armv7l|armv8l) arch=armv7 ;;
    *) echo "Unsupported Linux architecture: $machine" >&2; exit 1 ;;
esac
if ! command -v curl >/dev/null 2>&1 && ! command -v wget >/dev/null 2>&1; then
    echo 'Install curl or wget and run this script again.' >&2
    exit 1
fi
if ! command -v sha256sum >/dev/null 2>&1; then
    echo 'sha256sum is required to verify the installer.' >&2
    exit 1
fi

asset="openflux-zen-server-installer-linux-$arch"
release_url="https://github.com/$repository/releases/latest/download/$asset"
temp_dir=$(mktemp -d "${TMPDIR:-/tmp}/openflux-install.XXXXXX")
installer="$temp_dir/$asset"
checksum="$installer.sha256"
cleanup() {
    rm -f -- "$installer" "$checksum"
    rmdir -- "$temp_dir" 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM
download() {
    if command -v curl >/dev/null 2>&1; then
        curl --fail --location --show-error --silent --retry 3 --connect-timeout 20 "$1" --output "$2"
    else
        wget --quiet --timeout=20 --tries=3 --output-document="$2" "$1"
    fi
}
echo "OpenFlux Zen Server - Linux $arch"
echo "Downloading the latest release from $repository..."
if ! download "$release_url" "$installer" || ! download "$release_url.sha256" "$checksum"; then
    echo "Could not download the installer and checksum: $release_url" >&2
    exit 1
fi
expected=$(awk 'NR == 1 { print $1; exit }' "$checksum")
case "$expected" in
    ''|*[!a-fA-F0-9]*) echo 'Invalid release SHA-256 file.' >&2; exit 1 ;;
esac
if [ "${#expected}" -ne 64 ]; then
    echo 'Invalid release SHA-256 file.' >&2
    exit 1
fi
expected=$(printf '%s' "$expected" | tr 'A-F' 'a-f')
actual=$(sha256sum "$installer" | awk '{ print $1 }')
if [ "$actual" != "$expected" ]; then
    echo 'Installer SHA-256 mismatch.' >&2
    exit 1
fi
chmod 700 "$installer"
echo 'SHA-256 verified. Starting installation...'

needs_root=true
for arg in "$@"; do
    case "$arg" in --no-service|--verify) needs_root=false ;; esac
done
if [ "$(id -u)" -ne 0 ] && [ "$needs_root" = true ]; then
    if ! command -v sudo >/dev/null 2>&1; then
        echo 'Run this script as root, or use --no-service --dir <directory>.' >&2
        exit 1
    fi
    sudo "$installer" "$@"
else
    "$installer" "$@"
fi
