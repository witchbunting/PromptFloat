param([switch]$DesktopChecks, [switch]$CompatibilityChecks)
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
$env:MSBUILD_DISABLE_WORKLOAD_RESOLVER='1'
$workspacePath=$PSScriptRoot
[xml]$props=Get-Content -LiteralPath (Join-Path $workspacePath 'Directory.Build.props') -Raw
$releaseVersion=[string]$props.Project.PropertyGroup.Version
$publishDirectory=Join-Path $workspacePath "dist/PromptFloat-$releaseVersion-win-x64"
$localDotnet=Join-Path $workspacePath '.tools/dotnet/dotnet.exe'
$dotnetCommand=if(Test-Path -LiteralPath $localDotnet){$localDotnet}else{(Get-Command dotnet -ErrorAction Stop).Source}
$env:DOTNET_HOST_PATH=$dotnetCommand
$sdkVersion=(Get-Content -LiteralPath (Join-Path $workspacePath 'global.json') -Raw | ConvertFrom-Json).sdk.version
$msBuildEntry=Join-Path (Split-Path $dotnetCommand) "sdk/$sdkVersion/MSBuild.dll"
Push-Location $workspacePath
try {
    if(Test-Path -LiteralPath $msBuildEntry){
        & $dotnetCommand exec $msBuildEntry ./PromptFloat.Tests/PromptFloat.Tests.csproj -t:Build -restore -p:RestoreLockedMode=true -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:Configuration=Release -nodeReuse:false -v:minimal -nologo
        if($LASTEXITCODE -ne 0){throw 'Core test build failed.'}
        & $dotnetCommand ./PromptFloat.Tests/bin/Release/net10.0/win-x64/PromptFloat.Tests.dll
    }else{
        & $dotnetCommand restore ./PromptFloat.sln --locked-mode -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:PublishTrimmed=false
        if($LASTEXITCODE -ne 0){throw 'Locked restore failed.'}
        & $dotnetCommand run --project ./PromptFloat.Tests -c Release --no-restore -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:PublishTrimmed=false
    }
    if($LASTEXITCODE -ne 0){throw 'Core checks failed.'}
    if(Test-Path -LiteralPath $msBuildEntry){
        & $dotnetCommand exec $msBuildEntry ./PromptFloat/PromptFloat.csproj -t:Publish -restore -p:RestoreLockedMode=true -p:Configuration=Release -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false "-p:PublishDir=$publishDirectory/" -nodeReuse:false -v:minimal -nologo
    }else{
        & $dotnetCommand publish ./PromptFloat/PromptFloat.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $publishDirectory --nologo
    }
    if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
    if($DesktopChecks -or $CompatibilityChecks){
        $arguments=if($CompatibilityChecks){'--verify --compat'}else{'--verify'}
        $process=Start-Process -FilePath (Join-Path $publishDirectory 'PromptFloat.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if(-not $process.WaitForExit(180000)){$process.Kill($true);throw 'Isolated desktop checks timed out.'}
        if($process.ExitCode -ne 0){throw 'Desktop checks failed; inspect artifacts/verification-error.txt.'}
    }
    & (Join-Path $workspacePath 'scripts/package-release.ps1') -Version $releaseVersion
}finally{Pop-Location}
