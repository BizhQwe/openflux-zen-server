[English](README_EN.md) | [Русский](README.md)

<p align="center">
  <img src="src/OpenFlux.Zen.Server.Web/internal/panel/web/images/logo.svg" alt="OpenFlux Zen Server" width="96">
</p>

<h1 align="center">OpenFlux Zen Server</h1>

<p align="center">
  <strong>Standalone server and modern web panel for convenient OpenFlux tunnel management</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Go-1.26.9-00ADD8?logo=go&logoColor=white" alt="Go 1.26.9">
  <img src="https://img.shields.io/badge/Platforms-Windows%20%7C%20Linux-blue" alt="Windows and Linux">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-GPL--3.0-blue" alt="GPL-3.0"></a>
  <a href="https://github.com/BizhQwe/openflux-zen-server/releases"><img src="https://img.shields.io/github/v/release/BizhQwe/openflux-zen-server?color=blue&label=Release&logo=github" alt="Latest Release"></a>
</p>

## About

**OpenFlux Zen Server** is a complete, turnkey server solution based on the [OpenFlux](https://github.com/p1neappleXpress/OpenFlux) tunnel core, equipped with an intuitive web panel, native background system service, and a dedicated management CLI.

It allows you to deploy a secure L3/L4 tunnel server in minutes: configure exit tunnels directly in your browser, monitor real-time traffic and bandwidth, and share connection profiles with clients via links or QR codes.

All components (**Core**, **Web**, **CLI**, and **Installer**) are written in Go and compiled into self-contained binaries:
- **No external databases** — settings and tunnel state are stored locally.
- **No external web server required** — web interface assets are embedded directly into the panel binary.
- **No Docker required** — runs natively as a managed background service (systemd on Linux or Windows Service).

## Features

The core is based on OpenFlux v0.13.0, with Bitrix24, Mixed/Striped groups, per-carrier lane/network settings and an outbound SOCKS5 proxy for L4. See the [migration report](docs/core-upgrade-v0.13.0.md) for the local patch inventory and compatibility notes.

- 🚀 **Tunnel Management**: Create, configure, start, and stop server-side OpenFlux L3/L4 tunnels, supporting Multi configurations and multiple transport protocols.
- 📊 **Real-Time Monitoring**: Live upload/download bandwidth graphs, traffic consumption stats, and host resource usage (CPU, RAM, Disk).
- 📜 **Live Logs**: Built-in core and system log viewer with filtering and auto-scroll.
- 📱 **Instant Client Onboarding**: Automatic generation of compatible connection links and QR codes for OpenFlux client applications.
- 🌐 **4 Network Accessibility Modes**:
  - `domain` — Public IP connection or custom domain via Reverse Proxy (Caddy, Nginx).
  - `localtunnel` — Built-in public relay publication without requiring a dedicated public IP or port forwarding (NAT traversal).
  - `lan` — Local subnet / Wi-Fi access only.
  - `localhost` — Strictly accessible from the local host.
- 🔒 **Built-in Security**: The web panel is protected behind an obfuscated secret URL path (`/<secret_path>/`), salted & hashed passwords, rate limiting, and optional TLS/HTTPS.
- 🛠 **Integrated CLI (`openflux`)**: Check status, inspect login credentials, restart services, and toggle system boot autostart right from the terminal.
- 🌍 **Bilingual Interface**: Full English and Russian language support across the web panel, installer, and CLI tool.

## Screenshots

<details>
<summary><strong>Click to view web panel screenshots</strong></summary>
<br>

| Tunnel List & Live Stats | Login Screen |
|:---:|:---:|
| <img src="docs/screenshots/tunnels.png" alt="Tunnel list" width="460"> | <img src="docs/screenshots/login.png" alt="Login screen" width="460"> |

| Real-Time Log Viewer | Network Settings & Access Modes |
|:---:|:---:|
| <img src="docs/screenshots/logs.png" alt="Tunnel logs" width="460"> | <img src="docs/screenshots/network-settings.png" alt="Network settings" width="460"> |

| Server Settings | Configuration Import & Export |
|:---:|:---:|
| <img src="docs/screenshots/settings.png" alt="Panel settings" width="460"> | <img src="docs/screenshots/config.png" alt="Import and export" width="460"> |

</details>

## Installation

Two convenient installation methods are supported:

### Method 1: One-Command Online Install (Recommended)

The installation script automatically detects your CPU architecture, downloads the matching installer from the latest GitHub Release, verifies the SHA-256 checksum, and launches the interactive setup wizard.

#### Linux
`curl` must be installed on your system (e.g., `sudo apt install -y curl`):
```bash
curl -sSL https://raw.githubusercontent.com/BizhQwe/openflux-zen-server/main/scripts/install.sh | sudo sh
```
*(if curl is not available, use `wget -qO- ... | sh`)*

#### Windows
Open **PowerShell as Administrator** and execute:
```powershell
irm https://raw.githubusercontent.com/BizhQwe/openflux-zen-server/main/scripts/install.ps1 | iex
```

### Method 2: Standalone Release Installer (GitHub Releases)

Head over to [GitHub Releases](https://github.com/BizhQwe/openflux-zen-server/releases) and download the standalone installer for your platform:

| Platform | Architectures | Installer Binary |
|---|---|---|
| **Linux** | `amd64`, `arm64`, `386`, `armv7` | `openflux-zen-server-installer-linux-<arch>` |
| **Windows** | `amd64`, `arm64`, `386` | `openflux-zen-server-installer-windows-<arch>.exe` |

Run the downloaded binary:
```bash
# Linux (with root / sudo)
chmod +x openflux-zen-server-installer-linux-amd64
sudo ./openflux-zen-server-installer-linux-amd64

# Windows
.\openflux-zen-server-installer-windows-amd64.exe
```

### Silent and Custom Installation Flags

The installer supports command-line flags for unattended or automated installations:

```bash
# Example: silent install on port 8080 with domain mode and autostart enabled
sudo ./openflux-zen-server-installer-linux-amd64 --silent --mode domain --domain "vpn.example.com" --port 8080 --lang en --autostart on
```

| Flag | Default | Description |
|---|---|---|
| `--mode <mode>` | interactive (`domain`) | Network mode: `domain`, `localtunnel`, `lan`, `localhost` |
| `--port <port>` | `5000` | Web panel port (1–65535) |
| `--listen <host:port>` | mode-dependent | Exact listening host and port |
| `--domain <url>` | empty | Public domain or reverse proxy URL |
| `--lang <en\|ru>` | `en` / `ru` | Language for installer and web panel |
| `--autostart <on\|off>` | `on` | Automatically start service on OS boot |
| `--silent` | `false` | Unattended mode (skips wizard questions) |
| `--dir <path>` | `/opt/openflux-zen-server` or `Program Files` | Target installation directory |
| `--no-service` | `false` | Install files without registering or running as a service |
| `--reset-password` | `false` | Generate a new password during update |
| `--verify` | `false` | Verify embedded bundle integrity and exit |

> [!NOTE]
> On the initial installation, a unique administrator username and secure password are automatically generated and displayed in the summary alongside the panel URL. **Make sure to record them!**

## Quick Start

Once installation completes:

1. **Access the Web Panel**: In your browser, navigate to the URL printed by the installer:
   ```text
   http://<server-ip>:5000/<secret_path>/
   ```
2. **Log In**: Enter the unique username and password generated by the installer during setup.
3. **Create a Tunnel**:
   - Go to **Tunnels** and click **Add Tunnel**.
   - Configure the tunnel parameters (port, transport protocol, encryption key) and save.
   - Click **Start**.
4. **Connect Clients**:
   - Click on the created tunnel to view connection parameters.
   - Copy the connection URL or scan the **QR code** using an OpenFlux client app.

> [!TIP]
> Forgot your password or lost the panel link? Run this command on the server:
> ```bash
> openflux credentials
> ```
> *(on Linux: `sudo openflux credentials`)* — this immediately prints the generated username, password, and all available access URLs.

## CLI Management (`openflux`)

The package includes the `openflux` CLI tool. On Windows, it is available globally in any command prompt; on Linux, it is symlinked to `/usr/local/bin/openflux` (or accessible via sudo).

```bash
openflux <command> [options]
```

### Command Reference

| Command | Description | Example |
|---|---|---|
| `status` | Show panel, service, and network status | `openflux status` |
| `credentials` | Show login credentials (username, password, URLs) | `sudo openflux credentials` |
| `start` | Start the server background service | `sudo openflux start` |
| `stop` | Stop the server background service | `sudo openflux stop` |
| `restart` | Restart the server background service | `sudo openflux restart` |
| `autostart <enable\|disable\|status>` | Manage automatic boot startup | `sudo openflux autostart enable` |
| `uninstall [-y]` | Completely remove server, service, tunnels, and data | `sudo openflux uninstall` |
| `help` | Display CLI help and command overview | `openflux help` |

## Network Accessibility Modes

OpenFlux Zen Server provides 4 deployment modes:

1. **Public IP / Domain (`domain`)**:
   - Standard setup for cloud VPS with a public IP address.
   - Ideal for pairing with custom domain names or Reverse Proxies (Caddy, Nginx) with automated SSL/TLS.
2. **Localtunnel (`localtunnel`)**:
   - Ideal when the server **does not have a public IP** (behind CGNAT, mobile broadband, or home routers without port forwarding capabilities).
   - Establishes an outbound tunnel to a public relay, providing a reachable HTTPS URL anywhere in the world.
3. **Local Area Network (`lan`)**:
   - The panel is accessible to all devices connected to your local home or office network (via the server's local network IP, e.g. `http://192.168.1.100:5000/...`).
4. **Localhost Only (`localhost`)**:
   - Binds strictly to `127.0.0.1` and is accessible only from the machine it is running on.

## Project Structure

```text
src/
  OpenFlux.Zen.Server.Core/       # OpenFlux tunnel core (L3/L4 tunneling & protocol engine)
  OpenFlux.Zen.Server.Web/        # Web panel: HTTP API, authentication, storage, and embedded UI
  OpenFlux.Zen.Server.Cli/        # CLI management tool (openflux)
  OpenFlux.Zen.Server.Installer/  # Standalone self-extracting installer
scripts/                          # Build scripts (build.sh / build.ps1) and installer scripts
docs/screenshots/                 # Web interface screenshots
dist/                             # Compiled binaries and release assets
go.work                           # Go workspace configuration
```

Web interface sources are located in `src/OpenFlux.Zen.Server.Web/internal/panel/web`. Templates, scripts, styles, and images are embedded into the panel binary via `go:embed`.

## Building from Source

Building requires **Go 1.26.9+** (the version is specified in `go.work`).
The `.sh` scripts require a POSIX shell and `sha256sum` (Linux / Git Bash); the `.ps1` versions work without Git Bash.

Clone the repository:
```bash
git clone https://github.com/BizhQwe/openflux-zen-server.git
cd openflux-zen-server
```

### Build for Current Platform

```bash
# Linux
sh scripts/build.sh linux-amd64

# Windows
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Targets windows-amd64
```

### Build for All Target Platforms

```bash
# With no arguments, builds all seven Windows and Linux targets into dist/
sh scripts/build.sh

# Or on Windows
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

Compiled binaries and self-extracting installer packages (`openflux-zen-server-installer-*`) with SHA-256 digests will be generated in `dist/`.

All scripts share [scripts/targets.txt](scripts/targets.txt). The `--all` / `-All` switches remain supported; select a specific platform using the examples above.

### Preparing a Release

The command builds every platform, verifies individual SHA-256 files and creates `dist/SHA256SUMS.txt`:

```bash
sh scripts/package-release.sh
```

```powershell
.\scripts\package-release.ps1
```

For existing builds, use `sh scripts/package-release.sh --skip-build dist` or `.\scripts\package-release.ps1 -SkipBuild -ReleaseDir dist`. To select another output directory, pass a positional argument to `.sh` or `-ReleaseDir` to `.ps1`; a normal run copies the complete installer set there. These scripts prepare local files without publishing to GitHub.

## License & Acknowledgments

- Licensed under the **GNU General Public License v3.0**. See [`LICENSE`](LICENSE) for full terms.
- Based on the [OpenFlux](https://github.com/p1neappleXpress/OpenFlux) project.
- Third-party components and licenses are credited in [`NOTICE`](NOTICE) (including WireGuard-go, gVisor, gopacket, Gorilla WebSocket, pion/webrtc, and others).
