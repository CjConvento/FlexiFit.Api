@echo off
:: redeploy-flexifit.bat
:: Double-click this file to redeploy the app to IIS.
:: This must be run as Administrator (right-click > Run as administrator).

powershell -NoExit -ExecutionPolicy Bypass -File "%~dp0redeploy-flexifit.ps1"
