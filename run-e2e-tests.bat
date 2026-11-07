@echo off
chcp 65001 > nul
echo ========================================================
echo        PLAYWRIGHT E2E TESTS LAUNCHER - ERP 2026
echo ========================================================
echo.
echo [1] Run Invoices, Quantities, Serials, Branches & Permissions Test
echo [2] Concurrency Test: 2 Users in Same Branch (40 ops - Sales, Stock, Return, Cash Receipt)
echo [3] Concurrency Test: 3 Users in 3 Branches (90 ops - Invoices, Receipts, JVs, Stock)
echo [4] Run All General Accounting Tests (Headed)
echo [5] Run General Ledger Performance Test
echo [6] Run System Feedback Test
echo [7] Run Trial Balance Test
echo [8] Run Journal Entry Flow Test
echo [9] Run Concurrency Tests in Background (Headless)
echo.
set /p choice="Enter choice (1-9) [Default: 2]: "

if "%choice%"=="" set choice=2
if "%choice%"=="1" powershell -ExecutionPolicy Bypass -File "%~dp0tests-e2e\run-playwright.ps1" -Test invoices -Headed 1
if "%choice%"=="2" powershell -ExecutionPolicy Bypass -File "%~dp0tests-e2e\run-playwright.ps1" -Test concurrent-2users -Headed 1
if "%choice%"=="3" powershell -ExecutionPolicy Bypass -File "%~dp0tests-e2e\run-playwright.ps1" -Test concurrent-3branches -Headed 1
if "%choice%"=="4" powershell -ExecutionPolicy Bypass -File "%~dp0tests-e2e\run-playwright.ps1" -Test all -Headed 1
if "%choice%"=="5" powershell -ExecutionPolicy Bypass -File "%~dp0tests-e2e\run-playwright.ps1" -Test general-ledger -Headed 1
if "%choice%"=="6" powershell -ExecutionPolicy Bypass -File "%~dp0tests-e2e\run-playwright.ps1" -Test feedback -Headed 1
if "%choice%"=="7" powershell -ExecutionPolicy Bypass -File "%~dp0tests-e2e\run-playwright.ps1" -Test trial-balance -Headed 1
if "%choice%"=="8" powershell -ExecutionPolicy Bypass -File "%~dp0tests-e2e\run-playwright.ps1" -Test single-flow -Headed 1
if "%choice%"=="9" (
    powershell -ExecutionPolicy Bypass -File "%~dp0tests-e2e\run-playwright.ps1" -Test concurrent-2users -Headed 0
    powershell -ExecutionPolicy Bypass -File "%~dp0tests-e2e\run-playwright.ps1" -Test concurrent-3branches -Headed 0
)

pause
