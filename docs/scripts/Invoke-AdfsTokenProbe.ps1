#Requires -Version 5.1
# ASCII-only on purpose: Windows PowerShell 5.1 misreads UTF-8-no-BOM Chinese and breaks parsing.
[CmdletBinding()]
param(
  [string]$Authority = "https://speechmessagests.speechmessage.com.tw/adfs",
  [string]$Resource = "https://jesus.speechmessage.com.tw/",
  [string]$ClientId = "2ad88395-b77d-4561-9441-d0e40824f9bc",
  [string]$UserName = "",
  [string]$Password = "",
  [string]$WebApiWhoAmI = "https://jesus.speechmessage.com.tw/api/data/v8.2/WhoAmI",
  [string]$ResultPath = ""
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Read-AppSettingsCrmConnection {
  $candidates = @(
    (Join-Path (Get-Location) "SpeechMessageProducts.ChurchReport\appsettings.json"),
    (Join-Path $PSScriptRoot "..\..\SpeechMessageProducts.ChurchReport\appsettings.json")
  )
  foreach ($appsettings in $candidates) {
    if (-not (Test-Path -LiteralPath $appsettings)) { continue }
    $raw = Get-Content -LiteralPath $appsettings -Raw -Encoding UTF8
    $u = [regex]::Match($raw, '"Username"\s*:\s*"([^"]+)"').Groups[1].Value
    $p = [regex]::Match($raw, '"Password"\s*:\s*"([^"]+)"').Groups[1].Value
    if (-not [string]::IsNullOrWhiteSpace($u) -and -not [string]::IsNullOrWhiteSpace($p)) {
      # JSON raw text has \\ for a single backslash.
      $u = $u.Replace("\\", "\")
      return [pscustomobject]@{ UserName = $u; Password = $p; Source = $appsettings }
    }
  }
  return $null
}

function Write-ResultObject([hashtable]$obj) {
  $json = $obj | ConvertTo-Json -Depth 8
  if ([string]::IsNullOrWhiteSpace($ResultPath)) {
    $ResultPath = Join-Path (Get-Location) "SpeechMessageProducts.ChurchReport\Logs\adfs-token-probe-latest.json"
  }
  $dir = Split-Path -Parent $ResultPath
  if (-not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
  }
  [System.IO.File]::WriteAllText($ResultPath, $json, [System.Text.UTF8Encoding]::new($false))
  Write-Host "RESULT_FILE=$ResultPath"
  Write-Host $json
}

if ([string]::IsNullOrWhiteSpace($UserName) -or [string]::IsNullOrWhiteSpace($Password)) {
  $crm = Read-AppSettingsCrmConnection
  if ($null -ne $crm) {
    $UserName = $crm.UserName
    $Password = $crm.Password
    Write-Host "Using CrmConnection username from appsettings (password not printed)."
  }
}

if ([string]::IsNullOrWhiteSpace($UserName) -or [string]::IsNullOrWhiteSpace($Password)) {
  Write-ResultObject @{
    ok = $false
    stage = "credentials"
    error = "UserName/Password required."
  }
  exit 2
}

$tokenUrl = $Authority.TrimEnd('/') + "/oauth2/token"
Write-Host "POST $tokenUrl"
Write-Host "resource=$Resource"
Write-Host "client_id=$ClientId"
Write-Host "username=$UserName"

$body = @{
  client_id  = $ClientId
  resource   = $Resource
  grant_type = "password"
  username   = $UserName
  password   = $Password
}

try {
  $resp = Invoke-RestMethod -Method Post -Uri $tokenUrl -Body $body -ContentType "application/x-www-form-urlencoded"
}
catch {
  $detail = $_.Exception.Message
  if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
    $detail = $detail + " | " + $_.ErrorDetails.Message
  }
  Write-ResultObject @{
    ok = $false
    stage = "token"
    tokenUrl = $tokenUrl
    resource = $Resource
    clientId = $ClientId
    username = $UserName
    error = $detail
    hints = @(
      "ADFS may reject this ClientId (invalid_client).",
      "ADFS may disable resource-owner password credentials.",
      "Username may need UPN form user@domain instead of DOMAIN\user.",
      "Resource may need trailing slash removed or different org URL."
    )
  }
  exit 3
}

if (-not $resp.access_token) {
  Write-ResultObject @{
    ok = $false
    stage = "token"
    error = "Token response missing access_token."
  }
  exit 4
}

Write-Host "TOKEN OK expires_in=$($resp.expires_in) token_type=$($resp.token_type)"
$token = [string]$resp.access_token

Write-Host "GET $WebApiWhoAmI"
try {
  $who = Invoke-RestMethod -Method Get -Uri $WebApiWhoAmI -Headers @{
    Authorization     = "Bearer $token"
    Accept            = "application/json"
    "OData-Version"   = "4.0"
    "OData-MaxVersion"= "4.0"
  }
  Write-ResultObject @{
    ok = $true
    stage = "whoami"
    tokenUrl = $tokenUrl
    resource = $Resource
    clientId = $ClientId
    username = $UserName
    expiresIn = $resp.expires_in
    tokenType = $resp.token_type
    whoAmI = $who
    nextStep = "Set Package01FeeReadsEnabled=true and retest fee list for Returned=56"
  }
  exit 0
}
catch {
  $detail = $_.Exception.Message
  if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
    $detail = $detail + " | " + $_.ErrorDetails.Message
  }
  Write-ResultObject @{
    ok = $false
    stage = "whoami"
    tokenUrl = $tokenUrl
    resource = $Resource
    clientId = $ClientId
    username = $UserName
    expiresIn = $resp.expires_in
    tokenAcquired = $true
    error = $detail
  }
  exit 5
}