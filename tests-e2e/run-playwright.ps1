param(
    [string]$Test = "all",          # general-ledger, feedback, trial-balance, single-flow, auth, invoices, concurrent-2users, concurrent-3branches, or all
    [string]$Headed = "1",          # "1" / "0" / "true" / "false"
    [string]$BaseUrl = "https://localhost:7119"
)

$isHeaded = ($Headed -eq "1" -or $Headed -eq "true" -or $Headed -eq "yes")
$env:BASE_URL = $BaseUrl
$headedArg = if ($isHeaded) { "--headed" } else { "" }

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "         PLAYWRIGHT E2E TEST RUNNER - ERP 2026            " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Target URL : $BaseUrl" -ForegroundColor Gray
Write-Host "Mode       : $(if ($Headed) { 'Headed (Visible Browser)' } else { 'Headless' })" -ForegroundColor Gray
Write-Host "Test Target: $Test" -ForegroundColor Gray
Write-Host "----------------------------------------------------------" -ForegroundColor Cyan

# Check if web server is alive, auto-start if needed
Write-Host "Checking if ERP Web server is responding on $BaseUrl ..." -ForegroundColor Yellow
$serverReady = $false
try {
    $req = [System.Net.WebRequest]::Create("$BaseUrl/Account/Login")
    $req.Timeout = 2500
    $resp = $req.GetResponse()
    $resp.Close()
    $serverReady = $true
    Write-Host "-> Web server is ALREADY RUNNING and responding!`n" -ForegroundColor Green
} catch {
    $serverReady = $false
}

if (-not $serverReady) {
    Write-Host "-> Web server is NOT running. Automatically launching ERP.Web in background..." -ForegroundColor Cyan
    $projectPath = Join-Path (Split-Path $PSScriptRoot -Parent) "ERP.Web\ERP.Web.csproj"
    
    $proc = Start-Process -FilePath "dotnet" -ArgumentList "run --project `"$projectPath`" --urls `"https://localhost:7119;http://127.0.0.1:5239`"" -PassThru -WindowStyle Minimized
    
    Write-Host "Waiting for ERP.Web to initialize on $BaseUrl ..." -ForegroundColor Yellow
    $retries = 35
    while ($retries -gt 0 -and -not $serverReady) {
        Start-Sleep -Seconds 2
        try {
            $req = [System.Net.WebRequest]::Create("$BaseUrl/Account/Login")
            $req.Timeout = 2000
            $resp = $req.GetResponse()
            $resp.Close()
            $serverReady = $true
            Write-Host "-> Web server successfully started and ready!`n" -ForegroundColor Green
            break
        } catch {
            $retries--
            Write-Host -NoNewline "." -ForegroundColor Gray
        }
    }

    if (-not $serverReady) {
        Write-Host "`n-> ERROR: Failed to start ERP.Web automatically within 70 seconds." -ForegroundColor Red
        Write-Host "Please start ERP.Web from Visual Studio or run 'dotnet run --project ERP.Web' manually.`n" -ForegroundColor Red
        exit 1
    }
}

$testsDir = $PSScriptRoot

function Run-Script($scriptFile, $desc) {
    Write-Host "`n>>> Running: $desc ($scriptFile) ..." -ForegroundColor Cyan
    $fullPath = Join-Path $testsDir $scriptFile
    if ($headedArg) {
        node $fullPath --headed
    } else {
        node $fullPath
    }
    if ($LASTEXITCODE -eq 0) {
        Write-Host ">>> PASSED: $desc`n" -ForegroundColor Green
    } else {
        Write-Host ">>> FAILED: $desc (Exit Code: $LASTEXITCODE)`n" -ForegroundColor Red
    }
}

switch ($Test.ToLower()) {
    "general-ledger" {
        Run-Script "test-general-ledger.js" "General Ledger E2E Performance Test"
    }
    "feedback" {
        Run-Script "test-system-feedback.js" "System Feedback Tickets E2E Test"
    }
    "trial-balance" {
        Run-Script "test-trial-balance.js" "Trial Balance Verification Test"
    }
    "single-flow" {
        Run-Script "test-single-flow.js" "Single Session Journal Entry Complete Flow"
    }
    "auth" {
        Run-Script "test-auth.js" "Authentication & Session Preservation Test"
    }
    "financial" {
        Run-Script "test-financial-statements.js" "Financial Statements & Income Statement Test"
    }
    "invoices" {
        Run-Script "test-invoices-inventory-rbac.js" "Invoices, Quantities, Serials, Branches & RBAC Complete Test"
    }
    "concurrent-2users" {
        Run-Script "test-concurrent-2users-same-branch.js" "Concurrency Test: 2 Users in Same Branch (40 Operations)"
    }
    "concurrent-3branches" {
        Run-Script "test-concurrent-3users-3branches.js" "Concurrency Test: 3 Users in 3 Branches (90 Operations)"
    }
    default {
        # Run suite
        Run-Script "test-auth.js" "1. Authentication"
        Run-Script "test-general-ledger.js" "2. General Ledger Performance"
        Run-Script "test-trial-balance.js" "3. Trial Balance"
        Run-Script "test-system-feedback.js" "4. System Feedback"
        Run-Script "test-single-flow.js" "5. Full Journal Entry Flow"
    }
}

Write-Host "----------------------------------------------------------" -ForegroundColor Cyan
Write-Host "Test run completed. Check 'tests-e2e/screenshots' for captured images." -ForegroundColor Yellow
