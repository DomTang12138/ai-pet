param(
    [ValidateSet('start','status','connect','disconnect','event','bind','build','requests','result','codex-connect','codex-auto','codex-event','codex-install','codex-remove','codex-status','configure-generation','generate','cancel-generation','generation-status','create','video-create','video-requests','video-result')][string]$Action = 'status',
    [string]$Tool, [string]$Session, [string]$State, [string]$Slot,
    [string]$Executable, [string]$DataDirectory, [string]$RequestId, [string]$Theme, [string]$CodexHome, [string]$GeneratorConfig, [string]$CreationSpec
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($Action -eq 'build') {
    & (Join-Path $projectRoot 'runtime\build.ps1') -Public
    exit $LASTEXITCODE
}
if (-not $Executable) {
    $candidates = @((Join-Path $projectRoot 'AIPetDesktop.exe'), (Join-Path $projectRoot 'runtime\public-publish\AIPetDesktop.exe'), (Join-Path $projectRoot 'runtime\publish\AIPetDesktop.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\AI Pet Studio\AIPetDesktop.exe'))
    $Executable = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $Executable -or -not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw '未找到桌宠程序，请先构建或安装。' }
$Executable = [IO.Path]::GetFullPath($Executable)
if ($Action -in @('codex-install','codex-remove','codex-status')) {
    $setup = @{ Action = $Action.Substring(6); Executable = $Executable }
    if ($DataDirectory) { $setup.DataDirectory = $DataDirectory }
    if ($CodexHome) { $setup.CodexHome = $CodexHome }
    & (Join-Path $PSScriptRoot 'codex-connection.ps1') @setup
    return
}
$previousDataDirectory = $env:AI_PET_DATA_DIR
try {
    if ($DataDirectory) {
        if (-not [IO.Path]::IsPathRooted($DataDirectory)) { throw '数据目录必须是绝对路径。' }
        $env:AI_PET_DATA_DIR = $DataDirectory
    }
    if ($Action -eq 'start') {
        Start-Process -FilePath $Executable -WorkingDirectory (Split-Path $Executable -Parent) -WindowStyle Hidden | Out-Null
        Write-Output '已发送启动请求；用 status 查询能力和连接。'
        exit 0
    }
    $arguments = @()
    if ($Action -eq 'connect') { $arguments = @('--ai-connect', $Tool, $Session) }
    elseif ($Action -eq 'codex-auto') { $arguments = @('--ai-codex-auto') }
    elseif ($Action -eq 'codex-connect') {
        if (-not $Session) { $Session = $env:CODEX_THREAD_ID }
        if (-not $Session) { throw '请提供真实 Codex 会话编号，或从当前 Codex Agent 中执行。' }
        $arguments = @('--ai-codex-connect', $Session)
    }
    elseif ($Action -eq 'disconnect') { $arguments = @('--ai-disconnect') }
    elseif ($Action -eq 'codex-event') {
        if (-not $Session) { $Session = $env:CODEX_THREAD_ID }
        if (-not $Session) { throw '请提供真实 Codex 会话编号，或从当前 Codex Agent 中执行。' }
        $arguments = @('--ai-codex-event', $Session, $State)
    }
    elseif ($Action -eq 'event') { $arguments = @('--ai-event', $Tool, $Session, $State) }
    elseif ($Action -eq 'bind') { $arguments = @('--ai-bind', $State, $Slot) }
    elseif ($Action -eq 'configure-generation') {
        if (-not (Test-Path -LiteralPath $GeneratorConfig -PathType Leaf)) { throw '请提供生成器配置文件。' }
        $arguments = @('--ai-generator-config', ('"' + [IO.Path]::GetFullPath($GeneratorConfig) + '"'))
    }
    elseif ($Action -eq 'generate' -or $Action -eq 'cancel-generation') {
        if ($RequestId -notmatch '\A[a-f0-9]{32}\z') { throw '请提供创作编号。' }
        $command = if ($Action -eq 'generate') { '--ai-start-generation' } else { '--ai-cancel-generation' }
        $arguments = @($command, $RequestId)
    }
    elseif ($Action -in @('result','video-result')) {
        if ($RequestId -notmatch '\A[a-f0-9]{32}\z' -or -not (Test-Path -LiteralPath $Theme -PathType Leaf)) { throw '请提供创作编号和生成的主题包。' }
        $arguments = @(('--ai-' + $Action), $RequestId, ('"' + [IO.Path]::GetFullPath($Theme) + '"'))
    }
    $statusFile = $null
    if ($Action -in @('status','requests','generation-status','create','video-create','video-requests')) {
        $statusFile = Join-Path ([IO.Path]::GetTempPath()) ('ai-pet-status-' + [Guid]::NewGuid().ToString('N') + '.json')
        $command = if ($Action -in @('requests','video-requests')) { '--ai-' + $Action } else { '--ai-status' }
        $arguments = @($command, ('"' + $statusFile + '"'))
        if ($Action -eq 'generation-status') {
            if ($RequestId -notmatch '\A[a-f0-9]{32}\z') { throw '请提供创作编号。' }
            $arguments = @('--ai-generation-status', $RequestId, ('"' + $statusFile + '"'))
        }
        if ($Action -in @('create','video-create')) {
            if (-not (Test-Path -LiteralPath $CreationSpec -PathType Leaf)) { throw '请提供制作请求 JSON 文件。' }
            $command = if ($Action -eq 'video-create') { '--ai-video-create' } else { '--ai-create-request' }
            $arguments = @($command, ('"' + [IO.Path]::GetFullPath($CreationSpec) + '"'), ('"' + $statusFile + '"'))
        }
    } elseif ($Action -notin @('result','video-result','configure-generation')) {
        foreach ($value in $arguments | Select-Object -Skip 1) {
            if ($value -notmatch '\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,95}\z') { throw '请提供合法的工具、会话、状态或动作标识。' }
        }
    }
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -PassThru -WindowStyle Hidden
    # Wait for this command only; Start-Process -Wait also waits for generation descendants.
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "桌宠命令失败，退出码 $($process.ExitCode)。" }
    if ($statusFile) {
        Get-Content -LiteralPath $statusFile -Raw -Encoding UTF8
        Remove-Item -LiteralPath $statusFile
    } elseif ($Action -in @('result','video-result')) { Write-Output '候选已提交，请在桌宠中预览并选择使用。' }
    elseif ($Action -eq 'generate') { Write-Output '已启动生成；用 generation-status 查询进度。' }
    elseif ($Action -eq 'cancel-generation') { Write-Output '已请求取消当前生成。' }
    elseif ($Action -eq 'configure-generation') { Write-Output '生成器已配置。' }
    elseif ($Action -in @('event','codex-event')) { Write-Output 'Agent 状态已发送；仅由匹配工具和会话的运行中桌宠接收。' }
    else { Write-Output '配置已保存。' }
} finally { $env:AI_PET_DATA_DIR = $previousDataDirectory }
