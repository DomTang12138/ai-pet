param([switch]$CompileOnly, [switch]$Public, [string]$Python = 'python')

$ErrorActionPreference = 'Stop'

$runtimeRoot = $PSScriptRoot
$workspaceRoot = Split-Path $runtimeRoot -Parent
$outputRoot = Join-Path $runtimeRoot 'publish'
if ($Public) { $outputRoot = Join-Path $runtimeRoot 'public-publish' }
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET'
$compiler = @('Framework64','Framework') | ForEach-Object { Join-Path $frameworkRoot ($_ + '\v4.0.30319\csc.exe') } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compiler) {
    throw "The .NET Framework C# compiler was not found: $compiler"
}

function Find-GacAssembly([string]$name, [string[]]$roots) {
    foreach ($root in $roots) {
        $assemblyRoot = Join-Path $root $name
        if (Test-Path -LiteralPath $assemblyRoot) {
            $candidate = Get-ChildItem -LiteralPath $assemblyRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
            if ($candidate) {
                $assemblyPath = Join-Path $candidate.FullName ($name + '.dll')
                if (Test-Path -LiteralPath $assemblyPath) { return $assemblyPath }
            }
        }
    }
    throw "Required WPF assembly not found in the Windows GAC: $name"
}

$gacRoots = @(
    (Join-Path $frameworkRoot 'assembly\GAC_MSIL'),
    (Join-Path $frameworkRoot 'assembly\GAC_32')
)
$references = @(
    (Find-GacAssembly 'WindowsBase' $gacRoots),
    (Find-GacAssembly 'PresentationCore' $gacRoots),
    (Find-GacAssembly 'PresentationFramework' $gacRoots),
    (Find-GacAssembly 'System.Xaml' $gacRoots),
    (Find-GacAssembly 'System.IO.Compression' $gacRoots),
    (Find-GacAssembly 'System.IO.Compression.FileSystem' $gacRoots),
    (Join-Path (Split-Path $compiler -Parent) 'System.Windows.Forms.dll'),
    (Join-Path (Split-Path $compiler -Parent) 'System.Drawing.dll'),
    (Join-Path (Split-Path $compiler -Parent) 'System.Web.Extensions.dll')
)
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "Required assembly not found: $reference" }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$exePath = Join-Path $outputRoot 'AIPetDesktop.exe'
$sourcePath = Join-Path $runtimeRoot 'DesktopPet.cs'
$referencePath = Join-Path $workspaceRoot 'reference-cat.jpg'
$expectedReferenceSha256 = '112378A4BC93C8AE87AA71932F2309DDC41B6F3E3906C1E1E43701803409D209'
if (-not $Public -and (-not (Test-Path -LiteralPath $referencePath) -or
    (Get-FileHash -LiteralPath $referencePath -Algorithm SHA256).Hash -ne $expectedReferenceSha256)) {
    throw 'The bundled reference-cat.jpg does not match the user-provided test image.'
}
$runningPreview = Get-Process -Name 'AIPetDesktop' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exePath }
if ($runningPreview) {
    throw 'Close the running desktop preview from its tray menu before rebuilding.'
}
$arguments = @('/nologo', '/target:winexe', "/out:$exePath", ('/win32manifest:' + (Join-Path $runtimeRoot 'app.manifest'))) + ($references | ForEach-Object { "/reference:$_" }) + @($sourcePath, (Join-Path $runtimeRoot 'PetExperience.cs'), (Join-Path $runtimeRoot 'PetCodex.cs'), (Join-Path $runtimeRoot 'PetGeneration.cs'), (Join-Path $runtimeRoot 'PetDisplay.cs'), (Join-Path $runtimeRoot 'PetAppearance.cs'), (Join-Path $runtimeRoot 'PetCodexTitles.cs'), (Join-Path $runtimeRoot 'PetCodexFollow.cs'), (Join-Path $runtimeRoot 'PetVideoExperience.cs'), (Join-Path $runtimeRoot 'PetVideoRequests.cs'), (Join-Path $runtimeRoot 'PetVideoImport.cs'))
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $runtimeRoot 'AIPetDesktop.exe.config') -Destination ($exePath + '.config') -Force
if ($Public) {
    $demo = Start-Process -FilePath $exePath -ArgumentList @('--export-demo-photo', ('"' + (Join-Path $outputRoot 'reference-cat.jpg') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($demo.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $outputRoot 'reference-cat.jpg'))) { throw 'Public demo image generation failed.' }
    $webDemo = Start-Process -FilePath $exePath -ArgumentList @('--export-demo-assets', ('"' + $workspaceRoot + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($webDemo.ExitCode -ne 0) { throw 'Public Creator demo asset generation failed.' }
    $demoTheme = Start-Process -FilePath $exePath -ArgumentList @('--export-demo-theme', ('"' + (Join-Path $outputRoot 'public-default.lotheme') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($demoTheme.ExitCode -ne 0) { throw 'Public demo theme export failed.' }
    Write-Output "Public source build ready: $exePath"
    return
}
Copy-Item -LiteralPath $referencePath -Destination (Join-Path $outputRoot 'reference-cat.jpg') -Force
$motionPreviewPath = Join-Path $runtimeRoot 'assets\bundled-cat-motion-preview.lotheme'
$expectedMotionPreviewSha256 = '77FF4F13789859FB5CB608A385E3458644400BAF03E9E49DD48B4417167A4237'
if (-not (Test-Path -LiteralPath $motionPreviewPath) -or
    (Get-FileHash -LiteralPath $motionPreviewPath -Algorithm SHA256).Hash -ne $expectedMotionPreviewSha256) {
    throw 'The bundled cat motion preview has changed unexpectedly.'
}
Copy-Item -LiteralPath $motionPreviewPath -Destination (Join-Path $outputRoot 'bundled-cat-motion-preview.lotheme') -Force
$reviewedDefaultPath = Join-Path $runtimeRoot 'assets\reviewed-reference-cat.lotheme'
$reviewedDefaultHash = '63E9CBE074B53C818388229D9C1B5323DEE597C20788B27D0F077B89ECC9C5DD'
if (-not (Test-Path -LiteralPath $reviewedDefaultPath) -or
    (Get-FileHash -LiteralPath $reviewedDefaultPath -Algorithm SHA256).Hash -ne $reviewedDefaultHash) {
    throw 'The visually reviewed full-body reference theme fingerprint changed.'
}
Copy-Item -LiteralPath $reviewedDefaultPath -Destination (Join-Path $outputRoot 'reviewed-reference-cat.lotheme') -Force

if ($CompileOnly) { Write-Output "Built $exePath"; return }

# Regression processes must never inherit an actual user's tool connection or works.
$smokeEnvironmentNames = @('AI_PET_DATA_DIR','AI_PET_EVENT_DIR','AI_PET_REVIEW_LEDGER','CODEX_HOME')
$previousSmokeEnvironment = @{}
foreach ($name in $smokeEnvironmentNames) { $previousSmokeEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$smokeDataRoot = Join-Path $outputRoot ('build-smoke-data-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smokeDataRoot -Force | Out-Null
$env:AI_PET_DATA_DIR = $smokeDataRoot
$env:AI_PET_EVENT_DIR = Join-Path $smokeDataRoot 'codex-events'
$env:AI_PET_REVIEW_LEDGER = Join-Path $smokeDataRoot 'candidate-review-ledger.json'
$env:CODEX_HOME = Join-Path $smokeDataRoot 'codex'
try {

$placementResultPath = Join-Path $outputRoot 'window-placement-results.json'
$placementProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-window-placement', $placementResultPath) -PassThru -Wait -WindowStyle Hidden
if ($placementProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $placementResultPath)) {
    throw "Window placement geometry check failed (exit code $($placementProcess.ExitCode))."
}
$placementResult = Get-Content -LiteralPath $placementResultPath -Raw | ConvertFrom-Json
if (-not $placementResult.negativeMonitor -or -not $placementResult.resizePreservesPosition -or
    -not $placementResult.offsetAndTinyWorkArea -or $placementResult.detectedScreens -lt 1) {
    throw "Window placement geometry reported a failed assertion: $($placementResult | ConvertTo-Json -Compress)"
}
Add-Type -AssemblyName System.Drawing
$displayResultPath = Join-Path $smokeDataRoot 'display-compatibility.json'
$displayProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-display-compatibility', $displayResultPath) -PassThru -Wait -WindowStyle Hidden
if ($displayProcess.ExitCode -ne 0) { throw 'Native display compatibility failed.' }
$displayResult = Get-Content -LiteralPath $displayResultPath -Raw | ConvertFrom-Json
if (-not $displayResult.perMonitorV2 -or -not $displayResult.offscreenRecovery -or -not $displayResult.syntheticSettingsCallback) { throw 'Native display compatibility assertions failed.' }
$appearanceRoot = Join-Path $smokeDataRoot 'appearance'
$appearanceProcess = Start-Process -FilePath $exePath -ArgumentList @('--ai-test-appearance', $appearanceRoot) -PassThru -Wait -WindowStyle Hidden
if ($appearanceProcess.ExitCode -ne 0) { throw 'Modern appearance checks failed.' }
$appearanceResult = Get-Content -LiteralPath (Join-Path $appearanceRoot 'result.json') -Raw | ConvertFrom-Json
if (-not $appearanceResult.passed -or -not $appearanceResult.systemNotificationRefresh) { throw 'Modern appearance assertions failed.' }
$titleData = Join-Path $smokeDataRoot 'title-data'
$priorTitleData = $env:AI_PET_DATA_DIR
$priorTitleHome = $env:CODEX_HOME
$priorTitleEvents = $env:AI_PET_EVENT_DIR
try {
    $env:AI_PET_DATA_DIR = $titleData
    $env:CODEX_HOME = Join-Path $titleData 'codex'
    $env:AI_PET_EVENT_DIR = Join-Path $titleData 'codex-events'
    $titleRoot = Join-Path $smokeDataRoot 'session-titles'
    $titleProcess = Start-Process -FilePath $exePath -ArgumentList @('--ai-test-session-titles', $titleRoot) -PassThru -Wait -WindowStyle Hidden
    if ($titleProcess.ExitCode -ne 0) { throw 'Codex title checks failed.' }
    $titleResult = Get-Content -LiteralPath (Join-Path $titleRoot 'result.json') -Raw | ConvertFrom-Json
    if (-not $titleResult.passed) { throw 'Codex title assertions failed.' }
} finally {
    $env:AI_PET_DATA_DIR = $priorTitleData
    $env:CODEX_HOME = $priorTitleHome
    $env:AI_PET_EVENT_DIR = $priorTitleEvents
}


$autoData = Join-Path $smokeDataRoot 'auto-data'
$priorAutoData = $env:AI_PET_DATA_DIR
$priorAutoHome = $env:CODEX_HOME
$priorAutoEvents = $env:AI_PET_EVENT_DIR
try {
    $env:AI_PET_DATA_DIR = $autoData
    $env:CODEX_HOME = Join-Path $autoData 'codex'
    $env:AI_PET_EVENT_DIR = Join-Path $autoData 'codex-events'
    $autoRoot = Join-Path $smokeDataRoot 'codex-auto'
    $autoProcess = Start-Process -FilePath $exePath -ArgumentList @('--ai-test-codex-auto', $autoRoot) -PassThru -Wait -WindowStyle Hidden
    if ($autoProcess.ExitCode -ne 0) { throw 'Automatic Codex checks failed.' }
    $autoResult = Get-Content -LiteralPath (Join-Path $autoRoot 'result.json') -Raw | ConvertFrom-Json
    if (-not $autoResult.passed) { throw 'Automatic Codex assertions failed.' }
} finally {
    $env:AI_PET_DATA_DIR = $priorAutoData
    $env:CODEX_HOME = $priorAutoHome
    $env:AI_PET_EVENT_DIR = $priorAutoEvents
}

$bitmap = New-Object System.Drawing.Bitmap($referencePath)
$imageStream = New-Object System.IO.MemoryStream
$bitmap.Save($imageStream, [System.Drawing.Imaging.ImageFormat]::Png)
$portablePhoto = 'data:image/png;base64,' + [Convert]::ToBase64String($imageStream.ToArray())
$portableReferencePhoto = 'data:image/jpeg;base64,' + [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($referencePath))
$imageStream.Dispose()
$bitmap.Dispose()
$blinkAction = [String]::Concat([char]0x7728, [char]0x7728, [char]0x773C)
$sampleCard = @{
    name = 'Mimi'
    mode = 'real'
    photo = $portablePhoto
    photoName = 'reference-cat.jpg'
    action = $blinkAction
    prompt = 'Preserve the cat markings.'
    traits = @()
}
$samplePayload = @{ kind = 'little-orbit.character-card'; version = 1; status = 'confirmed'; character = $sampleCard }
$sampleJsonPath = Join-Path $outputRoot 'sample-role.little-orbit.json'
$sampleJson = ConvertTo-Json -InputObject $samplePayload -Depth 5 -Compress
[System.IO.File]::WriteAllText($sampleJsonPath, $sampleJson, (New-Object System.Text.UTF8Encoding($false)))
$referenceRolePreviewPath = Join-Path $outputRoot 'reference-role-transparent-preview.png'
$referenceRolePreviewReportPath = Join-Path $outputRoot 'reference-role-transparent-preview.json'
$referenceRolePreviewProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-reference-role-preview', $sampleJsonPath, $referenceRolePreviewPath, $referenceRolePreviewReportPath) -PassThru -Wait -WindowStyle Hidden
if ($referenceRolePreviewProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $referenceRolePreviewPath) -or
    -not (Test-Path -LiteralPath $referenceRolePreviewReportPath)) {
    throw "Reference role transparent fallback failed (exit code $($referenceRolePreviewProcess.ExitCode))."
}
$referenceRolePreviewReport = Get-Content -LiteralPath $referenceRolePreviewReportPath -Raw | ConvertFrom-Json
if (-not $referenceRolePreviewReport.roleMatchesReference -or -not $referenceRolePreviewReport.transparentPreview -or $referenceRolePreviewReport.whitePhotoCard) {
    throw "Reference role transparent fallback reported a photo card: $($referenceRolePreviewReport | ConvertTo-Json -Compress)"
}
$otherRole = $sampleJson | ConvertFrom-Json
$otherRole.character.name = 'Other dog'
$otherRole.character.photoName = 's32.jpg'
$otherRole.character.photo = 'data:image/jpeg;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $workspaceRoot 'third_party\LivePortrait-src\assets\examples\source\s32.jpg')))
$otherRolePath = Join-Path $outputRoot 'other-role.little-orbit.json'
[IO.File]::WriteAllText($otherRolePath, ($otherRole | ConvertTo-Json -Depth 5 -Compress), (New-Object System.Text.UTF8Encoding($false)))
$defaultReportPath = Join-Path $outputRoot 'reviewed-default-results.json'
$defaultProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-reviewed-default', $outputRoot, $otherRolePath, $defaultReportPath) -PassThru -Wait -WindowStyle Hidden
if ($defaultProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $defaultReportPath)) { throw 'Reviewed full-body default activation failed.' }
$defaultReport = Get-Content -LiteralPath $defaultReportPath -Raw | ConvertFrom-Json
if (-not $defaultReport.activatedAndBound -or -not $defaultReport.existingPreserved -or -not $defaultReport.otherPetProtected -or -not $defaultReport.tamperBlocked -or -not $defaultReport.vetoPreservedAndBlocked) { throw 'Reviewed default activation guard failed.' }
$referenceJpegRole = $sampleJson | ConvertFrom-Json
$referenceJpegRole.character.photo = $portableReferencePhoto
$referenceJpegRolePath = Join-Path $outputRoot 'reference-jpeg-role.little-orbit.json'
[IO.File]::WriteAllText($referenceJpegRolePath, ($referenceJpegRole | ConvertTo-Json -Depth 5 -Compress), (New-Object System.Text.UTF8Encoding($false)))
$otherRoleReportPath = Join-Path $outputRoot 'other-role-preview-rejection.json'
$otherRoleProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-reference-role-preview', $otherRolePath, $referenceRolePreviewPath, $otherRoleReportPath) -PassThru -Wait -WindowStyle Hidden
$otherRoleReport = Get-Content -LiteralPath $otherRoleReportPath -Raw | ConvertFrom-Json
if ($otherRoleProcess.ExitCode -ne 2 -or $otherRoleReport.roleMatchesReference -or $otherRoleReport.transparentPreview) {
    throw "A different pet was incorrectly shown as the reference cat: $($otherRoleReport | ConvertTo-Json -Compress)"
}
$smokeResultPath = Join-Path $outputRoot 'smoke-results.json'
foreach ($fallbackRole in @($referenceJpegRolePath, $otherRolePath)) {
    $fallbackReportPath = Join-Path $outputRoot (([IO.Path]::GetFileNameWithoutExtension($fallbackRole)) + '-no-photo-fallback.json')
    $fallbackProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-no-photo-fallback', $fallbackRole, $fallbackReportPath) -PassThru -Wait -WindowStyle Hidden
    if ($fallbackProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $fallbackReportPath)) {
        throw 'Missing-theme fallback check failed.'
    }
    $fallbackReport = Get-Content -LiteralPath $fallbackReportPath -Raw | ConvertFrom-Json
    if (-not $fallbackReport.passed) { throw "A runtime photo card or wrong pet was exposed: $fallbackReportPath" }
}
$smokeProcess = Start-Process -FilePath $exePath -ArgumentList @('--smoke-test', $smokeResultPath) -PassThru -Wait -WindowStyle Hidden
if ($smokeProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $smokeResultPath)) {
    throw "The WPF runtime smoke check failed (exit code $($smokeProcess.ExitCode))."
}
$smokeResult = Get-Content -LiteralPath $smokeResultPath -Raw | ConvertFrom-Json
if (-not $smokeResult.visibleAtLaunch -or -not $smokeResult.hideWorks -or -not $smokeResult.showWorks -or -not $smokeResult.trayRegistered -or
    -not $smokeResult.themeExportMenuPresent -or
    -not $smokeResult.photoDecoded -or -not $smokeResult.interactionToggle -or -not $smokeResult.sampleFramingWarning -or
    -not $smokeResult.positionPreservedOnRebuild -or -not $smokeResult.placementRoundTrip -or
    -not $smokeResult.screenMenuPresent -or -not $smokeResult.screenMenuMoveWorks) {
    throw "The WPF runtime smoke check reported a failed assertion: $($smokeResult | ConvertTo-Json -Compress)"
}
$restoreResultPath = Join-Path $outputRoot 'window-restore-results.json'
$restoreProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-window-restore', ($smokeResultPath + '.placement.json'), $restoreResultPath) -PassThru -Wait -WindowStyle Hidden
if ($restoreProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $restoreResultPath)) {
    throw "The saved window placement restore check failed (exit code $($restoreProcess.ExitCode))."
}
$restoreResult = Get-Content -LiteralPath $restoreResultPath -Raw | ConvertFrom-Json
if (-not $restoreResult.restored -or -not $restoreResult.photoDecoded) {
    throw "Saved window placement was not restored with the reference cat: $($restoreResult | ConvertTo-Json -Compress)"
}
$preferencesFixturePath = Join-Path $outputRoot 'preferences-fixture.json'
$preferencesFixture = '{"kind":"little-orbit.preferences","version":2,"preferences":{"automaticActionsEnabled":true,"frequencyMinutes":25,"interactionEnabled":false,"quietEnabled":true,"quietEnd":"07:45","quietStart":"23:15"}}'
[System.IO.File]::WriteAllText($preferencesFixturePath, $preferencesFixture, (New-Object System.Text.UTF8Encoding($false)))
$preferencesRoundTripPath = Join-Path $outputRoot 'preferences-roundtrip.json'
$preferencesProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-preferences', $preferencesFixturePath, $preferencesRoundTripPath) -PassThru -Wait -WindowStyle Hidden
$preferencesResultPath = $preferencesRoundTripPath + '.results.json'
if ($preferencesProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $preferencesResultPath)) {
    throw "Native preference persistence check failed (exit code $($preferencesProcess.ExitCode))."
}
$preferencesResult = Get-Content -LiteralPath $preferencesResultPath -Raw | ConvertFrom-Json
if (-not $preferencesResult.persisted -or -not $preferencesResult.portableRoundTrip -or -not $preferencesResult.automaticActionsEnabled -or
    -not $preferencesResult.automaticActionsPersisted -or $preferencesResult.interactionEnabled -or $preferencesResult.quietHours -ne '23:15-07:45' -or
    -not $preferencesResult.quietAtStart -or -not $preferencesResult.quietBeforeEnd -or $preferencesResult.quietAtEnd -or $preferencesResult.frequencyMinutes -ne 25) {
    throw "Native preference persistence reported an invalid result: $($preferencesResult | ConvertTo-Json -Compress)"
}
$legacyPreferencesFixturePath = Join-Path $outputRoot 'legacy-preferences-v1.json'
[System.IO.File]::WriteAllText($legacyPreferencesFixturePath, '{"kind":"little-orbit.preferences","version":1,"preferences":{"frequencyMinutes":25,"interactionEnabled":false,"quietEnabled":true,"quietEnd":"07:45","quietStart":"23:15"}}', (New-Object System.Text.UTF8Encoding($false)))
$legacyPreferencesResultPath = Join-Path $outputRoot 'legacy-preferences-v1-roundtrip.json'
$legacyPreferencesProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-preferences', $legacyPreferencesFixturePath, $legacyPreferencesResultPath) -PassThru -Wait -WindowStyle Hidden
$legacyPreferencesResult = Get-Content -LiteralPath ($legacyPreferencesResultPath + '.results.json') -Raw | ConvertFrom-Json
if ($legacyPreferencesProcess.ExitCode -ne 0 -or -not $legacyPreferencesResult.persisted -or
    $legacyPreferencesResult.automaticActionsEnabled -or -not $legacyPreferencesResult.automaticActionsPersisted) {
    throw "Legacy v1 preferences did not default automatic actions off: $($legacyPreferencesResult | ConvertTo-Json -Compress)"
}
$invalidPreferencesPath = Join-Path $outputRoot 'invalid-preferences.json'
[System.IO.File]::WriteAllText($invalidPreferencesPath, '{"kind":"little-orbit.preferences","version":2,"preferences":{"automaticActionsEnabled":false,"frequencyMinutes":17,"interactionEnabled":true,"quietEnabled":false,"quietEnd":"08:00","quietStart":"22:00"}}', (New-Object System.Text.UTF8Encoding($false)))
$invalidPreferencesResultPath = Join-Path $outputRoot 'invalid-preferences-roundtrip.json'
$invalidPreferencesProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-preferences', $invalidPreferencesPath, $invalidPreferencesResultPath) -PassThru -Wait -WindowStyle Hidden
$invalidPreferencesResult = Get-Content -LiteralPath ($invalidPreferencesResultPath + '.results.json') -Raw | ConvertFrom-Json
if ($invalidPreferencesProcess.ExitCode -ne 2 -or $invalidPreferencesResult.persisted -or $invalidPreferencesResult.error -ne 'invalid-preferences') {
    throw "Native preferences accepted a frequency outside the fixed 5-minute steps."
}
$preferencesImagePath = Join-Path $outputRoot 'preferences-dialog.png'
$preferencesViewResultPath = Join-Path $outputRoot 'preferences-dialog-results.json'
$preferencesViewProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-preferences', $preferencesImagePath, $preferencesViewResultPath) -PassThru -Wait -WindowStyle Hidden
if ($preferencesViewProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $preferencesImagePath) -or -not (Test-Path -LiteralPath $preferencesViewResultPath)) {
    throw "Native preferences dialog render failed (exit code $($preferencesViewProcess.ExitCode))."
}
$preferencesViewResult = Get-Content -LiteralPath $preferencesViewResultPath -Raw | ConvertFrom-Json
if (-not $preferencesViewResult.rendered -or $preferencesViewResult.saved -or $preferencesViewResult.size -ne '470x550') {
    throw "Native preferences dialog reported an invalid render: $($preferencesViewResult | ConvertTo-Json -Compress)"
}
$importPreviewPath = Join-Path $outputRoot 'import-preview.png'
$importResultPath = Join-Path $outputRoot 'import-results.json'
$importProcess = Start-Process -FilePath $exePath -ArgumentList @('--preview-import', $sampleJsonPath, $importPreviewPath, $importResultPath) -PassThru -Wait -WindowStyle Hidden
if ($importProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $importResultPath) -or -not (Test-Path -LiteralPath $importPreviewPath)) {
    throw "The portable role-card import check failed (exit code $($importProcess.ExitCode))."
}
$importResult = Get-Content -LiteralPath $importResultPath -Raw | ConvertFrom-Json
if (-not $importResult.imported -or $importResult.name -ne 'Mimi' -or -not $importResult.photoDecoded -or
    -not $importResult.photoPixels.StartsWith('380x') -or $importResult.sampleFramingWarning) {
    throw "The role-card import check reported a failed assertion: $($importResult | ConvertTo-Json -Compress)"
}
$v2Card = @{} + $sampleCard
$v2Card.sourceEvidence = @('face', 'frontPaws')
$v2Payload = @{ kind = 'little-orbit.character-card'; version = 2; status = 'draft'; character = $v2Card }
$v2Path = Join-Path $outputRoot 'source-evidence-role.little-orbit.json'
[System.IO.File]::WriteAllText($v2Path, (ConvertTo-Json -InputObject $v2Payload -Depth 5 -Compress), (New-Object System.Text.UTF8Encoding($false)))
$v2ResultPath = Join-Path $outputRoot 'source-evidence-import-results.json'
$v2PreviewPath = Join-Path $outputRoot 'source-evidence-import.png'
$v2Process = Start-Process -FilePath $exePath -ArgumentList @('--preview-import', $v2Path, $v2PreviewPath, $v2ResultPath) -PassThru -Wait -WindowStyle Hidden
$v2Result = Get-Content -LiteralPath $v2ResultPath -Raw | ConvertFrom-Json
if ($v2Process.ExitCode -ne 0 -or -not $v2Result.imported -or $v2Result.fullSilhouetteClaimed -or -not (Test-Path -LiteralPath $v2PreviewPath)) {
    throw "The v2 source-evidence card did not import safely: $($v2Result | ConvertTo-Json -Compress)"
}
$v3Card = @{} + $v2Card
$v3Card.referencePhotos = @(@{ photo = $portableReferencePhoto; photoName = 'reference-cat-side.jpg' })
$v3Payload = @{ kind = 'little-orbit.character-card'; version = 3; status = 'draft'; character = $v3Card }
$v3Path = Join-Path $outputRoot 'multi-photo-role.little-orbit.json'
[System.IO.File]::WriteAllText($v3Path, (ConvertTo-Json -InputObject $v3Payload -Depth 6 -Compress), (New-Object System.Text.UTF8Encoding($false)))
$v3ResultPath = Join-Path $outputRoot 'multi-photo-import-results.json'
$v3PreviewPath = Join-Path $outputRoot 'multi-photo-import.png'
$v3Process = Start-Process -FilePath $exePath -ArgumentList @('--preview-import', $v3Path, $v3PreviewPath, $v3ResultPath) -PassThru -Wait -WindowStyle Hidden
$v3Result = Get-Content -LiteralPath $v3ResultPath -Raw | ConvertFrom-Json
if ($v3Process.ExitCode -ne 0 -or -not $v3Result.imported -or $v3Result.referencePhotoCount -ne 1 -or
    -not $v3Result.photoDecoded -or -not (Test-Path -LiteralPath $v3PreviewPath)) {
    throw "The v3 multi-photo role card did not import safely: $($v3Result | ConvertTo-Json -Compress)"
}
$v4Card = @{} + $v2Card
$v4Card.referencePhotos = @()
$v4Card.photoReview = @{ subjectCount = 'one'; occlusion = 'present' }
$v4Payload = @{ kind = 'little-orbit.character-card'; version = 4; status = 'draft'; character = $v4Card }
$v4Path = Join-Path $outputRoot 'photo-review-role.little-orbit.json'
[System.IO.File]::WriteAllText($v4Path, (ConvertTo-Json -InputObject $v4Payload -Depth 6 -Compress), (New-Object System.Text.UTF8Encoding($false)))
$v4ResultPath = Join-Path $outputRoot 'photo-review-import-results.json'
$v4PreviewPath = Join-Path $outputRoot 'photo-review-import.png'
$v4Process = Start-Process -FilePath $exePath -ArgumentList @('--preview-import', $v4Path, $v4PreviewPath, $v4ResultPath) -PassThru -Wait -WindowStyle Hidden
$v4Result = Get-Content -LiteralPath $v4ResultPath -Raw | ConvertFrom-Json
if ($v4Process.ExitCode -ne 0 -or -not $v4Result.imported -or $v4Result.subjectCountReview -ne 'one' -or
    $v4Result.occlusionReview -ne 'present' -or -not $v4Result.photoDecoded -or -not (Test-Path -LiteralPath $v4PreviewPath)) {
    throw "The v4 human photo-review role card did not import safely: $($v4Result | ConvertTo-Json -Compress)"
}
$badPhotoReviewCard = @{} + $v4Card
$badPhotoReviewCard.photoReview = @{ subjectCount = 'two-ish'; occlusion = 'present' }
$badPhotoReviewPayload = @{} + $v4Payload
$badPhotoReviewPayload.character = $badPhotoReviewCard
$badPhotoReviewPath = Join-Path $outputRoot 'rejected-invalid-photo-review.little-orbit.json'
[System.IO.File]::WriteAllText($badPhotoReviewPath, (ConvertTo-Json -InputObject $badPhotoReviewPayload -Depth 6 -Compress), (New-Object System.Text.UTF8Encoding($false)))
$badPhotoReviewResultPath = Join-Path $outputRoot 'rejected-invalid-photo-review-results.json'
$badPhotoReviewPreviewPath = Join-Path $outputRoot 'rejected-invalid-photo-review.png'
$badPhotoReviewProcess = Start-Process -FilePath $exePath -ArgumentList @('--preview-import', $badPhotoReviewPath, $badPhotoReviewPreviewPath, $badPhotoReviewResultPath) -PassThru -Wait -WindowStyle Hidden
$badPhotoReviewResult = Get-Content -LiteralPath $badPhotoReviewResultPath -Raw | ConvertFrom-Json
if ($badPhotoReviewProcess.ExitCode -ne 2 -or $badPhotoReviewResult.imported -or (Test-Path -LiteralPath $badPhotoReviewPreviewPath)) {
    throw 'The v4 role-card importer accepted an unknown human photo-review value.'
}
$badReferenceCard = @{} + $v3Card
$badReferenceCard.referencePhotos = @(@{ photo = 'https://example.invalid/cat.jpg'; photoName = 'external.jpg' })
$badReferencePayload = @{} + $v3Payload
$badReferencePayload.character = $badReferenceCard
$badReferencePath = Join-Path $outputRoot 'rejected-external-reference-photo.little-orbit.json'
[System.IO.File]::WriteAllText($badReferencePath, (ConvertTo-Json -InputObject $badReferencePayload -Depth 6 -Compress), (New-Object System.Text.UTF8Encoding($false)))
$badReferenceResultPath = Join-Path $outputRoot 'rejected-external-reference-results.json'
$badReferencePreviewPath = Join-Path $outputRoot 'rejected-external-reference.png'
$badReferenceProcess = Start-Process -FilePath $exePath -ArgumentList @('--preview-import', $badReferencePath, $badReferencePreviewPath, $badReferenceResultPath) -PassThru -Wait -WindowStyle Hidden
$badReferenceResult = Get-Content -LiteralPath $badReferenceResultPath -Raw | ConvertFrom-Json
if ($badReferenceProcess.ExitCode -ne 2 -or $badReferenceResult.imported -or (Test-Path -LiteralPath $badReferencePreviewPath)) {
    throw 'The v3 role-card importer accepted an external supplementary photo.'
}
$badEvidenceCard = @{} + $v2Card
$badEvidenceCard.sourceEvidence = @('face', 'face')
$badEvidencePayload = @{} + $v2Payload
$badEvidencePayload.character = $badEvidenceCard
$badEvidencePath = Join-Path $outputRoot 'rejected-duplicate-evidence.little-orbit.json'
[System.IO.File]::WriteAllText($badEvidencePath, (ConvertTo-Json -InputObject $badEvidencePayload -Depth 5 -Compress), (New-Object System.Text.UTF8Encoding($false)))
$badEvidenceResultPath = Join-Path $outputRoot 'rejected-duplicate-evidence-results.json'
$badEvidencePreviewPath = Join-Path $outputRoot 'rejected-duplicate-evidence.png'
$badEvidenceProcess = Start-Process -FilePath $exePath -ArgumentList @('--preview-import', $badEvidencePath, $badEvidencePreviewPath, $badEvidenceResultPath) -PassThru -Wait -WindowStyle Hidden
$badEvidenceResult = Get-Content -LiteralPath $badEvidenceResultPath -Raw | ConvertFrom-Json
if ($badEvidenceProcess.ExitCode -ne 2 -or $badEvidenceResult.imported -or (Test-Path -LiteralPath $badEvidencePreviewPath)) {
    throw "The importer accepted duplicate source-evidence claims."
}
$badSampleCard = @{} + $sampleCard
$badSampleCard.photo = 'https://example.invalid/cat.png'
$badSamplePayload = @{} + $samplePayload
$badSamplePayload.character = $badSampleCard
$badSamplePath = Join-Path $outputRoot 'rejected-external-photo.little-orbit.json'
$badSampleJson = ConvertTo-Json -InputObject $badSamplePayload -Depth 5 -Compress
[System.IO.File]::WriteAllText($badSamplePath, $badSampleJson, (New-Object System.Text.UTF8Encoding($false)))
$rejectedPreviewPath = Join-Path $outputRoot 'rejected-external-photo.png'
$rejectionResultPath = Join-Path $outputRoot 'rejection-results.json'
$rejectionProcess = Start-Process -FilePath $exePath -ArgumentList @('--preview-import', $badSamplePath, $rejectedPreviewPath, $rejectionResultPath) -PassThru -Wait -WindowStyle Hidden
if ($rejectionProcess.ExitCode -ne 2 -or -not (Test-Path -LiteralPath $rejectionResultPath) -or (Test-Path -LiteralPath $rejectedPreviewPath)) {
    throw "The external-photo rejection check failed (exit code $($rejectionProcess.ExitCode))."
}
$rejectionResult = Get-Content -LiteralPath $rejectionResultPath -Raw | ConvertFrom-Json
if ($rejectionResult.imported -or $rejectionResult.error -ne 'invalid-role-card') {
    throw "The role-card importer did not reject an external photo: $($rejectionResult | ConvertTo-Json -Compress)"
}
$persistenceDirectory = Join-Path $outputRoot 'persistence-smoke'
$persistencePath = Join-Path $persistenceDirectory 'active-role.little-orbit.json'
$persistenceResultPath = Join-Path $outputRoot 'persistence-results.json'
$persistenceProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-role-persistence', $sampleJsonPath, $persistencePath, $persistenceResultPath) -PassThru -Wait -WindowStyle Hidden
if ($persistenceProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $persistenceResultPath)) {
    throw "The local role persistence check failed (exit code $($persistenceProcess.ExitCode))."
}
$persistenceResult = Get-Content -LiteralPath $persistenceResultPath -Raw | ConvertFrom-Json
if (-not $persistenceResult.persisted -or $persistenceResult.name -ne 'Mimi' -or -not $persistenceResult.photoDecoded) {
    throw "The local role persistence check reported a failed assertion: $($persistenceResult | ConvertTo-Json -Compress)"
}
$replacementPayload = $sampleJson | ConvertFrom-Json
$replacementPayload.character.name = 'Mimi v2'
$replacementJsonPath = Join-Path $outputRoot 'replacement-role.little-orbit.json'
$replacementJson = ConvertTo-Json -InputObject $replacementPayload -Depth 5 -Compress
[System.IO.File]::WriteAllText($replacementJsonPath, $replacementJson, (New-Object System.Text.UTF8Encoding($false)))
$roleRollbackRoot = Join-Path $outputRoot 'role-rollback-smoke'
New-Item -ItemType Directory -Path $roleRollbackRoot -Force | Out-Null
$roleRollbackResultPath = Join-Path $outputRoot 'role-rollback-results.json'
$roleRollbackProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-role-rollback', $sampleJsonPath, $replacementJsonPath, $roleRollbackRoot, $roleRollbackResultPath) -PassThru -Wait -WindowStyle Hidden
if ($roleRollbackProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $roleRollbackResultPath)) {
    throw "The local role rollback check failed (exit code $($roleRollbackProcess.ExitCode))."
}
$roleRollbackResult = Get-Content -LiteralPath $roleRollbackResultPath -Raw | ConvertFrom-Json
if (-not $roleRollbackResult.installed -or -not $roleRollbackResult.previousPreserved -or -not $roleRollbackResult.rolledBack -or
    $roleRollbackResult.active -ne 'Mimi' -or $roleRollbackResult.previous -ne 'Mimi v2') {
    throw "Role rollback reported an invalid result: $($roleRollbackResult | ConvertTo-Json -Compress)"
}
$roleReviewPath = Join-Path $outputRoot 'role-rollback-review.png'
$roleReviewResultPath = Join-Path $outputRoot 'role-rollback-review-results.json'
$roleReviewProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-role-review', $sampleJsonPath, $roleReviewPath, $roleReviewResultPath) -PassThru -Wait -WindowStyle Hidden
if ($roleReviewProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $roleReviewPath) -or -not (Test-Path -LiteralPath $roleReviewResultPath)) {
    throw "The WPF role review dialog render failed (exit code $($roleReviewProcess.ExitCode))."
}
$roleReviewResult = Get-Content -LiteralPath $roleReviewResultPath -Raw | ConvertFrom-Json
if (-not $roleReviewResult.rendered -or $roleReviewResult.restored -or $roleReviewResult.name -ne 'Mimi' -or $roleReviewResult.size -ne '454x500') {
    throw "The WPF role review dialog reported an invalid result: $($roleReviewResult | ConvertTo-Json -Compress)"
}
$previewPath = Join-Path $outputRoot 'desktop-preview.png'
$previewProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-preview', $previewPath) -PassThru -Wait -WindowStyle Hidden
if ($previewProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $previewPath)) {
    throw "The WPF preview render failed (exit code $($previewProcess.ExitCode))."
}
$themeFixturePath = Join-Path $workspaceRoot 'generated\rejected-segmentation\reference-cat-manual.lotheme'
if (-not (Test-Path -LiteralPath $themeFixturePath)) { throw "The reference theme fixture was not found: $themeFixturePath" }
$reviewFingerprintRoot = Join-Path $outputRoot 'theme-review-fingerprint-smoke'
$reviewFingerprintResultPath = Join-Path $outputRoot 'theme-review-fingerprint-results.json'
$reviewFingerprintProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-review-fingerprint', $themeFixturePath, $reviewFingerprintRoot, $reviewFingerprintResultPath) -PassThru -Wait -WindowStyle Hidden
if ($reviewFingerprintProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reviewFingerprintResultPath)) {
    throw "The theme review fingerprint gate failed (exit code $($reviewFingerprintProcess.ExitCode))."
}
$reviewFingerprintResult = Get-Content -LiteralPath $reviewFingerprintResultPath -Raw | ConvertFrom-Json
if (-not $reviewFingerprintResult.mutationRejected -or -not $reviewFingerprintResult.vetoAfterReviewBlocked -or -not $reviewFingerprintResult.isolated) {
    throw "Theme review fingerprint gate returned an invalid result: $($reviewFingerprintResult | ConvertTo-Json -Compress)"
}
$pendingHandoffRoot = Join-Path $outputRoot 'pending-role-handoff-smoke'
$pendingHandoffResultPath = Join-Path $outputRoot 'pending-role-handoff-results.json'
$pendingHandoffProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-pending-role-handoff', $sampleJsonPath, $otherRolePath, $themeFixturePath, $pendingHandoffRoot, $pendingHandoffResultPath) -PassThru -Wait -WindowStyle Hidden
if ($pendingHandoffProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $pendingHandoffResultPath)) {
    throw "The pending-role handoff check failed (exit code $($pendingHandoffProcess.ExitCode))."
}
$pendingHandoffResult = Get-Content -LiteralPath $pendingHandoffResultPath -Raw | ConvertFrom-Json
if (-not $pendingHandoffResult.oldRoleStayedActive -or -not $pendingHandoffResult.pendingReloaded -or -not $pendingHandoffResult.mutationRejected -or -not $pendingHandoffResult.failureRestored -or
    -not $pendingHandoffResult.switchedTogether -or -not $pendingHandoffResult.isolated -or $pendingHandoffResult.visualApproval) {
    throw "Pending-role handoff reported an invalid result: $($pendingHandoffResult | ConvertTo-Json -Compress)"
}
$themeRoleBindingRoot = Join-Path $outputRoot 'theme-role-binding-smoke'
$themeRoleBindingResultPath = Join-Path $outputRoot 'theme-role-binding-results.json'
$themeRoleBindingProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-theme-role-binding', $sampleJsonPath, $otherRolePath, $themeFixturePath, $themeRoleBindingRoot, $themeRoleBindingResultPath, $referenceJpegRolePath) -PassThru -Wait -WindowStyle Hidden
if ($themeRoleBindingProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $themeRoleBindingResultPath)) {
    throw "Role-bound theme safety check failed (exit code $($themeRoleBindingProcess.ExitCode))."
}
$themeRoleBindingResult = Get-Content -LiteralPath $themeRoleBindingResultPath -Raw | ConvertFrom-Json
if (-not $themeRoleBindingResult.matched -or -not $themeRoleBindingResult.reencodedMatched -or -not $themeRoleBindingResult.otherBlocked -or
    -not $themeRoleBindingResult.restored -or -not $themeRoleBindingResult.replacedPackageBlocked -or
    -not $themeRoleBindingResult.runtimeSuspended -or -not $themeRoleBindingResult.runtimeRestored) {
    throw "Role-bound theme safety check reported a mismatch: $($themeRoleBindingResult | ConvertTo-Json -Compress)"
}
$themeExportPath = Join-Path $outputRoot 'theme-export-smoke.lotheme'
$themeExportResultPath = Join-Path $outputRoot 'theme-export-results.json'
$themeExportProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-theme-export', $themeFixturePath, $themeExportPath, $themeExportResultPath) -PassThru -Wait -WindowStyle Hidden
if ($themeExportProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $themeExportPath) -or -not (Test-Path -LiteralPath $themeExportResultPath)) {
    throw "Native theme export check failed (exit code $($themeExportProcess.ExitCode))."
}
$themeExportResult = Get-Content -LiteralPath $themeExportResultPath -Raw | ConvertFrom-Json
$originalThemeHash = (Get-FileHash -LiteralPath $themeFixturePath -Algorithm SHA256).Hash.ToLowerInvariant()
$exportedThemeHash = (Get-FileHash -LiteralPath $themeExportPath -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not $themeExportResult.passed -or -not $themeExportResult.sameBytes -or
    $themeExportResult.sha256 -ne $originalThemeHash -or $exportedThemeHash -ne $originalThemeHash) {
    throw "Native theme export changed the package bytes: $($themeExportResult | ConvertTo-Json -Compress)"
}
$invalidThemeExportPath = Join-Path $outputRoot 'theme-export-invalid.json'
$invalidThemeExportResultPath = Join-Path $outputRoot 'theme-export-invalid-results.json'
if (Test-Path -LiteralPath $invalidThemeExportPath) { Remove-Item -LiteralPath $invalidThemeExportPath -Force }
$invalidThemeExportProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-theme-export', $themeFixturePath, $invalidThemeExportPath, $invalidThemeExportResultPath) -PassThru -Wait -WindowStyle Hidden
$invalidThemeExportResult = Get-Content -LiteralPath $invalidThemeExportResultPath -Raw | ConvertFrom-Json
if ($invalidThemeExportProcess.ExitCode -ne 2 -or (Test-Path -LiteralPath $invalidThemeExportPath) -or
    $invalidThemeExportResult.passed -or $invalidThemeExportResult.error -ne 'theme-export-failed') {
    throw "Native theme export accepted a non-.lotheme destination."
}
$themePreviewPath = Join-Path $outputRoot 'theme-runtime-preview.png'
$themeResultPath = Join-Path $outputRoot 'theme-import-results.json'
$themeProcess = Start-Process -FilePath $exePath -ArgumentList @('--preview-theme', $themeFixturePath, $themePreviewPath, $themeResultPath) -PassThru -Wait -WindowStyle Hidden
if ($themeProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $themePreviewPath) -or -not (Test-Path -LiteralPath $themeResultPath)) {
    throw "The WPF theme preview check failed (exit code $($themeProcess.ExitCode))."
}
$themeResult = Get-Content -LiteralPath $themeResultPath -Raw | ConvertFrom-Json
if (-not $themeResult.loaded -or $themeResult.name -ne 'Reference Cat Review' -or $themeResult.slot -ne 'idle' -or
    $themeResult.frameCount -ne 1 -or $themeResult.canvas -ne '1279x1706' -or $themeResult.previewPixels -ne '190x254' -or
    $themeResult.installed -or -not $themeResult.reviewRequired) {
    throw "The WPF theme preview reported an invalid result: $($themeResult | ConvertTo-Json -Compress)"
}
$reviewDialogPath = Join-Path $outputRoot 'theme-review-dialog.png'
$reviewDialogResultPath = Join-Path $outputRoot 'theme-review-dialog-results.json'
$reviewDialogProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-theme-review', $themeFixturePath, $reviewDialogPath, $reviewDialogResultPath) -PassThru -Wait -WindowStyle Hidden
if ($reviewDialogProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reviewDialogPath) -or -not (Test-Path -LiteralPath $reviewDialogResultPath)) {
    throw "The WPF theme review dialog render failed (exit code $($reviewDialogProcess.ExitCode))."
}
$reviewDialogResult = Get-Content -LiteralPath $reviewDialogResultPath -Raw | ConvertFrom-Json
if (-not $reviewDialogResult.rendered -or $reviewDialogResult.installed -or
    $reviewDialogResult.size -ne '650x600' -or $reviewDialogResult.actionCount -ne 1) {
    throw "The WPF theme review dialog reported an invalid result: $($reviewDialogResult | ConvertTo-Json -Compress)"
}
$manualMattePath = Join-Path $workspaceRoot 'generated\rejected-segmentation\reference-cat-manual-matte.png'
if (-not (Test-Path -LiteralPath $manualMattePath)) { throw "The reference RGBA fixture was not found: $manualMattePath" }
Add-Type -AssemblyName System.IO.Compression
$multiReviewThemePath = Join-Path $outputRoot 'theme-multi-action-review.lotheme'
$multiReviewStream = [System.IO.File]::Open($multiReviewThemePath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
$multiReviewArchive = New-Object System.IO.Compression.ZipArchive($multiReviewStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
$multiReviewManifest = $multiReviewArchive.CreateEntry('manifest.json')
$multiReviewWriter = New-Object System.IO.StreamWriter($multiReviewManifest.Open(), (New-Object System.Text.UTF8Encoding($false)))
$multiReviewWriter.Write('{"actions":[{"fps":10,"frames":["frames/idle/0001.png","frames/idle/0002.png"],"loop":true,"slot":"idle"},{"fps":10,"frames":["frames/hover/0001.png","frames/hover/0002.png"],"loop":true,"slot":"hover"},{"fps":10,"frames":["frames/click/0001.png","frames/click/0002.png"],"loop":false,"slot":"click"}],"format":"little-orbit.theme","name":"Cat multi-action review fixture","review":"required","version":1}')
$multiReviewWriter.Dispose()
foreach ($slot in @('idle', 'hover', 'click')) {
    foreach ($frameNumber in 1..2) {
        $frameEntry = $multiReviewArchive.CreateEntry(('frames/{0}/{1:D4}.png' -f $slot, $frameNumber))
        $frameOutput = $frameEntry.Open()
        $frameInput = [System.IO.File]::OpenRead($manualMattePath)
        $frameInput.CopyTo($frameOutput)
        $frameInput.Dispose()
        $frameOutput.Dispose()
    }
}
$multiReviewArchive.Dispose()
$multiReviewStream.Dispose()
$multiReviewImagePath = Join-Path $outputRoot 'theme-multi-action-review.png'
$multiReviewResultPath = Join-Path $outputRoot 'theme-multi-action-review-results.json'
$multiReviewProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-theme-review', $multiReviewThemePath, $multiReviewImagePath, $multiReviewResultPath) -PassThru -Wait -WindowStyle Hidden
if ($multiReviewProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $multiReviewImagePath) -or -not (Test-Path -LiteralPath $multiReviewResultPath)) {
    throw "The multi-action WPF review dialog render failed (exit code $($multiReviewProcess.ExitCode))."
}
$multiReviewResult = Get-Content -LiteralPath $multiReviewResultPath -Raw | ConvertFrom-Json
if (-not $multiReviewResult.rendered -or $multiReviewResult.installed -or
    $multiReviewResult.size -ne '650x600' -or $multiReviewResult.actionCount -ne 3) {
    throw "The multi-action WPF review dialog reported an invalid result: $($multiReviewResult | ConvertTo-Json -Compress)"
}
$allThemeSlots = @('idle', 'ambient', 'recent-activity', 'needs-user', 'stop-unknown', 'interrupt', 'hover', 'click')
$allSlotActions = @($allThemeSlots | ForEach-Object {
    @{ slot = $_; fps = 10; loop = ($_ -eq 'idle' -or $_ -eq 'hover'); frames = @(('frames/{0}/0001.png' -f $_), ('frames/{0}/0002.png' -f $_)) }
})
$allSlotManifest = @{ format = 'little-orbit.theme'; version = 1; name = 'UNAPPROVED reference cat eight-slot QA'; review = 'required'; actions = $allSlotActions } |
    ConvertTo-Json -Depth 6 -Compress
$allSlotThemePath = Join-Path $outputRoot 'theme-eight-slot-qa.lotheme'
$allSlotStream = [System.IO.File]::Open($allSlotThemePath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
$allSlotArchive = New-Object System.IO.Compression.ZipArchive($allSlotStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
$manifestEntry = $allSlotArchive.CreateEntry('manifest.json')
$manifestWriter = New-Object System.IO.StreamWriter($manifestEntry.Open(), (New-Object System.Text.UTF8Encoding($false)))
$manifestWriter.Write($allSlotManifest)
$manifestWriter.Dispose()
foreach ($slot in $allThemeSlots) {
    foreach ($frameNumber in 1..2) {
        $entry = $allSlotArchive.CreateEntry(('frames/{0}/{1:D4}.png' -f $slot, $frameNumber))
        $output = $entry.Open()
        $input = [System.IO.File]::OpenRead($manualMattePath)
        $input.CopyTo($output)
        $input.Dispose()
        $output.Dispose()
    }
}
$allSlotArchive.Dispose()
$allSlotStream.Dispose()
$allSlotReviewImage = Join-Path $outputRoot 'theme-eight-slot-review.png'
$allSlotReviewResult = Join-Path $outputRoot 'theme-eight-slot-review-results.json'
$allSlotReviewProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-theme-review', $allSlotThemePath, $allSlotReviewImage, $allSlotReviewResult) -PassThru -Wait -WindowStyle Hidden
if ($allSlotReviewProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $allSlotReviewImage) -or -not (Test-Path -LiteralPath $allSlotReviewResult)) {
    throw "The eight-slot cat review failed (exit code $($allSlotReviewProcess.ExitCode))."
}
$allSlotReview = Get-Content -LiteralPath $allSlotReviewResult -Raw | ConvertFrom-Json
if (-not $allSlotReview.rendered -or $allSlotReview.installed -or $allSlotReview.size -ne '650x600' -or $allSlotReview.actionCount -ne 8) {
    throw "The eight-slot cat review returned an invalid result: $($allSlotReview | ConvertTo-Json -Compress)"
}
$interactionResultPath = Join-Path $outputRoot 'theme-interaction-results.json'
$interactionProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-theme-interaction', $allSlotThemePath, $interactionResultPath) -PassThru -Wait -WindowStyle Hidden
if ($interactionProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $interactionResultPath)) {
    throw "Theme click/drag interaction check failed (exit code $($interactionProcess.ExitCode))."
}
$interactionResult = Get-Content -LiteralPath $interactionResultPath -Raw | ConvertFrom-Json
if (-not $interactionResult.passed -or $interactionResult.slotCount -ne 8 -or -not $interactionResult.clickRecognized -or
    -not $interactionResult.clickProtected -or -not $interactionResult.returnedToIdle -or
    -not $interactionResult.dragRejectedAsClick -or -not $interactionResult.interactionOffBlocksAction -or
    -not $interactionResult.eventSlotsSelectable -or -not $interactionResult.themeExportMenuEnabled) {
    throw "Theme click/drag interaction reported a failed assertion: $($interactionResult | ConvertTo-Json -Compress)"
}
$automaticActionResultPath = Join-Path $outputRoot 'theme-automatic-action-results.json'
$automaticActionProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-auto-action', $allSlotThemePath, $automaticActionResultPath) -PassThru -Wait -WindowStyle Hidden
if ($automaticActionProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $automaticActionResultPath)) {
    throw "The automatic action scheduler check failed (exit code $($automaticActionProcess.ExitCode))."
}
$automaticActionResult = Get-Content -LiteralPath $automaticActionResultPath -Raw | ConvertFrom-Json
if (-not $automaticActionResult.passed -or -not $automaticActionResult.automaticActionsDisabled -or
    -not $automaticActionResult.selectedAmbient -or
    -not $automaticActionResult.quietSuppressed -or -not $automaticActionResult.frequencyRespected -or
    -not $automaticActionResult.interactionProtected -or -not $automaticActionResult.missingSlotSuppressed -or
    -not $automaticActionResult.eventOnlySuppressed -or
    -not $automaticActionResult.hiddenWindowSuppressed -or -not $automaticActionResult.schedulerTimerFired -or
    -not $automaticActionResult.schedulerReturnedToIdle) {
    throw "The automatic action scheduler reported a failed assertion: $($automaticActionResult | ConvertTo-Json -Compress)"
}
$unknownSlotPath = Join-Path $outputRoot 'rejected-theme-confirmed-ended.lotheme'
$unknownManifest = $allSlotManifest | ConvertFrom-Json
$unknownManifest.actions[4].slot = 'confirmed-ended'
$unknownStream = [System.IO.File]::Open($unknownSlotPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
$unknownArchive = New-Object System.IO.Compression.ZipArchive($unknownStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
$unknownEntry = $unknownArchive.CreateEntry('manifest.json')
$unknownWriter = New-Object System.IO.StreamWriter($unknownEntry.Open(), (New-Object System.Text.UTF8Encoding($false)))
$unknownWriter.Write(($unknownManifest | ConvertTo-Json -Depth 6 -Compress))
$unknownWriter.Dispose()
$unknownArchive.Dispose()
$unknownStream.Dispose()
$unknownResultPath = Join-Path $outputRoot 'rejected-theme-confirmed-ended.json'
$unknownPreviewPath = Join-Path $outputRoot 'rejected-theme-confirmed-ended.png'
$unknownProcess = Start-Process -FilePath $exePath -ArgumentList @('--preview-theme', $unknownSlotPath, $unknownPreviewPath, $unknownResultPath) -PassThru -Wait -WindowStyle Hidden
if ($unknownProcess.ExitCode -ne 2 -or (Test-Path -LiteralPath $unknownPreviewPath) -or -not (Test-Path -LiteralPath $unknownResultPath)) {
    throw "The WPF importer did not reject disabled confirmed-ended (exit code $($unknownProcess.ExitCode))."
}
$unknownResult = Get-Content -LiteralPath $unknownResultPath -Raw | ConvertFrom-Json
if ($unknownResult.loaded -or $unknownResult.error -ne 'invalid-theme-package') {
    throw "The WPF importer reported an unexpected disabled-slot result: $($unknownResult | ConvertTo-Json -Compress)"
}
$relayEventDirectory = Join-Path $outputRoot 'codex-relay-smoke'
New-Item -ItemType Directory -Path $relayEventDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $relayEventDirectory -File | Remove-Item -Force
$relaySession = 'cat-hook-qa-session'
$sessionHashBytes = [System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($relaySession))
$relaySessionHash = [System.BitConverter]::ToString($sessionHashBytes).Replace('-', '').ToLowerInvariant()
$relayPayload = @{ session_id = $relaySession; cwd = $workspaceRoot; hook_event_name = 'PermissionRequest'; prompt = 'SENSITIVE_FIXTURE_TOKEN'; tool_input = @{ description = 'SENSITIVE_FIXTURE_TOKEN' } } | ConvertTo-Json -Depth 4 -Compress
$previousEventDir = $env:AI_PET_EVENT_DIR
try {
    $env:AI_PET_EVENT_DIR = $relayEventDirectory
    $relayPayload | & $Python (Join-Path $workspaceRoot 'tools\codex_hook_relay.py')
    if ($LASTEXITCODE -ne 0) { throw "Codex hook relay exited with $LASTEXITCODE." }
} finally { $env:AI_PET_EVENT_DIR = $previousEventDir }
$relayRecordPath = Join-Path $relayEventDirectory ($relaySessionHash + '.json')
if (-not (Test-Path -LiteralPath $relayRecordPath)) { throw 'The Codex relay did not write a sanitized event.' }
$relayRecordText = Get-Content -LiteralPath $relayRecordPath -Raw
$relayRecord = $relayRecordText | ConvertFrom-Json
if ($relayRecordText.Contains('SENSITIVE_FIXTURE_TOKEN') -or $relayRecord.event -ne 'PermissionRequest' -or
    $relayRecord.session -ne $relaySessionHash -or @($relayRecord.PSObject.Properties.Name).Count -ne 5) {
    throw 'The Codex relay persisted more than its allowed evidence fields.'
}
$relayResultPath = Join-Path $outputRoot 'codex-relay-results.json'
$relayProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-codex-relay', $allSlotThemePath, $relayEventDirectory, $relaySessionHash, 'needs-user', $relayResultPath, 'codex-hook', $themeFixturePath) -PassThru -Wait -WindowStyle Hidden
if ($relayProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $relayResultPath)) {
    throw "The WPF Codex relay check failed (exit code $($relayProcess.ExitCode))."
}
$relayResult = Get-Content -LiteralPath $relayResultPath -Raw | ConvertFrom-Json
if (-not $relayResult.passed -or -not $relayResult.eventMapped -or -not $relayResult.duplicateIgnored -or
    -not $relayResult.otherSessionIgnored -or -not $relayResult.telemetryAccurate -or -not $relayResult.noDistinctAccurate) {
    throw "The WPF Codex relay mapped an event incorrectly: $($relayResult | ConvertTo-Json -Compress)"
}
$invalidRelayPayloads = @(
    (@{ session_id = 'unknown-event-session'; cwd = $workspaceRoot; hook_event_name = 'ConfirmedEnded' } | ConvertTo-Json -Compress),
    (@{ session_id = 'foreign-project-session'; cwd = 'C:\Windows'; hook_event_name = 'Stop' } | ConvertTo-Json -Compress)
)
$previousEventDir = $env:AI_PET_EVENT_DIR
try {
    $env:AI_PET_EVENT_DIR = $relayEventDirectory
    foreach ($invalidRelayPayload in $invalidRelayPayloads) {
        $invalidRelayPayload | & $Python (Join-Path $workspaceRoot 'tools\codex_hook_relay.py')
        if ($LASTEXITCODE -ne 0) { throw "Codex hook relay failed closed incorrectly ($LASTEXITCODE)." }
    }
} finally { $env:AI_PET_EVENT_DIR = $previousEventDir }
if (@(Get-ChildItem -LiteralPath $relayEventDirectory -Filter '*.json').Count -ne 2) {
    throw 'An unsupported or foreign-project Codex event was persisted.'
}
$stopRelayPayload = @{ session_id = $relaySession; cwd = $workspaceRoot; hook_event_name = 'Stop'; last_assistant_message = 'SENSITIVE_FIXTURE_TOKEN' } | ConvertTo-Json -Depth 3 -Compress
$previousEventDir = $env:AI_PET_EVENT_DIR
try {
    $env:AI_PET_EVENT_DIR = $relayEventDirectory
    $stopRelayPayload | & $Python (Join-Path $workspaceRoot 'tools\codex_hook_relay.py')
    if ($LASTEXITCODE -ne 0) { throw "Codex Stop relay exited with $LASTEXITCODE." }
} finally { $env:AI_PET_EVENT_DIR = $previousEventDir }
$stopRelayResultPath = Join-Path $outputRoot 'codex-stop-relay-results.json'
$stopRelayProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-codex-relay', $allSlotThemePath, $relayEventDirectory, $relaySessionHash, 'stop-unknown', $stopRelayResultPath) -PassThru -Wait -WindowStyle Hidden
if ($stopRelayProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $stopRelayResultPath)) {
    throw "The WPF Codex Stop relay check failed (exit code $($stopRelayProcess.ExitCode))."
}
$stopRelayResult = Get-Content -LiteralPath $stopRelayResultPath -Raw | ConvertFrom-Json
if (-not $stopRelayResult.passed -or -not $stopRelayResult.eventMapped -or $stopRelayResult.consumedCount -ne 2 -or
    (Get-ChildItem -LiteralPath $relayEventDirectory -File | Get-Content -Raw | Out-String).Contains('SENSITIVE_FIXTURE_TOKEN')) {
    throw 'Codex Stop was not mapped neutrally or leaked assistant content.'
}
$desktopTransitionDir = Join-Path $outputRoot 'codex-desktop-transition-smoke'
New-Item -ItemType Directory -Path $desktopTransitionDir -Force | Out-Null
Get-ChildItem -LiteralPath $desktopTransitionDir -File | Remove-Item -Force
$desktopSessionHash = ('ab' * 32)
$transitionAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
foreach ($transition in @(@{ event='DesktopTurnEndedUnknown'; at=$transitionAt }, @{ event='DesktopTurnStarted'; at=($transitionAt + 8) })) {
    $eventId = [Guid]::NewGuid().ToString('N')
    $payload = [ordered]@{ version=1; eventId=$eventId; observedAtMs=$transition.at; event=$transition.event; session=$desktopSessionHash } | ConvertTo-Json -Compress
    $path = Join-Path $desktopTransitionDir ($desktopSessionHash + '.' + $transition.at + '.' + $eventId + '.event.json')
    [IO.File]::WriteAllText($path, $payload, (New-Object System.Text.UTF8Encoding($false)))
}
$desktopTransitionResultPath = Join-Path $outputRoot 'codex-desktop-transition-results.json'
$desktopTransitionProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-codex-relay', $allSlotThemePath, $desktopTransitionDir, $desktopSessionHash, 'recent-activity', $desktopTransitionResultPath, 'desktop-rollout') -PassThru -Wait -WindowStyle Hidden
if ($desktopTransitionProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $desktopTransitionResultPath)) {
    throw 'The Codex Desktop turn transition check failed.'
}
$desktopTransitionResult = Get-Content -LiteralPath $desktopTransitionResultPath -Raw | ConvertFrom-Json
if (-not $desktopTransitionResult.passed -or $desktopTransitionResult.consumedCount -ne 1) {
    throw 'An immediate Codex Desktop goal turn transition played both stop and start gestures.'
}
$nativeDesktopFixtureRoot = Join-Path $outputRoot 'codex-desktop-native-fixture'
$nativeDesktopSessionsRoot = Join-Path $nativeDesktopFixtureRoot 'sessions'
$nativeDesktopSessionDir = Join-Path $nativeDesktopSessionsRoot '2026\10\02'
$nativeDesktopEvents = Join-Path $nativeDesktopFixtureRoot 'events'
New-Item -ItemType Directory -Path $nativeDesktopSessionDir,$nativeDesktopEvents -Force | Out-Null
Get-ChildItem -LiteralPath $nativeDesktopEvents -File | Remove-Item -Force
$nativeDesktopId = '01a0ee5c-d7e2-7912-b362-d1bb6bf0fba2'
$nativeDesktopTurnId = '01a0fcbf-c692-76e2-a1cc-efe01e2a4ea4'
$nativeDesktopRollout = Join-Path $nativeDesktopSessionDir ("rollout-test-" + $nativeDesktopId + '.jsonl')
$nativeDesktopLines = @(
    (@{ type='session_meta'; payload=@{ id=$nativeDesktopId; originator='Codex Desktop'; cwd=$workspaceRoot } } | ConvertTo-Json -Depth 5 -Compress),
    ([ordered]@{ timestamp='2026-10-02T13:13:41.776Z'; type='event_msg'; payload=[ordered]@{ type='task_complete'; turn_id=$nativeDesktopTurnId; last_agent_message='SENSITIVE_DESKTOP_FIXTURE' }; ordinal=1 } | ConvertTo-Json -Depth 5 -Compress),
    ([ordered]@{ timestamp='2026-10-02T13:13:41.785Z'; type='event_msg'; payload=[ordered]@{ type='task_started'; turn_id=$nativeDesktopTurnId; root_turn_id=$nativeDesktopTurnId }; ordinal=2 } | ConvertTo-Json -Depth 5 -Compress)
)
[IO.File]::WriteAllText($nativeDesktopRollout, ($nativeDesktopLines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
$nativeDesktopResultPath = Join-Path $outputRoot 'codex-desktop-native-results.json'
$nativeDesktopSelection = Join-Path $nativeDesktopFixtureRoot 'codex-desktop-selection.json'
$nativeDesktopProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-desktop-rollout', $nativeDesktopSessionsRoot, $nativeDesktopRollout, $allSlotThemePath, $nativeDesktopEvents, 'recent-activity', $nativeDesktopResultPath, $nativeDesktopSelection) -PassThru -Wait -WindowStyle Hidden
if ($nativeDesktopProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $nativeDesktopResultPath)) {
    throw 'The native Desktop rollout source check failed.'
}
$nativeDesktopResult = Get-Content -LiteralPath $nativeDesktopResultPath -Raw | ConvertFrom-Json
if (-not $nativeDesktopResult.passed -or -not $nativeDesktopResult.metadataOnly -or -not $nativeDesktopResult.reviewSemanticsAccurate -or $nativeDesktopResult.eventFiles -ne 2 -or $nativeDesktopResult.consumedCount -ne 1) {
    throw 'Native Desktop rollout parsing, privacy, or transition mapping failed.'
}
$nativeToolEvents = Join-Path $nativeDesktopFixtureRoot 'tool-events'
New-Item -ItemType Directory -Path $nativeToolEvents -Force | Out-Null
Get-ChildItem -LiteralPath $nativeToolEvents -File | Remove-Item -Force
$nativeToolRollout = Join-Path $nativeDesktopSessionDir ("rollout-test-tool-" + $nativeDesktopId + '.jsonl')
$toolCompletedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$nativeToolLines = @(
    (@{ type='session_meta'; payload=@{ id=$nativeDesktopId; originator='Codex Desktop'; cwd=$workspaceRoot } } | ConvertTo-Json -Depth 5 -Compress),
    ([ordered]@{ timestamp='2026-10-02T13:13:41.776Z'; type='event_msg'; payload=[ordered]@{ type='item_completed'; turn_id=$nativeDesktopTurnId; completed_at_ms=$toolCompletedAt; item=[ordered]@{ type='CommandExecution'; command='SENSITIVE_DESKTOP_FIXTURE'; stdout='SENSITIVE_DESKTOP_FIXTURE' } } } | ConvertTo-Json -Depth 6 -Compress),
    ([ordered]@{ timestamp='2026-10-02T13:13:41.785Z'; type='event_msg'; payload=[ordered]@{ type='item_completed'; turn_id=$nativeDesktopTurnId; completed_at_ms=($toolCompletedAt + 1); item=[ordered]@{ type='FileChange'; changes='SENSITIVE_DESKTOP_FIXTURE' } } } | ConvertTo-Json -Depth 6 -Compress)
)
[IO.File]::WriteAllText($nativeToolRollout, ($nativeToolLines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
$nativeToolResultPath = Join-Path $outputRoot 'codex-desktop-tool-results.json'
$nativeToolProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-desktop-rollout', $nativeDesktopSessionsRoot, $nativeToolRollout, $allSlotThemePath, $nativeToolEvents, 'recent-activity', $nativeToolResultPath) -PassThru -Wait -WindowStyle Hidden
if ($nativeToolProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $nativeToolResultPath)) {
    throw 'The native Desktop tool-activity source check failed.'
}
$nativeToolResult = Get-Content -LiteralPath $nativeToolResultPath -Raw | ConvertFrom-Json
if (-not $nativeToolResult.passed -or -not $nativeToolResult.metadataOnly -or $nativeToolResult.eventFiles -ne 1 -or $nativeToolResult.consumedCount -ne 1) {
    throw 'Desktop tool activity was not throttled, mapped, or sanitized.'
}
$nativeNoActivityEvents = Join-Path $nativeDesktopFixtureRoot 'no-activity-events'
New-Item -ItemType Directory -Path $nativeNoActivityEvents -Force | Out-Null
Get-ChildItem -LiteralPath $nativeNoActivityEvents -File | Remove-Item -Force
$nativeNoActivityRollout = Join-Path $nativeDesktopSessionDir ("rollout-test-no-activity-" + $nativeDesktopId + '.jsonl')
$nativeNoActivityLines = @(
    (@{ type='session_meta'; payload=@{ id=$nativeDesktopId; originator='Codex Desktop'; cwd=$workspaceRoot } } | ConvertTo-Json -Depth 5 -Compress),
    ([ordered]@{ timestamp='2026-10-02T13:13:41.776Z'; type='event_msg'; payload=[ordered]@{ type='item_completed'; turn_id=$nativeDesktopTurnId; completed_at_ms=$toolCompletedAt; item=[ordered]@{ type='Reasoning'; raw_content='SENSITIVE_DESKTOP_FIXTURE' } } } | ConvertTo-Json -Depth 6 -Compress),
    ([ordered]@{ timestamp='2026-10-02T13:13:41.785Z'; type='event_msg'; payload=[ordered]@{ type='item_completed'; turn_id=$nativeDesktopTurnId; completed_at_ms=($toolCompletedAt - 60000); item=[ordered]@{ type='CommandExecution'; command='SENSITIVE_DESKTOP_FIXTURE' } } } | ConvertTo-Json -Depth 6 -Compress)
)
[IO.File]::WriteAllText($nativeNoActivityRollout, ($nativeNoActivityLines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
$nativeNoActivityResultPath = Join-Path $outputRoot 'codex-desktop-no-activity-results.json'
$nativeNoActivityProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-desktop-rollout', $nativeDesktopSessionsRoot, $nativeNoActivityRollout, $allSlotThemePath, $nativeNoActivityEvents, 'none', $nativeNoActivityResultPath) -PassThru -Wait -WindowStyle Hidden
if ($nativeNoActivityProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $nativeNoActivityResultPath)) {
    throw 'The native Desktop unsupported/stale activity check failed.'
}
$nativeNoActivityResult = Get-Content -LiteralPath $nativeNoActivityResultPath -Raw | ConvertFrom-Json
if (-not $nativeNoActivityResult.passed -or $nativeNoActivityResult.eventFiles -ne 0 -or $nativeNoActivityResult.consumedCount -ne 0) {
    throw 'Reasoning or stale tool activity generated a pet action.'
}
# Keep both hook fixtures inside the runtime's ten-second freshness window after the native Desktop check.
Get-ChildItem -LiteralPath $relayEventDirectory -File | Remove-Item -Force
$previousEventDir = $env:AI_PET_EVENT_DIR
try {
    $env:AI_PET_EVENT_DIR = $relayEventDirectory
    foreach ($payload in @($relayPayload, $stopRelayPayload)) {
        $payload | & $Python (Join-Path $workspaceRoot 'tools\codex_hook_relay.py')
        if ($LASTEXITCODE -ne 0) { throw "Fresh Codex hook fixture failed with $LASTEXITCODE." }
    }
} finally { $env:AI_PET_EVENT_DIR = $previousEventDir }
$bundledRelayResultPath = Join-Path $outputRoot 'codex-bundled-gesture-results.json'
$bundledRelayProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-bundled-codex-gestures', $relayEventDirectory, $relaySessionHash, 'stop-unknown', $bundledRelayResultPath) -PassThru -Wait -WindowStyle Hidden
if ($bundledRelayProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $bundledRelayResultPath)) {
    throw "The bundled cat did not respond to Codex events (exit code $($bundledRelayProcess.ExitCode))."
}
$bundledRelayResult = Get-Content -LiteralPath $bundledRelayResultPath -Raw | ConvertFrom-Json
if (-not $bundledRelayResult.mapped -or $bundledRelayResult.consumedCount -ne 2 -or $bundledRelayResult.gesture -ne 'stop-unknown') {
    throw 'The bundled cat preview did not perform the neutral Stop gesture.'
}
$nativeRelayDirectory = Join-Path $outputRoot 'codex-native-relay-smoke'
New-Item -ItemType Directory -Path $nativeRelayDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $nativeRelayDirectory -File | Remove-Item -Force
$nativeInputPath = Join-Path $outputRoot 'codex-native-relay-input.tmp'
try {
    foreach ($payload in @($relayPayload, $stopRelayPayload)) {
        [IO.File]::WriteAllText($nativeInputPath, $payload, (New-Object System.Text.UTF8Encoding($false)))
        $nativeProcess = Start-Process -FilePath $exePath -ArgumentList @('--codex-hook-relay', $workspaceRoot, $nativeRelayDirectory) -RedirectStandardInput $nativeInputPath -PassThru -Wait -WindowStyle Hidden
        if ($nativeProcess.ExitCode -ne 0) { throw "Native Codex Hook relay exited with $($nativeProcess.ExitCode)." }
    }
} finally { if (Test-Path -LiteralPath $nativeInputPath) { Remove-Item -LiteralPath $nativeInputPath -Force } }
$nativeLatestPath = Join-Path $nativeRelayDirectory ($relaySessionHash + '.json')
$nativeFiles = @(Get-ChildItem -LiteralPath $nativeRelayDirectory -Filter '*.json' -File)
if ($nativeFiles.Count -ne 3 -or -not (Test-Path -LiteralPath $nativeLatestPath) -or
    @($nativeFiles | Where-Object { $_.Name -like '*.event.json' }).Count -ne 2 -or
    ($nativeFiles | Get-Content -Raw | Out-String).Contains('SENSITIVE_FIXTURE_TOKEN')) {
    throw 'The packaged native relay lost an event or persisted sensitive input.'
}
$nativeBundledResultPath = Join-Path $outputRoot 'codex-native-bundled-results.json'
$nativeBundledProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-bundled-codex-gestures', $nativeRelayDirectory, $relaySessionHash, 'stop-unknown', $nativeBundledResultPath) -PassThru -Wait -WindowStyle Hidden
$nativeBundledResult = Get-Content -LiteralPath $nativeBundledResultPath -Raw | ConvertFrom-Json
if ($nativeBundledProcess.ExitCode -ne 0 -or -not $nativeBundledResult.mapped -or $nativeBundledResult.consumedCount -ne 2) {
    throw 'The native relay did not drive the bundled desktop gesture.'
}
$loopThemePath = Join-Path $outputRoot 'theme-loop-smoke.lotheme'
$loopZipStream = [System.IO.File]::Open($loopThemePath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
$loopArchive = New-Object System.IO.Compression.ZipArchive($loopZipStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
$loopManifestEntry = $loopArchive.CreateEntry('manifest.json')
$loopManifestWriter = New-Object System.IO.StreamWriter($loopManifestEntry.Open(), (New-Object System.Text.UTF8Encoding($false)))
$loopManifestWriter.Write('{"actions":[{"fps":10,"frames":["frames/idle/0001.png","frames/idle/0002.png"],"loop":true,"slot":"idle"}],"format":"little-orbit.theme","name":"Loop smoke","review":"required","version":1}')
$loopManifestWriter.Dispose()
foreach ($frameNumber in 1..2) {
    $frameEntry = $loopArchive.CreateEntry(('frames/idle/{0:D4}.png' -f $frameNumber))
    $frameOutput = $frameEntry.Open()
    $frameInput = [System.IO.File]::OpenRead($manualMattePath)
    $frameInput.CopyTo($frameOutput)
    $frameInput.Dispose()
    $frameOutput.Dispose()
}
$loopArchive.Dispose()
$loopZipStream.Dispose()
$loopResultPath = Join-Path $outputRoot 'theme-loop-results.json'
$loopProcess = Start-Process -FilePath $exePath -ArgumentList @('--smoke-theme-playback', $loopThemePath, $loopResultPath) -PassThru -Wait -WindowStyle Hidden
if ($loopProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $loopResultPath)) {
    throw "The WPF theme loop smoke check failed (exit code $($loopProcess.ExitCode))."
}
$loopResult = Get-Content -LiteralPath $loopResultPath -Raw | ConvertFrom-Json
if (-not $loopResult.loaded -or $loopResult.frameCount -ne 2 -or $loopResult.timerTicks -lt 3 -or -not $loopResult.loop) {
    throw "The WPF theme timer did not advance the loop fixture: $($loopResult | ConvertTo-Json -Compress)"
}
$themeRollbackRoot = Join-Path $outputRoot 'theme-rollback-smoke'
$clockResultPath = Join-Path $outputRoot 'theme-clock-results.json'
$clockProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-theme-playback-clock', $loopThemePath, $clockResultPath) -PassThru -Wait -WindowStyle Hidden
$clockResult = Get-Content -LiteralPath $clockResultPath -Raw | ConvertFrom-Json
if ($clockProcess.ExitCode -ne 0 -or -not $clockResult.passed -or $clockResult.installed -or $clockResult.stallMs -ne 450) {
    throw "Theme playback did not recover elapsed time after a UI stall: $($clockResult | ConvertTo-Json -Compress)"
}
Write-Output "Theme monotonic playback clock passed: $($clockResult | ConvertTo-Json -Compress)"
New-Item -ItemType Directory -Path $themeRollbackRoot -Force | Out-Null
$rollbackResultPath = Join-Path $outputRoot 'theme-rollback-results.json'
$rollbackProcess = Start-Process -FilePath $exePath -ArgumentList @('--test-theme-rollback', $themeFixturePath, $loopThemePath, $themeRollbackRoot, $rollbackResultPath) -PassThru -Wait -WindowStyle Hidden
if ($rollbackProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $rollbackResultPath)) {
    throw "The WPF theme rollback check failed (exit code $($rollbackProcess.ExitCode))."
}
$rollbackResult = Get-Content -LiteralPath $rollbackResultPath -Raw | ConvertFrom-Json
if (-not $rollbackResult.installed -or -not $rollbackResult.previousPreserved -or -not $rollbackResult.rolledBack -or
    $rollbackResult.active -ne 'Reference Cat Review' -or $rollbackResult.previous -ne 'Loop smoke') {
    throw "Theme rollback reported an invalid result: $($rollbackResult | ConvertTo-Json -Compress)"
}
$maliciousThemePath = Join-Path $outputRoot 'rejected-theme-traversal.lotheme'
$maliciousStream = [System.IO.File]::Open($maliciousThemePath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
$maliciousArchive = New-Object System.IO.Compression.ZipArchive($maliciousStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
$manifestEntry = $maliciousArchive.CreateEntry('manifest.json')
$manifestWriter = New-Object System.IO.StreamWriter($manifestEntry.Open(), (New-Object System.Text.UTF8Encoding($false)))
$manifestWriter.Write('{}')
$manifestWriter.Dispose()
$escapeEntry = $maliciousArchive.CreateEntry('../escape.png')
$escapeStream = $escapeEntry.Open()
$escapeBytes = [System.Text.Encoding]::ASCII.GetBytes('blocked')
$escapeStream.Write($escapeBytes, 0, $escapeBytes.Length)
$escapeStream.Dispose()
$maliciousArchive.Dispose()
$maliciousStream.Dispose()
$maliciousPreviewPath = Join-Path $outputRoot 'rejected-theme-traversal.png'
$maliciousResultPath = Join-Path $outputRoot 'rejected-theme-results.json'
$maliciousProcess = Start-Process -FilePath $exePath -ArgumentList @('--preview-theme', $maliciousThemePath, $maliciousPreviewPath, $maliciousResultPath) -PassThru -Wait -WindowStyle Hidden
if ($maliciousProcess.ExitCode -ne 2 -or (Test-Path -LiteralPath $maliciousPreviewPath) -or -not (Test-Path -LiteralPath $maliciousResultPath)) {
    throw "The WPF importer did not reject a path-traversal theme package (exit code $($maliciousProcess.ExitCode))."
}
$maliciousResult = Get-Content -LiteralPath $maliciousResultPath -Raw | ConvertFrom-Json
if ($maliciousResult.loaded -or $maliciousResult.error -ne 'invalid-theme-package') {
    throw "The WPF theme importer reported an unexpected rejection result: $($maliciousResult | ConvertTo-Json -Compress)"
}
$duplicateThemePath = Join-Path $outputRoot 'rejected-theme-duplicate-key.lotheme'
$duplicateStream = [System.IO.File]::Open($duplicateThemePath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
$duplicateArchive = New-Object System.IO.Compression.ZipArchive($duplicateStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
$duplicateManifestEntry = $duplicateArchive.CreateEntry('manifest.json')
$duplicateWriter = New-Object System.IO.StreamWriter($duplicateManifestEntry.Open(), (New-Object System.Text.UTF8Encoding($false)))
$duplicateWriter.Write('{"format":"little-orbit.theme","format":"little-orbit.theme"}')
$duplicateWriter.Dispose()
$dummyFrameEntry = $duplicateArchive.CreateEntry('frames/idle/0001.png')
$dummyFrameStream = $dummyFrameEntry.Open()
$dummyFrameBytes = [System.Text.Encoding]::ASCII.GetBytes('unused')
$dummyFrameStream.Write($dummyFrameBytes, 0, $dummyFrameBytes.Length)
$dummyFrameStream.Dispose()
$duplicateArchive.Dispose()
$duplicateStream.Dispose()
$duplicateResultPath = Join-Path $outputRoot 'rejected-theme-duplicate-key.json'
$duplicateProcess = Start-Process -FilePath $exePath -ArgumentList @('--preview-theme', $duplicateThemePath, (Join-Path $outputRoot 'rejected-theme-duplicate-key.png'), $duplicateResultPath) -PassThru -Wait -WindowStyle Hidden
if ($duplicateProcess.ExitCode -ne 2 -or -not (Test-Path -LiteralPath $duplicateResultPath)) {
    throw "The WPF importer did not reject duplicate JSON fields (exit code $($duplicateProcess.ExitCode))."
}
$duplicateResult = Get-Content -LiteralPath $duplicateResultPath -Raw | ConvertFrom-Json
if ($duplicateResult.loaded -or $duplicateResult.error -ne 'invalid-theme-package') {
    throw "The WPF importer reported an unexpected duplicate-key result: $($duplicateResult | ConvertTo-Json -Compress)"
}
$floatingPreviewPath = Join-Path $outputRoot 'desktop-floating-preview.png'
$floatingPreviewProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-floating-preview', $floatingPreviewPath) -PassThru -Wait -WindowStyle Hidden
if ($floatingPreviewProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $floatingPreviewPath)) {
    throw "The transparent floating preview did not render (exit code $($floatingPreviewProcess.ExitCode))."
}
foreach ($gesture in @('recent-activity', 'needs-user', 'interrupt')) {
    $gestureImage = Join-Path $outputRoot ("desktop-gesture-$gesture.png")
    $gestureReport = Join-Path $outputRoot ("desktop-gesture-$gesture.json")
    $gestureProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-floating-gesture', $gesture, $gestureImage, $gestureReport) -PassThru -Wait -WindowStyle Hidden
    if ($gestureProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $gestureImage) -or -not (Test-Path -LiteralPath $gestureReport)) {
        throw "The $gesture desktop gesture did not render (exit code $($gestureProcess.ExitCode))."
    }
    $gestureResult = Get-Content -LiteralPath $gestureReport -Raw | ConvertFrom-Json
    if (-not $gestureResult.rendered -or $gestureResult.gesture -ne $gesture) {
        throw "The $gesture desktop gesture reported an unexpected result."
    }
}
$expiredCueImage = Join-Path $outputRoot 'desktop-permission-expired.png'
$expiredCueReport = Join-Path $outputRoot 'desktop-permission-expired.json'
$expiredCueProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-floating-gesture', 'needs-user', $expiredCueImage, $expiredCueReport, '2700') -PassThru -Wait -WindowStyle Hidden
if ($expiredCueProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $expiredCueImage) -or -not (Test-Path -LiteralPath $expiredCueReport)) {
    throw 'The temporary permission cue did not render after its expiry interval.'
}
function Count-TopAlphaPixels([string]$path) {
    $image = New-Object System.Drawing.Bitmap($path)
    try {
        $count = 0
        for ($y = 0; $y -lt [Math]::Min(25, $image.Height); $y++) {
            for ($x = 0; $x -lt $image.Width; $x++) {
                if ($image.GetPixel($x, $y).A -gt 0) { $count++ }
            }
        }
        return $count
    } finally { $image.Dispose() }
}
$visibleCuePixels = Count-TopAlphaPixels (Join-Path $outputRoot 'desktop-gesture-needs-user.png')
$expiredCuePixels = Count-TopAlphaPixels $expiredCueImage
$idleCuePixels = Count-TopAlphaPixels $floatingPreviewPath
if ($visibleCuePixels -lt 100 -or $expiredCuePixels -ne 0 -or $idleCuePixels -ne 0) {
    throw "The permission cue did not stay temporary: visible=$visibleCuePixels expired=$expiredCuePixels idle=$idleCuePixels."
}
$themeCueCounts = @{}
foreach ($case in @(@('idle', 300), @('needs-user', 300), @('needs-user', 2700))) {
    $slot = [string]$case[0]
    $delay = [string]$case[1]
    $key = "$slot-$delay"
    $capturePath = Join-Path $outputRoot "theme-permission-$key.png"
    $reportPath = Join-Path $outputRoot "theme-permission-$key.json"
    $captureProcess = Start-Process -FilePath $exePath -ArgumentList @('--render-theme-floating', $allSlotThemePath, $capturePath, $reportPath, $slot, $delay) -PassThru -Wait -WindowStyle Hidden
    if ($captureProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $capturePath) -or -not (Test-Path -LiteralPath $reportPath)) {
        throw "Transparent theme permission capture failed for $key."
    }
    $captureReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (-not $captureReport.rendered -or $captureReport.installed -or -not $captureReport.transparent -or
        $captureReport.slot -ne $slot -or $captureReport.currentSlot -ne 'idle' -or $captureReport.oneShotActive) {
        throw "Transparent theme permission capture reported an invalid result for $key."
    }
    $themeCueCounts[$key] = Count-TopAlphaPixels $capturePath
}
if ($themeCueCounts['needs-user-300'] -lt $themeCueCounts['idle-300'] + 100 -or
    $themeCueCounts['needs-user-2700'] -ne $themeCueCounts['idle-300']) {
    throw "Theme permission cue did not stay temporary: idle=$($themeCueCounts['idle-300']) visible=$($themeCueCounts['needs-user-300']) expired=$($themeCueCounts['needs-user-2700'])."
}
Write-Output "Built $exePath"
Write-Output "Rendered $floatingPreviewPath"
Write-Output 'Rendered recent-activity, needs-user, and interrupt desktop gestures.'
Write-Output "Temporary permission cue passed: visible=$visibleCuePixels top pixels, expired=$expiredCuePixels, idle=$idleCuePixels."
Write-Output "Transparent theme permission cue passed: idle=$($themeCueCounts['idle-300']) visible=$($themeCueCounts['needs-user-300']) expired=$($themeCueCounts['needs-user-2700'])."
Write-Output "Smoke checks passed: $($smokeResult | ConvertTo-Json -Compress)"
Write-Output "Bundled-photo framing advisory passed; imported role cards remain sample-neutral."
Write-Output "Role-card previous-version rollback passed: $($roleRollbackResult | ConvertTo-Json -Compress)"
Write-Output "Role-card restore review dialog rendered: $($roleReviewResult | ConvertTo-Json -Compress)"
Write-Output "Native preferences persistence and quiet hours passed: $($preferencesResult | ConvertTo-Json -Compress)"
Write-Output "Portable v1 preferences remain supported and default automatic actions off: $($legacyPreferencesResult | ConvertTo-Json -Compress)"
Write-Output "Invalid native preference rejection passed: $($invalidPreferencesResult.error)"
Write-Output "Native preferences dialog render passed: $($preferencesViewResult | ConvertTo-Json -Compress)"
Write-Output "Portable role-card import passed: $($importResult | ConvertTo-Json -Compress)"
Write-Output "External photo rejection passed: $($rejectionResult.error)"
Write-Output "Local role save/reload passed: $($persistenceResult | ConvertTo-Json -Compress)"
Write-Output "WPF theme package preview passed: $($themeResult | ConvertTo-Json -Compress)"
Write-Output "WPF active theme export preserved the reference-cat package bytes: $($themeExportResult | ConvertTo-Json -Compress)"
Write-Output "WPF theme review dialog render passed: $($reviewDialogResult | ConvertTo-Json -Compress)"
Write-Output "WPF three-action theme review dialog render passed: $($multiReviewResult | ConvertTo-Json -Compress)"
Write-Output "WPF eight-slot cat review and interaction checks passed: $($interactionResult | ConvertTo-Json -Compress)"
Write-Output "WPF automatic-action frequency, quiet, visibility, slot and interruption gates passed: $($automaticActionResult | ConvertTo-Json -Compress)"
Write-Output "WPF disabled confirmed-ended slot rejection passed: $($unknownResult.error)"
Write-Output "Sanitized Codex Hook relay and WPF session filtering passed: $($relayResult | ConvertTo-Json -Compress)"
Write-Output "Codex Stop maps only to stop-unknown; unsupported and foreign events were ignored: $($stopRelayResult | ConvertTo-Json -Compress)"
Write-Output "WPF theme frame timer passed: $($loopResult | ConvertTo-Json -Compress)"
Write-Output "Bundled cat responded to queued Codex evidence: $($bundledRelayResult | ConvertTo-Json -Compress)"
Write-Output "Packaged native Hook relay drove the bundled cat: $($nativeBundledResult | ConvertTo-Json -Compress)"
Write-Output "WPF previous theme preservation and rollback passed: $($rollbackResult | ConvertTo-Json -Compress)"
Write-Output "Pending role and theme handoff passed: $($pendingHandoffResult | ConvertTo-Json -Compress)"
Write-Output "Theme review fingerprint gate passed: $($reviewFingerprintResult | ConvertTo-Json -Compress)"
Write-Output "WPF path-traversal rejection passed: $($maliciousResult.error)"
Write-Output "WPF duplicate-manifest-field rejection passed: $($duplicateResult.error)"
Write-Output "Rendered $previewPath"
} finally {
    foreach ($name in $smokeEnvironmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousSmokeEnvironment[$name], 'Process')
    }
}
