#Requires -Version 7.0
<#
.SYNOPSIS
Starts the API and web development servers.
.DESCRIPTION
Requires PowerShell 7+, the .NET 10 SDK, and Node.js with npm.
Installs web dependencies with npm ci when node_modules is missing.
The web app is available at http://localhost:5173 and proxies API requests
to http://localhost:5021. Press Ctrl+C to stop both servers.

For live APIM access, configure Apim__SubscriptionId,
Apim__ResourceGroupName, and Apim__ServiceName in your environment as needed
and sign in using an Azure credential supported by DefaultAzureCredential
(for example, az login). The http launch profile sets
AZURE_TOKEN_CREDENTIALS=dev so an Azure VM's managed identity is not used in
place of your az login. Credentials are not required just to start the app.
Verify live access with: node tests/smoke/live-smoke.mjs
.EXAMPLE
pwsh -File .\start.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$webDirectory = Join-Path $PSScriptRoot 'src\web'
$apiProject = Join-Path $PSScriptRoot 'src\server\ApimPolicyVisualizer.Api\ApimPolicyVisualizer.Api.csproj'

$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$node = (Get-Command node -CommandType Application -ErrorAction Stop).Source
$npm = (Get-Command npm.cmd -CommandType Application -ErrorAction Stop).Source

$sdks = & $dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.')) {
    throw 'Install the .NET 10 SDK before starting the solution.'
}

if (-not (Test-Path (Join-Path $webDirectory 'node_modules') -PathType Container)) {
    Push-Location $webDirectory
    try {
        & $npm ci
        if ($LASTEXITCODE -ne 0) {
            throw "npm ci failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

$vite = Join-Path $webDirectory 'node_modules\vite\bin\vite.js'
if (-not (Test-Path $vite -PathType Leaf)) {
    throw "Vite is missing. Run npm ci in '$webDirectory' and try again."
}

function Start-DevProcess {
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [string] $WorkingDirectory
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    return [System.Diagnostics.Process]::Start($startInfo)
}

$api = $null
$web = $null
try {
    $api = Start-DevProcess -FilePath $dotnet -WorkingDirectory $PSScriptRoot -Arguments @(
        'run', '--project', $apiProject, '--launch-profile', 'http'
    )
    $web = Start-DevProcess -FilePath $node -WorkingDirectory $webDirectory -Arguments @(
        $vite, '--host', 'localhost', '--port', '5173', '--strictPort'
    )

    Write-Host 'Starting web app: http://localhost:5173'
    Write-Host 'Starting API:     http://localhost:5021'
    Write-Host 'Press Ctrl+C to stop both servers.'

    while ($true) {
        if ($api.HasExited) {
            throw "The API server exited with code $($api.ExitCode). See its output above."
        }
        if ($web.HasExited) {
            throw "The web server exited with code $($web.ExitCode). See its output above."
        }
        Start-Sleep -Milliseconds 500
    }
}
finally {
    foreach ($process in @($web, $api)) {
        if ($null -ne $process) {
            if (-not $process.HasExited) {
                $process.Kill($true)
                $process.WaitForExit()
            }
            $process.Dispose()
        }
    }
}
