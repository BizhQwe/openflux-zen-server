#!/bin/sh
# Prepare GitHub release assets from all seven single-file installers.
set -eu
cd "$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
release_dir=${1:-dist}
if [ ! -d "$release_dir" ]; then
    echo "Release directory not found: $release_dir" >&2
    exit 1
fi

set --
for target in windows-amd64 windows-arm64 windows-386 linux-amd64 linux-arm64 linux-386 linux-armv7; do
    suffix=''
    case "$target" in windows-*) suffix='.exe' ;; esac
    asset="openflux-zen-server-installer-$target$suffix"
    if [ ! -s "$release_dir/$asset" ] || [ ! -s "$release_dir/$asset.sha256" ]; then
        echo "Missing installer or SHA-256: $asset" >&2
        exit 1
    fi
    (cd "$release_dir" && sha256sum --check --status "$asset.sha256") || {
        echo "Installer SHA-256 mismatch: $asset" >&2
        exit 1
    }
    set -- "$@" "$asset"
done

(cd "$release_dir" && sha256sum "$@" > SHA256SUMS.txt)
echo 'Release ready: 7 installers, individual SHA-256 files and SHA256SUMS.txt.'
