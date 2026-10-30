###############################################################################
# Upload content.zip (and/or mods.zip) into the game's LocalState on an Xbox in Dev Mode
# via the Windows Device Portal REST API. The game unpacks them on its next start:
#   LocalState\content.zip -> LocalState\Content     LocalState\mods.zip -> LocalState\mods
#
# Only needed for the small code-only .msix (the *_full.msix already contains Content) or to add mods.
#
# USAGE:
#   .\upload_content_to_xbox.ps1 -XboxIP 192.168.1.50                      # builds content.zip if missing, uploads it
#   .\upload_content_to_xbox.ps1 -XboxIP 192.168.1.50 -ModsZip .\mods.zip -SkipContent
# Device Portal user/password = the ones set in Dev Home > Remote Access Settings.
#
# Note: the Device Portal file API can upload a file into an EXISTING folder only (it cannot create
# folders), which is why content goes up as one zip instead of 2500 loose files.
###############################################################################
param(
    [Parameter(Mandatory=$true)][string]$XboxIP,
    [string]$Username = "",
    [string]$Password = "",
    [string]$ContentZip = (Join-Path $PSScriptRoot "content.zip"),
    [string]$ModsZip = "",
    [switch]$SkipContent,
    [string]$PackageName = "FridayNightFunkin.MonoGame"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

if (-not $Username) { $Username = Read-Host "Device Portal user name" }
if (-not $Password) {
    $sec = Read-Host "Device Portal password" -AsSecureString
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
}

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.ServerCertificateCustomValidationCallback = { $true }   # Device Portal uses a self-signed cert
$handler.CookieContainer = New-Object System.Net.CookieContainer
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromHours(3)
$base = "https://${XboxIP}:11443"

function SetAuth([string]$user) {
    $b = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${user}:$Password"))
    $client.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Basic", $b)
}

# 1. find the installed package (this GET also delivers the CSRF cookie needed for POST)
SetAuth "auto-$Username"     # "auto-" prefix = no browser-style auth prompt (Device Portal convention)
$resp = $client.GetAsync("$base/api/app/packagemanager/packages").Result
if (-not $resp.IsSuccessStatusCode) { SetAuth $Username; $resp = $client.GetAsync("$base/api/app/packagemanager/packages").Result }
if (-not $resp.IsSuccessStatusCode) { throw "Device Portal login failed: $($resp.StatusCode)" }
$pkgs = ($resp.Content.ReadAsStringAsync().Result | ConvertFrom-Json).InstalledPackages
$pkg = $pkgs | Where-Object { $_.PackageFullName -like "$PackageName*" } | Select-Object -First 1
if (-not $pkg) { throw "The game is not installed on the Xbox (install the .msix first)." }
$pfn = $pkg.PackageFullName
Write-Host "Package: $pfn" -ForegroundColor Green
$csrf = ($handler.CookieContainer.GetCookies([Uri]$base) | Where-Object Name -eq "CSRF-Token").Value
if ($csrf) { $client.DefaultRequestHeaders.Add("X-CSRF-Token", $csrf) }

function Upload([string]$file) {
    $name = [IO.Path]::GetFileName($file)
    $size = (Get-Item $file).Length / 1MB
    Write-Host ("Uploading {0} ({1:N0} MB) to LocalState ..." -f $name, $size) -ForegroundColor Yellow
    $url = "$base/api/filesystem/apps/file?knownfolderid=LocalAppData&packagefullname=$([Uri]::EscapeDataString($pfn))&path=$([Uri]::EscapeDataString('\LocalState'))"
    $fs = [IO.File]::OpenRead($file)
    try {
        $content = New-Object System.Net.Http.MultipartFormDataContent
        $sc = New-Object System.Net.Http.StreamContent($fs)
        $content.Add($sc, $name, $name)
        $r = $client.PostAsync($url, $content).Result
        if (-not $r.IsSuccessStatusCode) { throw "upload of $name failed: $($r.StatusCode) $($r.Content.ReadAsStringAsync().Result)" }
    } finally { $fs.Dispose() }
    Write-Host "  done" -ForegroundColor Green
}

if (-not $SkipContent) {
    if (-not (Test-Path $ContentZip)) { & (Join-Path $PSScriptRoot "package_content.ps1") -OutputZip $ContentZip }
    if ([IO.Path]::GetFileName($ContentZip) -ne "content.zip") { throw "the uploaded file must be named content.zip" }
    Upload $ContentZip
}
if ($ModsZip) {
    if ([IO.Path]::GetFileName($ModsZip) -ne "mods.zip") { throw "the mods archive must be named mods.zip" }
    Upload $ModsZip
}
Write-Host "Start the game on the Xbox: it unpacks the zip(s) on the loading screen." -ForegroundColor Green
