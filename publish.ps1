# Genera publish\SpotLay.exe: un unico ejecutable que no necesita .NET instalado.
$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
Set-Location $PSScriptRoot
dotnet publish src/SpotLay.App/SpotLay.App.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o publish
