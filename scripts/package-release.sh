#!/bin/sh
# Build and verify the complete release set; --skip-build checks existing assets.
set -eu
cd "$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
release_dir=dist
skip_build=0
directory_set=0
while [ "$#" -gt 0 ]; do
    case "$1" in
        --skip-build) skip_build=1 ;;
        -h|--help)
            echo 'Usage: sh scripts/package-release.sh [--skip-build] [release-directory]'
            echo 'Builds all targets by default. --skip-build verifies previously built installers.'
            exit 0 ;;
        -*) echo "Unknown option: $1" >&2; exit 1 ;;
        *)
            [ "$directory_set" -eq 0 ] || { echo 'Only one release directory is allowed.' >&2; exit 1; }
            release_dir=$1
            directory_set=1 ;;
    esac
    shift
done

command -v sha256sum >/dev/null 2>&1 || { echo 'sha256sum is required.' >&2; exit 1; }
if [ "$skip_build" -eq 0 ]; then
    sh scripts/build.sh
    mkdir -p "$release_dir"
    input_dir=dist
else
    input_dir=$release_dir
fi
if [ ! -d "$release_dir" ]; then
    echo "Release directory not found: $release_dir" >&2
    exit 1
fi
input_dir=$(CDPATH= cd -- "$input_dir" && pwd)
release_dir=$(CDPATH= cd -- "$release_dir" && pwd)
targets=$(tr -d '\r' < scripts/targets.txt)

set --
for target in $targets; do
    suffix=''
    case "$target" in windows-*) suffix='.exe' ;; esac
    asset="openflux-zen-server-installer-$target$suffix"
    if [ ! -s "$input_dir/$asset" ] || [ ! -s "$input_dir/$asset.sha256" ]; then
        echo "Missing installer or SHA-256: $asset" >&2
        exit 1
    fi
    expected_hash=''
    expected_file=''
    read -r expected_hash expected_file < "$input_dir/$asset.sha256" || [ -n "$expected_hash" ]
    expected_file=$(printf '%s' "$expected_file" | tr -d '\r')
    case "$expected_file" in "$asset"|"*$asset") ;; *) echo "Invalid SHA-256 filename: $asset" >&2; exit 1 ;; esac
    (cd "$input_dir" && printf '%s *%s\n' "$expected_hash" "$asset" | sha256sum --check --status) || {
        echo "Installer SHA-256 mismatch: $asset" >&2
        exit 1
    }
    set -- "$@" "$asset"
done

# Verify every input before copying, so an incomplete set cannot look ready.
if [ "$input_dir" != "$release_dir" ]; then
    for asset in "$@"; do
        cp -p "$input_dir/$asset" "$release_dir/$asset"
        cp -p "$input_dir/$asset.sha256" "$release_dir/$asset.sha256"
    done
fi
installer_count=$#
for bootstrap in install.sh install.ps1; do
    cp "scripts/$bootstrap" "$release_dir/$bootstrap"
    (cd "$release_dir" && sha256sum "$bootstrap" > "$bootstrap.sha256")
    set -- "$@" "$bootstrap"
done
(cd "$release_dir" && sha256sum "$@" > SHA256SUMS.txt)
echo "Release ready: $installer_count installers, bootstrap scripts, individual SHA-256 files and SHA256SUMS.txt in $release_dir."
