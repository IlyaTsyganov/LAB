param([switch]$Test,[switch]$SkipVulkanProbe)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$workspace=Split-Path -Parent $root
$version='1.0'
$sdk=Join-Path $workspace '.tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $sdk)){$sdk='dotnet'}
$env:DOTNET_CLI_UI_LANGUAGE='en'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$compiler=Join-Path $workspace '.tools/llvm-mingw-20261020-ucrt-x86_64/bin/clang++.exe'
$msvc=$false
if(!(Test-Path -LiteralPath $compiler)){
    $clang=Get-Command clang++ -ErrorAction SilentlyContinue
    if($clang){$compiler=$clang.Source}else{
        $vswhere=Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
        if(!(Test-Path -LiteralPath $vswhere)){throw 'Install LLVM-MinGW or Visual Studio C++ build tools.'}
        $installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if(!$installation){throw 'Visual Studio C++ build tools were not found.'}
        $vcvars=Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
        $environmentLines=& $env:ComSpec /d /s /c ('"'+$vcvars+'" >nul && set')
        if($LASTEXITCODE){throw 'Could not initialize the C++ build environment.'}
        foreach($line in $environmentLines){if($line -match '^(INCLUDE|LIB|LIBPATH|PATH)=(.*)$'){[Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process')}}
        $compiler=(Get-Command cl.exe -ErrorAction Stop).Source
        $msvc=$true
    }
}
function Reset-BuildDirectory([string]$directory){
    $absolute=[IO.Path]::GetFullPath($directory)
    if(!$absolute.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Build output is outside the project.'}
    if(Test-Path -LiteralPath $absolute){Remove-Item -LiteralPath $absolute -Recurse -Force}
    New-Item -ItemType Directory -Path $absolute -Force | Out-Null
}
function Build-Native([string]$source,[string]$output,[switch]$Shared){
    if($msvc){
        $object=Join-Path $root ('.tools/native-build/'+[IO.Path]::GetFileNameWithoutExtension($output)+'.obj')
        $nativeArgs=@('/nologo','/std:c++17','/O2','/EHsc','/MT','/Ithird_party/Vulkan-Headers/include',$source,('/Fo'+$object),('/Fe'+$output))
        if($Shared){$nativeArgs+='/LD'}
        $nativeArgs+='/link';$nativeArgs+='kernel32.lib'
        if($Shared){$nativeArgs+=('/IMPLIB:'+(Join-Path $root '.tools/native-build/lab-lighting.lib'))}
        & $compiler @nativeArgs
    }else{
        $nativeArgs=@('-std=c++17','-O2','-static','-Ithird_party/Vulkan-Headers/include',$source,'-o',$output)
        if($Shared){$nativeArgs+='-shared'}
        & $compiler @nativeArgs
    }
    if($LASTEXITCODE){throw ('Native build failed: '+$source)}
}
Push-Location $root
try{
    New-Item -ItemType Directory -Path '.tools/native-build','Tests/output','artifacts' -Force | Out-Null
    Build-Native 'Native/Lighting/lab_lighting.cpp' 'Native/Lighting/lab-lighting.dll' -Shared
    $publish=Join-Path $root ('artifacts/publish-'+$version)
    $package=Join-Path $root ('artifacts/package-'+$version)
    Reset-BuildDirectory $publish
    Reset-BuildDirectory $package
    & $sdk publish lab.csproj -c Release -o $publish --nologo -p:NuGetAudit=false -p:DebugType=None -p:DebugSymbols=false -v:q
    if($LASTEXITCODE){throw 'LAB publish failed'}
    if($Test){
        & $sdk run --project Tests/Lab.Tests.csproj -c Release -p:NuGetAudit=false --no-launch-profile
        if($LASTEXITCODE){throw 'Native fixture checks failed'}
        Build-Native 'Tests/lighting_lifetime.cpp' 'Tests/output/lighting-lifetime.exe'
        & ./Tests/output/lighting-lifetime.exe
        if($LASTEXITCODE){throw 'Lighting lifetime checks failed'}
        Build-Native 'Tests/lighting_probe.cpp' 'Tests/output/lighting-probe.exe'
        $previousPath=$env:VK_LAYER_PATH;$previousFix=$env:LAB_LIGHTING_FIX
        try{$env:VK_LAYER_PATH=(Get-Content -LiteralPath Tests/output/lighting-path.txt -Raw).Trim();$env:LAB_LIGHTING_FIX='1';if($SkipVulkanProbe){& ./Tests/output/lighting-probe.exe --math-only}else{& ./Tests/output/lighting-probe.exe};if($LASTEXITCODE){throw 'Lighting loader checks failed'}}finally{$env:VK_LAYER_PATH=$previousPath;$env:LAB_LIGHTING_FIX=$previousFix}
    }
    foreach($name in @('lab.exe','lab.dll','lab.deps.json','lab.runtimeconfig.json','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','LICENSE')){
        Copy-Item -LiteralPath (Join-Path $publish $name) -Destination (Join-Path $package $name)
    }
    Copy-Item -LiteralPath (Join-Path $publish 'Assets') -Destination $package -Recurse
    New-Item -ItemType Directory -Path (Join-Path $package 'Native/Lighting'),(Join-Path $package 'runtimes/win-x64/native'),(Join-Path $package 'logs') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $publish 'Native/Lighting/lab-lighting.dll') -Destination (Join-Path $package 'Native/Lighting/lab-lighting.dll')
    Copy-Item -LiteralPath (Join-Path $publish 'runtimes/win-x64/native/WebView2Loader.dll') -Destination (Join-Path $package 'runtimes/win-x64/native/WebView2Loader.dll')
    $archive=Join-Path $root ('artifacts/lab-'+$version+'-win-x64.zip')
    if(Test-Path -LiteralPath $archive){Remove-Item -LiteralPath $archive -Force}
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($package,$archive)
    $zip=[IO.Compression.ZipFile]::OpenRead($archive)
    try{
        $names=@($zip.Entries | ForEach-Object {$_.FullName.Replace('\','/')})
        $files=@($names | Where-Object {!$_.EndsWith('/')})
        $expected=@('lab.exe','lab.dll','lab.deps.json','lab.runtimeconfig.json','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','LICENSE','Native/Lighting/lab-lighting.dll','runtimes/win-x64/native/WebView2Loader.dll')
        $expected+=@(Get-ChildItem -LiteralPath Assets -Recurse -File | ForEach-Object {[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')})
        if(@(Compare-Object ($expected | Sort-Object) ($files | Sort-Object)).Count){throw 'Release archive contains missing or unexpected files.'}
        if($names -match '(^|/)(README\.md|CREDITS\.md|settings\.json|[^/]+\.pdb|[^/]+\.log)$'){throw 'Release contains documentation, user data or debug output.'}
        $names | Set-Content -LiteralPath ($archive+'.contents.txt') -Encoding utf8
    }finally{$zip.Dispose()}
    $digest=Get-FileHash -LiteralPath $archive -Algorithm SHA256
    [IO.File]::WriteAllText($archive+'.sha256',$digest.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($archive)+[Environment]::NewLine)
    Write-Output ('LAB is ready: '+(Join-Path $package 'lab.exe'))
    Write-Output ('Verified release archive: '+$archive)
}finally{Pop-Location}
