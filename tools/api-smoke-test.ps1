param(
    [string]$BaseUrl = "http://127.0.0.1:5116",
    [string]$LoadPath = "",
    [string]$LoadSaveId = "",
    [string]$UserId = "",
    [string]$DisplayName = "",
    [string]$AuthProvider = "",
    [string]$ProviderUserId = "",
    [string]$BearerToken = "",
    [switch]$CreateTestSave
)

$ErrorActionPreference = "Stop"

function Write-Step($text) {
    Write-Host ""
    Write-Host "== $text ==" -ForegroundColor Cyan
}

function Write-SaveSummary($label, $response) {
    if ($null -eq $response) {
        return
    }

    $saveId = $response.currentSaveId
    if (-not $saveId -and $response.currentSave) {
        $saveId = $response.currentSave.saveId
    }

    $owner = $response.currentOwnerUserId
    if (-not $owner -and $response.currentSave) {
        $owner = $response.currentSave.ownerUserId
    }

    if ($saveId) {
        Write-Host "$label SaveId: $saveId" -ForegroundColor Yellow
    }

    if ($owner) {
        Write-Host "$label Owner: $owner" -ForegroundColor DarkYellow
    }
}

function Invoke-JsonGet($url) {
    Invoke-RestMethod -Method Get -Uri $url -Headers (Get-RequestHeaders) -TimeoutSec 15
}

function Invoke-JsonPost($url, $body = $null) {
    if ($null -eq $body) {
        return Invoke-RestMethod -Method Post -Uri $url -Headers (Get-RequestHeaders) -TimeoutSec 15
    }

    return Invoke-RestMethod -Method Post -Uri $url -Headers (Get-RequestHeaders) -ContentType "application/json" -Body ($body | ConvertTo-Json -Depth 8) -TimeoutSec 15
}

function Get-RequestHeaders() {
    $headers = @{}

    if ($BearerToken) {
        $headers["Authorization"] = "Bearer $BearerToken"
    }

    if ($UserId) {
        $headers["X-MMA-User-Id"] = $UserId
    }

    if ($DisplayName) {
        $headers["X-MMA-Display-Name"] = $DisplayName
    }

    if ($AuthProvider) {
        $headers["X-MMA-Auth-Provider"] = $AuthProvider
    }

    if ($ProviderUserId) {
        $headers["X-MMA-Provider-User-Id"] = $ProviderUserId
    }

    return $headers
}

Write-Step "Health"
$health = Invoke-JsonGet "$BaseUrl/api/v1/health"
$health | ConvertTo-Json -Depth 8

Write-Step "Auth"
$auth = Invoke-JsonGet "$BaseUrl/api/v1/auth/me"
$auth | ConvertTo-Json -Depth 8

Write-Step "Session"
$session = Invoke-JsonGet "$BaseUrl/api/v1/session"
$session | ConvertTo-Json -Depth 8

Write-Step "Detected saves"
$saves = Invoke-JsonGet "$BaseUrl/api/v1/session/saves"
$saves | ConvertTo-Json -Depth 8

if ($LoadSaveId) {
    Write-Step "Load save by id"
    $loaded = Invoke-JsonPost "$BaseUrl/api/v1/session/load/id" @{ saveId = $LoadSaveId }
    Write-SaveSummary "Loaded" $loaded
    $loaded | ConvertTo-Json -Depth 8
}
elseif ($LoadPath) {
    Write-Step "Load save by path"
    $loaded = Invoke-JsonPost "$BaseUrl/api/v1/session/load/path" @{ path = $LoadPath }
    Write-SaveSummary "Loaded" $loaded
    $loaded | ConvertTo-Json -Depth 8
}
elseif ($saves.Count -gt 0) {
    Write-Step "Load last save"
    $loaded = Invoke-JsonPost "$BaseUrl/api/v1/session/load/last"
    Write-SaveSummary "Loaded" $loaded
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
    Write-SaveSummary "Created" $created
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
