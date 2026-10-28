# Publish-FinTSExplorer.ps1
#
# Veröffentlicht Dienst und Notifier in einem Rutsch.
# - FinTSExplorerService ist ein echter Windows-Dienst -> Stop-Service/Start-Service.
# - FinTSExplorer.Notifier läuft in der Desktop-Sitzung (Autostart per Aufgabenplanung,
#   Trigger "bei Anmeldung") -> Prozess beenden/neu starten statt Stop-Service/Start-Service.

# Self-elevate: falls nicht als Administrator gestartet, Skript erhöht neu starten und beenden
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$serviceName = "FinTSExplorerService"
$serviceProjectPath = "C:\Projects\VS2026\FinTSExplorer\FinTSExplorer.Service\FinTSExplorer.Service.csproj"
$serviceOutputPath = "C:\Projects\VS2026\FinTSExplorer\Deploy\Service"

$notifierProcessName = "FinTSExplorer.Notifier"
$notifierTaskName = "FinTSExplorer Notifier"
$notifierProjectPath = "C:\Projects\VS2026\FinTSExplorer\FinTSExplorer.Notifier\FinTSExplorer.Notifier.csproj"
$notifierOutputPath = "C:\Projects\VS2026\FinTSExplorer\Deploy\Notifier"

Write-Host "Stopping $serviceName..."
Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue

Write-Host "Stopping $notifierProcessName..."
Stop-Process -Name $notifierProcessName -Force -ErrorAction SilentlyContinue

# Dienst self-contained: die .NET-Laufzeit liegt im Deploy-Ordner. Ein .NET-Update von Windows kann dem laufenden Dienst
# so nicht mehr die Dateien wegnehmen (sonst FileNotFoundException, z. B. System.Net.NetworkInformation). Laufzeit-Updates
# kommen dafür erst mit dem nächsten Publish. Der Notifier bleibt vorerst wie er ist.
dotnet publish $serviceProjectPath -c Release -r win-x64 --self-contained true -o $serviceOutputPath
dotnet publish $notifierProjectPath -c Release -o $notifierOutputPath

Write-Host "Starting $serviceName..."
Start-Service -Name $serviceName

Write-Host "Starting $notifierProcessName..."
$task = Get-ScheduledTask -TaskName $notifierTaskName -ErrorAction SilentlyContinue
if ($task) {
    Start-ScheduledTask -TaskName $notifierTaskName
} else {
    Start-Process "$notifierOutputPath\FinTSExplorer.Notifier.exe"
}

Write-Host "Done."
