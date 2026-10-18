# ==============================================================================
# OpenFlux Zen Server - Fast 1-Command Bootstrap Installer (Windows)
# Supports: Windows x64, Windows ARM64, Windows x86
# ==============================================================================

#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host ""
    Write-Host "  [ERROR] Administrator privileges required." -ForegroundColor Red
    Write-Host "  Please run PowerShell as Administrator (Right click -> Run as administrator) and retry." -ForegroundColor Yellow
    Write-Host ""
    return
}

$nativeArch = $env:PROCESSOR_ARCHITEW6432
if (-not $nativeArch) { $nativeArch = $env:PROCESSOR_ARCHITECTURE }
$arch = switch ($nativeArch.ToUpperInvariant()) {
    'AMD64' { 'windows-amd64' }
    'ARM64' { 'windows-arm64' }
    'X86'   { 'windows-386' }
    default { 'windows-amd64' }
}
$displayArch = switch ($nativeArch.ToUpperInvariant()) {
    'ARM64' { 'win-arm64' }
    'X86'   { 'win-x86' }
    default { 'win-x64' }
}

# Unique private directory: predictable executables in a shared TEMP are unsafe.
$tempDir = Join-Path ([IO.Path]::GetTempPath()) ('openflux-install-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDir -ErrorAction Stop | Out-Null
$client = $null
$handler = $null
try {
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-18', 'S-1-5-32-544') | Select-Object -Unique) {
        $principal = New-Object Security.Principal.SecurityIdentifier($sid)
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($principal, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $tempDir -AclObject $acl
    Add-Type -AssemblyName System.Net.Http
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $handler = New-Object Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $client = New-Object Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromMinutes(5)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('OpenFlux-Bootstrap/1.1.2')

    function Save-ReleaseFile([string]$Address, [string]$Destination, [long]$Limit) {
        if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
        $uri = [Uri]$Address
        $deadline = [DateTime]::UtcNow.AddMinutes(5)
        $cancel = New-Object Threading.CancellationTokenSource
        $cancel.CancelAfter(300000)
        try {
        for ($redirect = 0; $redirect -lt 10; $redirect++) {
            if ($uri.Scheme -ne 'https' -or $uri.UserInfo) { throw 'Only HTTPS downloads are allowed.' }
            $response = $client.GetAsync($uri, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $cancel.Token).GetAwaiter().GetResult()
            try {
                if ([int]$response.StatusCode -in @(301, 302, 303, 307, 308)) {
                    if (-not $response.Headers.Location) { throw 'Missing redirect target.' }
                    $uri = [Uri]::new($uri, $response.Headers.Location)
                    continue
                }
                if ($response.StatusCode -ne [Net.HttpStatusCode]::OK) { throw "Download HTTP $([int]$response.StatusCode)." }
                if ($response.Content.Headers.ContentLength -gt $Limit) { throw 'Download exceeds size limit.' }
                $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                try {
                    $file = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    try {
                        $buffer = New-Object byte[] 65536
                        [long]$total = 0
                        while ($true) {
                            $remaining = [int][Math]::Max(0, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
                            if ($remaining -eq 0) { throw 'Download timed out.' }
                            $read = $stream.ReadAsync($buffer, 0, $buffer.Length, $cancel.Token)
                            if (-not $read.Wait($remaining)) { throw 'Download timed out.' }
                            $count = $read.GetAwaiter().GetResult()
                            if ($count -eq 0) { break }
                            $total += $count
                            if ($total -gt $Limit) { throw 'Download exceeds size limit.' }
                            $file.Write($buffer, 0, $count)
                        }
                        if ($total -eq 0) { throw 'Empty download.' }
                    } finally { $file.Dispose() }
                } finally { $stream.Dispose() }
                return
            } finally { $response.Dispose() }
        }
        throw 'Too many download redirects.'
        } finally { $cancel.Cancel(); $cancel.Dispose() }
    }

    $asset = "openflux-zen-server-installer-$arch.exe"
    $installerExe = Join-Path $tempDir $asset
    $checksumFile = Join-Path $tempDir 'installer.sha256'
    $repo = if ($env:OPENFLUX_REPOSITORY) { $env:OPENFLUX_REPOSITORY } else { 'BizhQwe/openflux-zen-server' }
    $bases = @("https://github.com/$repo/releases/latest/download", "https://github.com/$repo/releases/download/v1.1.2")
    Write-Host "`n  OpenFlux Zen Server - Windows ($displayArch)" -ForegroundColor Cyan
    Write-Host '  Downloading and verifying installer...' -ForegroundColor Gray
    $downloaded = $false
    foreach ($base in $bases) {
        try { Save-ReleaseFile "$base/$asset.sha256" $checksumFile 4096 } catch { continue }
        $fields = ([IO.File]::ReadAllText($checksumFile).Trim() -split '\s+')
        if ($fields.Count -ne 2 -or $fields[0] -notmatch '^[0-9a-fA-F]{64}$' -or ($fields[1] -replace '^\*', '') -cne $asset) {
            throw 'Invalid installer SHA-256 file. Nothing was executed.'
        }
        try { Save-ReleaseFile "$base/$asset" $installerExe 268435456 } catch { continue }
        if ((Get-FileHash -LiteralPath $installerExe -Algorithm SHA256).Hash -ine $fields[0]) {
            throw 'Installer SHA-256 mismatch. Nothing was executed.'
        }
        $downloaded = $true
        break
    }
    if (-not $downloaded) { throw "Failed to download a verified installer for $displayArch." }
    & $installerExe @args
    if ($LASTEXITCODE -ne 0) { throw "Installer failed with exit code $LASTEXITCODE." }
} finally {
    if ($client) { $client.Dispose() }
    if ($handler) { $handler.Dispose() }
    foreach ($name in @("openflux-zen-server-installer-$arch.exe", 'installer.sha256')) {
        $entry = Join-Path $tempDir $name
        if (Test-Path -LiteralPath $entry) { Remove-Item -LiteralPath $entry -Force }
    }
    if (Test-Path -LiteralPath $tempDir) { [IO.Directory]::Delete($tempDir) }
}
