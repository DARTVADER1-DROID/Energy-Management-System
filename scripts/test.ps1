[CmdletBinding()]
param(
    [switch]$KeepDatabase
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot "docker-compose.test.yml"
$postgresPort = if ($env:EMS_TEST_POSTGRES_PORT) {
    $env:EMS_TEST_POSTGRES_PORT
}
else {
    "55432"
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker CLI was not found. Install Docker Desktop and ensure it is running."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found. Install the .NET 10 SDK."
}

$previousConnectionString = $env:EMS_TEST_CONNECTION_STRING
$testConnectionString = "Host=localhost;Port=$postgresPort;Database=energy_management_tests;Username=test_user;Password=test_password"
$composeArguments = @("compose", "-f", $composeFile)

try {
    Write-Host "Starting isolated PostgreSQL 17 test database..." -ForegroundColor Cyan
    & docker @composeArguments up --detach --wait postgres
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL could not be started. Check Docker Desktop and port $postgresPort."
    }

    $env:EMS_TEST_CONNECTION_STRING = $testConnectionString

    Write-Host "Restoring and building the test project, then running all tests..." -ForegroundColor Cyan
    Push-Location $repositoryRoot
    try {
        & dotnet test "tests/EnergyManagement.Tests/EnergyManagement.Tests.csproj" --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "The test command failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "All tests passed." -ForegroundColor Green
}
finally {
    $env:EMS_TEST_CONNECTION_STRING = $previousConnectionString

    if (-not $KeepDatabase) {
        Write-Host "Stopping the test database..." -ForegroundColor Cyan
        & docker @composeArguments down --remove-orphans
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Docker Compose could not fully clean up the test database."
        }
    }
    else {
        Write-Host "Keeping the test database running as requested." -ForegroundColor Yellow
    }
}
