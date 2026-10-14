#!/bin/sh
set -eu
cd "$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
targets="${*:-linux-amd64}"
if [ "$targets" = "--all" ]; then
    targets="windows-amd64 windows-arm64 windows-386 linux-amd64 linux-arm64 linux-386 linux-armv7"
fi
mkdir -p dist
bundle=src/OpenFlux.Zen.Server.Installer/payload/bundle.zip
trap 'rm -f "$bundle"' EXIT HUP INT TERM
for target in $targets; do
    case "$target" in windows-amd64|windows-arm64|windows-386|linux-amd64|linux-arm64|linux-386|linux-armv7) ;; *) echo "Unsupported target: $target" >&2; exit 1;; esac
    os=${target%%-*}
    arch=${target#*-}
    arm=""
    [ "$arch" != armv7 ] || { arch=arm; arm=7; }
    suffix=""
    [ "$os" != windows ] || suffix=.exe
    bin="dist/$target"
    mkdir -p "$bin"
    echo "Building $target"
    GOOS="$os" GOARCH="$arch" GOARM="$arm" CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o "$bin/openflux-zen-server-core$suffix" ./src/OpenFlux.Zen.Server.Core
    GOOS="$os" GOARCH="$arch" GOARM="$arm" CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o "$bin/openflux-zen-server-web$suffix" ./src/OpenFlux.Zen.Server.Web
    GOOS="$os" GOARCH="$arch" GOARM="$arm" CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o "$bin/openflux-zen-server-cli$suffix" ./src/OpenFlux.Zen.Server.Cli
    go run ./src/OpenFlux.Zen.Server.Installer/cmd/bundle --os "$os" --arch "$arch" --dir "$bin" --out "$bundle"
    installer="dist/openflux-zen-server-installer-$target$suffix"
    GOOS="$os" GOARCH="$arch" GOARM="$arm" CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o "$installer" ./src/OpenFlux.Zen.Server.Installer
    if command -v sha256sum >/dev/null 2>&1; then (cd dist && sha256sum "$(basename "$installer")") > "$installer.sha256"; fi
    rm -f "$bin/openflux-core$suffix" "$bin/openflux-zen-server$suffix" "$bin/openflux$suffix"
done
