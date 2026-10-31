param([switch]$Test)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$workspace=Split-Path -Parent $root
$sdk=Join-Path $workspace '.tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $sdk)){$sdk='dotnet'}
$compiler=Join-Path $workspace '.tools/llvm-mingw-20261020-ucrt-x86_64/bin/clang++.exe'
if(!(Test-Path -LiteralPath $compiler)){$compiler='clang++'}
$env:DOTNET_CLI_UI_LANGUAGE='en'
Push-Location $root
try{
    & $compiler -std=c++17 -O2 -static -shared -Ithird_party/Vulkan-Headers/include Native/Lighting/lab_lighting.cpp -o Native/Lighting/lab-lighting.dll
    if($LASTEXITCODE){throw 'Lighting build failed'}
    & $sdk publish lab.csproj -c Release -o publish --nologo -p:NuGetAudit=false -v:q
    if($LASTEXITCODE){throw 'LAB publish failed'}
    Copy-Item -LiteralPath README.md -Destination publish/README.md -Force
    New-Item -ItemType Directory -Path publish/docs -Force | Out-Null
    Copy-Item -LiteralPath docs/interface.png -Destination publish/docs/interface.png -Force
    if($Test){
        & $sdk run --project Tests/Lab.Tests.csproj -c Release -p:NuGetAudit=false --no-launch-profile
        if($LASTEXITCODE){throw 'Native fixture checks failed'}
        & $compiler -std=c++17 -O2 -static -Ithird_party/Vulkan-Headers/include Tests/lighting_probe.cpp -o Tests/output/lighting-probe.exe
        if($LASTEXITCODE){throw 'Lighting probe build failed'}
        $previousPath=$env:VK_LAYER_PATH;$previousFix=$env:LAB_LIGHTING_FIX
        try{$env:VK_LAYER_PATH=Get-Content -LiteralPath Tests/output/lighting-path.txt -Raw;$env:LAB_LIGHTING_FIX='1';& ./Tests/output/lighting-probe.exe;if($LASTEXITCODE){throw 'Lighting loader checks failed'}}finally{$env:VK_LAYER_PATH=$previousPath;$env:LAB_LIGHTING_FIX=$previousFix}
    }
    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    Compress-Archive -Path publish/* -DestinationPath artifacts/lab-0.1.0-win-x64.zip -Force
    $digest=Get-FileHash -LiteralPath artifacts/lab-0.1.0-win-x64.zip -Algorithm SHA256
    [IO.File]::WriteAllText((Join-Path $root 'artifacts/lab-0.1.0-win-x64.zip.sha256'),$digest.Hash.ToLowerInvariant()+"  lab-0.1.0-win-x64.zip`n")
    Write-Output 'LAB is ready: publish/lab.exe'
}finally{Pop-Location}
