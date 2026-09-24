#!/usr/bin/env bash
set -euo pipefail
PREFIX="${OPENFLUX_PREFIX:-/opt/openflux-zen-server}"; REPO="https://github.com/BizhQwe/openflux-zen-server.git"+mkdir -p "$PREFIX"; if [ ! -d "$PREFIX/.git" ]; then git clone "$REPO" "$PREFIX"; else git -C "$PREFIX" pull --ff-only; fi
dotnet publish "$PREFIX/src/OpenFlux.Zen.Server/OpenFlux.Zen.Server.csproj" -c Release -o "$PREFIX/app"; cp -r "$PREFIX/runtimes" "$PREFIX/app/"; chmod +x "$PREFIX/app/runtimes/openflux-linux-"* || true
cat >/etc/systemd/system/openflux-zen-server.service <<EOF
[Unit]
Description=OpenFlux Zen Server
After=network.target
[Service]
WorkingDirectory=$PREFIX/app
ExecStart=/usr/bin/dotnet $PREFIX/app/OpenFlux.Zen.Server.dll
Restart=on-failure
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload; systemctl enable --now openflux-zen-server.service; echo "OpenFlux Zen Server installed at $PREFIX"

