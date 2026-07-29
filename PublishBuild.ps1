param(
    [string] $Architecture = "x64",
    [string] $MainVer = "1.0.0",
    [string] $WorkerVer = "1.0.0",
    [string] $Output = "build/WTGWizard"
)

$ErrorActionPreference = "Stop";

dotnet publish src/WTGWizard.Worker -c Release -r "win-$Architecture" -o "$Output" -p:Platform=$Architecture -p:Version=$WorkerVer;
dotnet publish src/WTGWizard.Main -c Release -r "win-$Architecture" -o "$Output" -p:Platform=$Architecture -p:Version=$MainVer;

Remove-Item "$Output/WTGWizard.*.pdb" -Force;