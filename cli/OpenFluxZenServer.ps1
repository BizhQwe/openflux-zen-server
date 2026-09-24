param([ValidateSet('help','credentials','uninstall')][string]$Command='help')
$root=Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path);$db=Join-Path $root 'app\data\openflux.db'
switch($Command){'help'{Write-Host 'OpenFluxZenServer help|credentials|uninstall'}'credentials'{Write-Host 'Credentials are configured with OPENFLUX_ADMIN_USER and OPENFLUX_ADMIN_PASSWORD during installation.'}'uninstall'{& (Join-Path $root 'installers\uninstall.bat')}}
