#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="/opt/openflux-zen-server"
if [ ! -d "$APP_DIR" ]; then
    APP_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
fi

if [ -f "$APP_DIR/OpenFlux.Zen.Server" ]; then
    "$APP_DIR/OpenFlux.Zen.Server" "$@"
elif [ -f "$APP_DIR/OpenFlux.Zen.Server.dll" ]; then
    dotnet "$APP_DIR/OpenFlux.Zen.Server.dll" "$@"
elif [ -f "$APP_DIR/publish/OpenFlux.Zen.Server.dll" ]; then
    dotnet "$APP_DIR/publish/OpenFlux.Zen.Server.dll" "$@"
elif [ -f "$APP_DIR/src/OpenFlux.Zen.Server/bin/Release/net10.0/OpenFlux.Zen.Server.dll" ]; then
    dotnet "$APP_DIR/src/OpenFlux.Zen.Server/bin/Release/net10.0/OpenFlux.Zen.Server.dll" "$@"
else
    dotnet "$APP_DIR/src/OpenFlux.Zen.Server/bin/Debug/net10.0/OpenFlux.Zen.Server.dll" "$@"
fi
