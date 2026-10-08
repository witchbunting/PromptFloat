param([Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference='Stop'
$workspacePath=Split-Path $PSScriptRoot
$releaseDirectory=Join-Path $workspacePath "dist/PromptFloat-$Version-win-x64"
if(-not (Test-Path -LiteralPath (Join-Path $releaseDirectory 'PromptFloat.exe'))){throw 'Build the executable before packaging.'}
foreach($document in @('README.md','使用说明.md','验证报告.md','LICENSE','LICENSE-THIRD-PARTY.md','CHANGELOG.md')){
    Copy-Item -LiteralPath (Join-Path $workspacePath $document) -Destination (Join-Path $releaseDirectory $document) -Force
}
foreach($folder in @('licenses','docs')){
    $source=Join-Path $workspacePath $folder
    if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $releaseDirectory -Recurse -Force}
}
$evidenceNames=@('core-tests.json','artifact-versions.json','responsiveness.json','polish-interaction.json','agent-style-save.json','tutorial-tests.json','desktop-tests.json','desktop-metrics.json','compatibility.json')
$evidenceDirectory=Join-Path $releaseDirectory 'artifacts'
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
foreach($name in $evidenceNames){
    $source=Join-Path $workspacePath "artifacts/$name"
    if(Test-Path -LiteralPath $source){
        if($name -eq 'desktop-metrics.json'){
            $metrics=Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
            $metrics.PSObject.Properties.Remove('dataDirectory')
            $metrics | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $evidenceDirectory $name) -Encoding utf8
        }else{Copy-Item -LiteralPath $source -Destination (Join-Path $evidenceDirectory $name) -Force}
    }
}
$unexpected=Get-ChildItem -LiteralPath $releaseDirectory -File -Recurse | Where-Object {$_.Name -in @('settings.json','credentials.json') -or $_.Extension -in @('.db','.sqlite','.log','.pdb')}
if($unexpected){throw 'Release directory contains data, keys, logs, or debug files.'}
$zipPath=Join-Path $workspacePath "dist/PromptFloat-$Version-win-x64.zip"
Compress-Archive -LiteralPath $releaseDirectory -DestinationPath $zipPath -Force
$hash=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
"$hash  $(Split-Path $zipPath -Leaf)" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ascii
Write-Output "Release archive: $zipPath"
Write-Output "SHA256: $hash"
