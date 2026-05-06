param(
    [string]$BaseUrl = "http://127.0.0.1:5116",
    [string]$LoadPath = "",
    [string]$LoadSaveId = "",
    [switch]$CreateTestSave
)

$ErrorActionPreference = "Stop"

function Write-Step($text) {
    Write-Host ""
    Write-Host "== $text ==" -ForegroundColor Cyan
}

function Invoke-JsonGet($url) {
    Invoke-RestMethod -Method Get -Uri $url -TimeoutSec 15
}

function Invoke-JsonPost($url, $body = $null) {
    if ($null -eq $body) {
        return Invoke-RestMethod -Method Post -Uri $url -TimeoutSec 15
    }

    return Invoke-RestMethod -Method Post -Uri $url -ContentType "application/json" -Body ($body | ConvertTo-Json -Depth 8) -TimeoutSec 15
}

Write-Step "Health"
$health = Invoke-JsonGet "$BaseUrl/api/v1/health"
$health | ConvertTo-Json -Depth 8

Write-Step "Session"
$session = Invoke-JsonGet "$BaseUrl/api/v1/session"
$session | ConvertTo-Json -Depth 8

Write-Step "Detected saves"
$saves = Invoke-JsonGet "$BaseUrl/api/v1/session/saves"
$saves | ConvertTo-Json -Depth 8

if ($LoadSaveId) {
    Write-Step "Load save by id"
    $loaded = Invoke-JsonPost "$BaseUrl/api/v1/session/load/id" @{ saveId = $LoadSaveId }
    $loaded | ConvertTo-Json -Depth 8
}
elseif ($LoadPath) {
    Write-Step "Load save by path"
    $loaded = Invoke-JsonPost "$BaseUrl/api/v1/session/load/path" @{ path = $LoadPath }
    $loaded | ConvertTo-Json -Depth 8
}
elseif ($saves.Count -gt 0) {
    Write-Step "Load last save"
    $loaded = Invoke-JsonPost "$BaseUrl/api/v1/session/load/last"
    $loaded | ConvertTo-Json -Depth 8
}
elseif ($CreateTestSave) {
    Write-Step "Create test save"
    $created = Invoke-JsonPost "$BaseUrl/api/v1/session/create" @{
        saveName = "ApiSmoke"
        agentName = "Api Tester"
        agencyName = "Smoke Test Management"
        fighterCount = 800
        nationality = "Spain"
        avatarKey = "Promoter"
    }
    $created | ConvertTo-Json -Depth 8
}
else {
    Write-Warning "No saves found and no -CreateTestSave flag passed. Dashboard and roster checks may fail."
}

Write-Step "Dashboard"
try {
    $dashboard = Invoke-JsonGet "$BaseUrl/api/v1/dashboard"
    $dashboard | ConvertTo-Json -Depth 8
}
catch {
    Write-Warning $_.Exception.Message
}

Write-Step "Roster"
try {
    $roster = Invoke-JsonGet "$BaseUrl/api/v1/roster?take=5"
    $roster | ConvertTo-Json -Depth 8
}
catch {
    Write-Warning $_.Exception.Message
}

Write-Step "World Feed"
try {
    $worldFeed = Invoke-JsonGet "$BaseUrl/api/v1/world-feed"
    $worldFeed | ConvertTo-Json -Depth 8
}
catch {
    Write-Warning $_.Exception.Message
}

Write-Step "Prospects"
try {
    $prospects = Invoke-JsonGet "$BaseUrl/api/v1/prospects"
    $prospects | ConvertTo-Json -Depth 8
}
catch {
    Write-Warning $_.Exception.Message
}

Write-Step "Agent"
try {
    $agent = Invoke-JsonGet "$BaseUrl/api/v1/agent"
    $agent | ConvertTo-Json -Depth 8
}
catch {
    Write-Warning $_.Exception.Message
}
