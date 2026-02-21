param(
    [string]$SSMSPath = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE"
)

# Request Admin privileges if not already running as Admin
if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Elevating privileges to copy files to Program Files..." -ForegroundColor Yellow
    $arguments = "& '" + $MyInvocation.MyCommand.Definition + "'"
    Start-Process powershell -Verb runAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -Command $arguments"
    exit
}

$extensionName = "SQLsense"
$targetDir = "$SSMSPath\Extensions\$extensionName"
$sourceDir = "$PSScriptRoot\SQLsense\bin\Debug"

Write-Host "Deploying $extensionName to $targetDir..." -ForegroundColor Cyan

if (Test-Path $targetDir) {
    Write-Host "Cleaning up old extension files..." -ForegroundColor Yellow
    Remove-Item -Path "$targetDir\*" -Force -Recurse
} else {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    Write-Host "Created target directory."
}

# Essential output files (.dll, .pkgdef) and the manifest for MEF discovery
Copy-Item -Path "$sourceDir\*.dll" -Destination $targetDir -Force
Copy-Item -Path "$sourceDir\SQLsense.pkgdef" -Destination $targetDir -Force
Copy-Item -Path "$sourceDir\extension.vsixmanifest" -Destination $targetDir -Force

# Critical for MEF components (Real-time formatting): Clear the Component Model Cache
Write-Host "Clearing SSMS MEF Component Cache..." -ForegroundColor Yellow
$mefCachePath = "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_*\ComponentModelCache"
Get-ChildItem -Path $mefCachePath -Include * -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Forcing SSMS to rebuild its UI Menu Cache..." -ForegroundColor Yellow
$ssmsExe = "$SSMSPath\Ssms.exe"
if (Test-Path $ssmsExe) {
    Start-Process -FilePath $ssmsExe -ArgumentList "/updateconfiguration" -Wait
    Write-Host "UI Cache rebuilt successfully." -ForegroundColor Green
}

Write-Host "Deployment complete! You can now start SSMS." -ForegroundColor Green


