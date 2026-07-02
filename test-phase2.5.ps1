# Phase 2.5 Test Script — Captures the REAL download request
# Run this AFTER reloading the extension and BEFORE triggering a download

Write-Host "=== MAC-1 Phase 2.5 Test Script ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clear old debug data
Write-Host "[1/6] Clearing old debug data..." -ForegroundColor Yellow
$extensionUrl = "chrome-extension://$(Get-ChildItem 'C:\Users\bilal\AppData\Local\Google\Chrome\User Data\Default\Extensions' | Where-Object { Test-Path "$($_.FullName)\*\manifest.json" } | ForEach-Object { Get-ChildItem "$($_.FullName)\*" -Directory | Select-Object -First 1 } | ForEach-Object { $_.Parent.Name })/popup.html"
Write-Host "  Extension URL: $extensionUrl"

# Step 2: Check if service is running
Write-Host "[2/6] Checking service..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "http://localhost:57575/api/health" -Method GET -TimeoutSec 3
    Write-Host "  Service: RUNNING (version $($health.version))" -ForegroundColor Green
} catch {
    Write-Host "  Service: NOT RUNNING — Start MAC-1.Service first!" -ForegroundColor Red
    Write-Host "  Run: dotnet run --project 'C:\Users\bilal\Desktop\MAC-1-master\MAC-1.Service'" -ForegroundColor Yellow
    exit 1
}

# Step 3: Test basic GET request
Write-Host "[3/6] Testing basic GET request..." -ForegroundColor Yellow
$testUrl = "https://datavaults.co/4clnh37qt2r5/Grand_Theft_Auto_Vice_City_%5BCONOR%5D.zip"
try {
    $response = Invoke-WebRequest -Uri $testUrl -Method HEAD -TimeoutSec 10 -UseBasicParsing
    Write-Host "  Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "  Content-Type: $($response.Headers['Content-Type'])"
    Write-Host "  Content-Length: $($response.Headers['Content-Length'])"
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Step 4: Instructions for manual test
Write-Host "[4/6] Manual Test Instructions:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. Open Chrome and navigate to:" -ForegroundColor White
Write-Host "     $testUrl" -ForegroundColor Cyan
Write-Host ""
Write-Host "  2. Wait for the page to load completely" -ForegroundColor White
Write-Host ""
Write-Host "  3. Click the 'Free Download' button (FIRST CLICK)" -ForegroundColor White
Write-Host "     - This may open a popup/ad — CLOSE IT" -ForegroundColor White
Write-Host ""
Write-Host "  4. Click the 'Free Download' button AGAIN (SECOND CLICK)" -ForegroundColor White
Write-Host "     - This should start the 20-second countdown" -ForegroundColor White
Write-Host ""
Write-Host "  5. WAIT for countdown to reach 0" -ForegroundColor White
Write-Host ""
Write-Host "  6. Click the 'Download' button that appears" -ForegroundColor White
Write-Host "     - Watch the Chrome download bar — what happens?" -ForegroundColor White
Write-Host ""
Write-Host "  7. Click the extension popup and click 'Export Debug Report'" -ForegroundColor White
Write-Host ""
Write-Host "  8. Save the report and share the contents" -ForegroundColor White
Write-Host ""

# Step 5: Alternative test — direct form submission
Write-Host "[5/6] Alternative: Direct Form Submission Test" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Open Chrome DevTools (F12) → Console tab and paste:" -ForegroundColor White
Write-Host ""
Write-Host @'
// This simulates the form submission flow
async function testDownloadFlow() {
    console.log('=== Starting download flow test ===');
    
    // Step 1: GET the download page
    console.log('[1] Fetching download page...');
    const pageResponse = await fetch("https://datavaults.co/4clnh37qt2r5/Grand_Theft_Auto_Vice_City_%5BCONOR%5D.zip", {
        credentials: 'include'
    });
    const pageHtml = await pageResponse.text();
    console.log('[1] Page length:', pageHtml.length);
    
    // Extract form data
    const parser = new DOMParser();
    const doc = parser.parseFromString(pageHtml, 'text/html');
    const form = doc.querySelector('form');
    if (!form) {
        console.log('[1] No form found!');
        return;
    }
    
    console.log('[1] Form action:', form.action);
    console.log('[1] Form method:', form.method);
    
    // Get all form fields
    const formData = {};
    form.querySelectorAll('input').forEach(input => {
        formData[input.name] = input.value;
        console.log('[1] Field:', input.name, '=', input.value);
    });
    
    // Step 2: Submit form (op=download1)
    console.log('[2] Submitting form (op=download1)...');
    const submitResponse = await fetch(form.action || window.location.href, {
        method: form.method || 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams(formData).toString(),
        credentials: 'include'
    });
    const submitHtml = await submitResponse.text();
    console.log('[2] Response length:', submitHtml.length);
    console.log('[2] Content-Type:', submitResponse.headers.get('content-type'));
    
    // Check if response is HTML or file
    if (submitHtml.includes('<html') || submitHtml.includes('<!DOCTYPE')) {
        console.log('[2] Response is HTML — not a file!');
        console.log('[2] Preview:', submitHtml.substring(0, 500));
        
        // Extract next form
        const doc2 = parser.parseFromString(submitHtml, 'text/html');
        const form2 = doc2.querySelector('form');
        if (form2) {
            console.log('[2] Next form action:', form2.action);
            const formData2 = {};
            form2.querySelectorAll('input').forEach(input => {
                formData2[input.name] = input.value;
                console.log('[2] Field:', input.name, '=', input.value);
            });
            
            // Step 3: Submit second form (op=download2)
            console.log('[3] Submitting form (op=download2) — waiting 22s first...');
            await new Promise(r => setTimeout(r, 22000));
            
            const submit2Response = await fetch(form2.action || window.location.href, {
                method: form2.method || 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: new URLSearchParams(formData2).toString(),
                credentials: 'include'
            });
            const submit2Html = await submit2Response.text();
            console.log('[3] Response length:', submit2Html.length);
            console.log('[3] Content-Type:', submit2Response.headers.get('content-type'));
            
            if (submit2Html.includes('<html') || submit2Html.includes('<!DOCTYPE')) {
                console.log('[3] Response is STILL HTML — form flow loops!');
            } else {
                console.log('[3] Response is a FILE! Size:', submit2Html.length);
            }
        }
    } else {
        console.log('[2] Response is a FILE! Size:', submitHtml.length);
    }
}

testDownloadFlow();
'@ -ForegroundColor DarkGray
Write-Host ""

# Step 6: Monitor logs
Write-Host "[6/6] To monitor Chrome extension logs:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. Open Chrome → chrome://extensions" -ForegroundColor White
Write-Host "  2. Find 'MAC-1 Download Manager'" -ForegroundColor White
Write-Host "  3. Click 'Service Worker' link (under 'Inspect views')" -ForegroundColor White
Write-Host "  4. This opens DevTools for the background script" -ForegroundColor White
Write-Host "  5. Watch the Console tab for [MAC-1-DEBUG] logs" -ForegroundColor White
Write-Host ""
Write-Host "  Also check the page console:" -ForegroundColor White
Write-Host "  - Open the download page" -ForegroundColor White
Write-Host "  - Press F12 → Console tab" -ForegroundColor White
Write-Host "  - Watch for [MAC-1-PAGE] logs" -ForegroundColor White
Write-Host ""
Write-Host "=== Ready! Trigger a download and export the debug report ===" -ForegroundColor Green
