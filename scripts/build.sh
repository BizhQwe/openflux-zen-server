#!/bin/sh
set -eu
cd "$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
all_targets=$(tr -d '\r' < scripts/targets.txt)
targets="${*:-$all_targets}"
if [ "$targets" = "--all" ]; then
    targets=$all_targets
fi
for target in $targets; do
    case " $(printf '%s' "$all_targets" | tr '\n' ' ') " in
        *" $target "*) ;;
        *) echo "Unsupported target: $target" >&2; exit 1 ;;
    esac
done
command -v sha256sum >/dev/null 2>&1 || { echo 'sha256sum is required.' >&2; exit 1; }
go=${GO:-go}
host_os=$("$go" env GOHOSTOS)
host_arch=$("$go" env GOHOSTARCH)
mkdir -p dist
bundle=src/OpenFlux.Zen.Server.Installer/payload/bundle.zip
trap 'rm -f "$bundle"' EXIT HUP INT TERM
for target in $targets; do
    os=${target%%-*}
    arch=${target#*-}
    arm=""
    [ "$arch" != armv7 ] || { arch=arm; arm=7; }
    suffix=""
    [ "$os" != windows ] || suffix=.exe
    bin="dist/$target"
    mkdir -p "$bin"
    echo "Building $target"
    GOOS="$os" GOARCH="$arch" GOARM="$arm" CGO_ENABLED=0 "$go" build -trimpath -ldflags='-s -w' -o "$bin/openflux-zen-server-core$suffix" ./src/OpenFlux.Zen.Server.Core
    GOOS="$os" GOARCH="$arch" GOARM="$arm" CGO_ENABLED=0 "$go" build -trimpath -ldflags='-s -w' -o "$bin/openflux-zen-server-web$suffix" ./src/OpenFlux.Zen.Server.Web
    GOOS="$os" GOARCH="$arch" GOARM="$arm" CGO_ENABLED=0 "$go" build -trimpath -ldflags='-s -w' -o "$bin/openflux-zen-server-cli$suffix" ./src/OpenFlux.Zen.Server.Cli
    GOOS="$host_os" GOARCH="$host_arch" GOARM='' CGO_ENABLED=0 "$go" run ./src/OpenFlux.Zen.Server.Installer/cmd/bundle --os "$os" --arch "$arch" --dir "$bin" --out "$bundle"
    installer="dist/openflux-zen-server-installer-$target$suffix"
    GOOS="$os" GOARCH="$arch" GOARM="$arm" CGO_ENABLED=0 "$go" build -trimpath -ldflags='-s -w' -o "$installer" ./src/OpenFlux.Zen.Server.Installer
    (cd dist && sha256sum "$(basename "$installer")") > "$installer.sha256"
    rm -f "$bin/openflux-core$suffix" "$bin/openflux-zen-server$suffix" "$bin/openflux$suffix"
done
