param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [switch]$BuildWindows,
    [switch]$Development
)
$ErrorActionPreference = 'Stop'
# Unity clears Temp when it exits, including any player built there.
$checkRoot = Join-Path ([IO.Path]::GetFullPath($ProjectPath)) 'Builds/ProjectChecks'
New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
$taskArguments = @('-batchmode', '-quit', '-projectPath', ('"' + [IO.Path]::GetFullPath($ProjectPath) + '"'), '-logFile', ('"' + (Join-Path $checkRoot 'checks.log') + '"'))
if ($BuildWindows) {
    $taskArguments += @('-executeMethod', 'SokobanProjectChecks.BuildWindows', '-sokobanBuildPath', ('"' + (Join-Path $checkRoot 'Build/Sokoban.exe') + '"'))
    if ($Development) { $taskArguments += '-sokobanDevelopment' }
} else { $taskArguments += @('-nographics', '-executeMethod', 'SokobanProjectChecks.RunBatch') }
$launchInfo = New-Object System.Diagnostics.ProcessStartInfo
$launchInfo.FileName = $UnityPath
$launchInfo.Arguments = $taskArguments -join ' '
$launchInfo.UseShellExecute = $false
$launchInfo.CreateNoWindow = $true
# Some automation hosts omit this standard Windows variable. UPM uses it to
# locate its global configuration even when no custom configuration exists.
# Repair only the child process environment; preserve any explicit value.
if ([string]::IsNullOrWhiteSpace($launchInfo.EnvironmentVariables['ALLUSERSPROFILE'])) {
    $commonDataDirectory = [Environment]::GetFolderPath('CommonApplicationData')
    if ([string]::IsNullOrWhiteSpace($commonDataDirectory) -or -not (Test-Path -LiteralPath $commonDataDirectory -PathType Container)) {
        throw 'Cannot locate Windows CommonApplicationData for Unity Package Manager.'
    }
    $launchInfo.EnvironmentVariables['ALLUSERSPROFILE'] = $commonDataDirectory
    Write-Output 'Restored missing ALLUSERSPROFILE for the Unity child process.'
}
$unityProcess = [Diagnostics.Process]::Start($launchInfo)
try {
    $unityProcess.WaitForExit()
    if ($unityProcess.ExitCode -ne 0) { throw ('Unity check failed; see ' + (Join-Path $checkRoot 'checks.log')) }
} finally { $unityProcess.Dispose() }
if ($BuildWindows -and -not (Test-Path -LiteralPath (Join-Path $checkRoot 'Build/Sokoban.exe') -PathType Leaf)) {
    throw ('Unity exited without the expected player; see ' + (Join-Path $checkRoot 'checks.log'))
}
Write-Output ('Unity check passed; log: ' + (Join-Path $checkRoot 'checks.log'))
