@echo off
sc stop OpenFluxZenServer >nul 2>&1
sc delete OpenFluxZenServer >nul 2>&1
echo Service removed. Delete the installation directory to remove application data.
