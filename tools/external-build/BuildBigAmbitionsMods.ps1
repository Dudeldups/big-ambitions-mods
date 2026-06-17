[CmdletBinding(DefaultParameterSetName = "Single")]
param(
    [Parameter(ParameterSetName = "Single", Mandatory = $true)]
    [string] $ModName,

    [Parameter(ParameterSetName = "All", Mandatory = $true)]
    [switch] $All,

    [Parameter(ParameterSetName = "List", Mandatory = $true)]
    [switch] $List,

    [switch] $Install,

    [string] $Configuration = "Release",

    [string] $UnityEditorPath = "C:\Program Files\Unity\Hub\Editor\2022.3.62f2",

    [string] $ModsLocalRoot,

    [string] $ConfigPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string] $Message)
    Write-Host "[external-build] $Message"
}

function Write-BuildWarning {
    param([string] $Message)
    Write-Warning "[external-build] $Message"
}

function Get-RepositoryRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..\..")).Path
}

function ConvertTo-RepoRelativePath {
    param(
        [string] $RepoRoot,
        [string] $Path
    )

    $full = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\') + '\'
    if ($full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($root.Length)
    }
    return $full
}

function Get-RelativePathCompat {
    param(
        [string] $BasePath,
        [string] $Path
    )

    $baseFull = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    $baseUri = [System.Uri]::new($baseFull)
    $pathUri = [System.Uri]::new($pathFull)
    return [System.Uri]::UnescapeDataString(
        $baseUri.MakeRelativeUri($pathUri).ToString().Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    )
}

function Get-DefaultModsLocalRoot {
    $local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $localLow = $local -replace "\\Local$", "\LocalLow"
    return Join-Path $localLow "Hovgaard Games\Big Ambitions\ModsLocal"
}

function Get-JsonString {
    param(
        [object] $Object,
        [string] $PropertyName,
        [string] $DefaultValue
    )

    if ($null -eq $Object) {
        return $DefaultValue
    }

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $DefaultValue
    }

    $value = [string] $property.Value
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value
}

function Get-JsonBool {
    param(
        [object] $Object,
        [string] $PropertyName,
        [bool] $DefaultValue
    )

    if ($null -eq $Object) {
        return $DefaultValue
    }

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $DefaultValue
    }

    return [bool] $property.Value
}

function Test-IsExcludedSource {
    param([System.IO.FileInfo] $File)

    if ($File.Name -ieq "UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs") {
        return $true
    }

    $segments = $File.FullName -split "[\\/]+"
    foreach ($segment in $segments) {
        if ($segment -in @("Editor", "bin", "obj", "Library", "Temp")) {
            return $true
        }
    }

    return $false
}

function Get-ModSourceFiles {
    param([string] $SourceDir)

    $scriptsDir = Join-Path $SourceDir "Scripts"
    if (-not (Test-Path -LiteralPath $scriptsDir -PathType Container)) {
        return @()
    }

    return @(
        Get-ChildItem -LiteralPath $scriptsDir -Recurse -File -Filter "*.cs" |
            Where-Object { -not (Test-IsExcludedSource $_) } |
            Sort-Object FullName
    )
}

function New-ModInfo {
    param(
        [string] $RepoRoot,
        [System.IO.DirectoryInfo] $Directory,
        [object] $Override
    )

    $folderName = $Directory.Name
    $sourceDirValue = Get-JsonString $Override "sourceDir" (ConvertTo-RepoRelativePath $RepoRoot $Directory.FullName)
    $sourceDir = if ([System.IO.Path]::IsPathRooted($sourceDirValue)) {
        [System.IO.Path]::GetFullPath($sourceDirValue)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $sourceDirValue))
    }

    $modName = Get-JsonString $Override "modName" $folderName
    $assemblyName = Get-JsonString $Override "assemblyName" $folderName
    $modsLocalFolder = Get-JsonString $Override "modsLocalFolder" $modName
    $dllTargetSubfolder = Get-JsonString $Override "dllTargetSubfolder" ""
    $enabled = Get-JsonBool $Override "enabled" $true

    $sources = if (Test-Path -LiteralPath $sourceDir -PathType Container) {
        Get-ModSourceFiles $sourceDir
    } else {
        @()
    }

    return [pscustomobject]@{
        ModName = $modName
        SourceDir = $sourceDir
        SourceDirRelative = ConvertTo-RepoRelativePath $RepoRoot $sourceDir
        AssemblyName = $assemblyName
        ModsLocalFolder = $modsLocalFolder
        DllTargetSubfolder = $dllTargetSubfolder
        Enabled = $enabled
        HasThumbnail = (Test-Path -LiteralPath (Join-Path $sourceDir "thumbnail.png") -PathType Leaf)
        HasLocales = (Test-Path -LiteralPath (Join-Path $sourceDir "Locales") -PathType Container)
        Sources = @($sources)
    }
}

function Get-ConfigOverrides {
    param([string] $Path)

    $overrides = @{}
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $overrides
    }

    $config = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($null -eq $config.mods) {
        return $overrides
    }

    foreach ($entry in @($config.mods)) {
        $name = Get-JsonString $entry "modName" ""
        if ([string]::IsNullOrWhiteSpace($name)) {
            Write-Step "Skipped config entry without modName."
            continue
        }

        $overrides[$name] = $entry
    }

    return $overrides
}

function Get-DetectedMods {
    param(
        [string] $RepoRoot,
        [hashtable] $Overrides
    )

    $modsRoot = Join-Path $RepoRoot "Assets\Mods"
    if (-not (Test-Path -LiteralPath $modsRoot -PathType Container)) {
        throw "Mods folder not found: $modsRoot"
    }

    $modsByName = @{}
    foreach ($dir in Get-ChildItem -LiteralPath $modsRoot -Directory | Sort-Object Name) {
        $override = $Overrides[$dir.Name]
        $info = New-ModInfo -RepoRoot $RepoRoot -Directory $dir -Override $override
        $modsByName[$info.ModName] = $info
    }

    foreach ($entry in $Overrides.GetEnumerator()) {
        $modNameFromConfig = [string] $entry.Key
        if ($modsByName.ContainsKey($modNameFromConfig)) {
            continue
        }

        $sourceDirValue = Get-JsonString $entry.Value "sourceDir" ""
        if ([string]::IsNullOrWhiteSpace($sourceDirValue)) {
            continue
        }

        $sourceDir = if ([System.IO.Path]::IsPathRooted($sourceDirValue)) {
            [System.IO.Path]::GetFullPath($sourceDirValue)
        } else {
            [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $sourceDirValue))
        }

        $fakeDir = [System.IO.DirectoryInfo]::new($sourceDir)
        $info = New-ModInfo -RepoRoot $RepoRoot -Directory $fakeDir -Override $entry.Value
        $modsByName[$info.ModName] = $info
    }

    return @($modsByName.Values | Sort-Object ModName)
}

function Assert-DependencyPaths {
    param(
        [string] $RepoRoot,
        [string] $UnityEditorPath
    )

    $gameDlls = Join-Path $RepoRoot "Assets\_BaDependencies\GameDlls"
    if (-not (Test-Path -LiteralPath $gameDlls -PathType Container)) {
        throw "Game DLL folder not found: $gameDlls"
    }

    $modApi = Join-Path $gameDlls "BigAmbitions.ModAPI.dll"
    if (-not (Test-Path -LiteralPath $modApi -PathType Leaf)) {
        throw "Required dependency not found: $modApi"
    }

    $unityEngine = Join-Path $UnityEditorPath "Editor\Data\Managed\UnityEngine"
    if (-not (Test-Path -LiteralPath $unityEngine -PathType Container)) {
        throw "UnityEngine managed DLL folder not found: $unityEngine"
    }

    $netStandardShims = Join-Path $UnityEditorPath "Editor\Data\NetStandard\compat\2.1.0\shims"
    if (-not (Test-Path -LiteralPath $netStandardShims -PathType Container)) {
        throw "Unity netstandard shim folder not found: $netStandardShims"
    }
}

function Get-ReferenceFiles {
    param(
        [string] $RepoRoot,
        [string] $UnityEditorPath
    )

    $paths = [System.Collections.Generic.List[string]]::new()

    $gameDlls = Join-Path $RepoRoot "Assets\_BaDependencies\GameDlls"
    Get-ChildItem -LiteralPath $gameDlls -File -Filter "*.dll" |
        Sort-Object Name |
        ForEach-Object { $paths.Add($_.FullName) }

    $unityEngine = Join-Path $UnityEditorPath "Editor\Data\Managed\UnityEngine"
    Get-ChildItem -LiteralPath $unityEngine -File -Filter "UnityEngine*.dll" |
        Sort-Object Name |
        ForEach-Object { $paths.Add($_.FullName) }

    $netStandardShims = Join-Path $UnityEditorPath "Editor\Data\NetStandard\compat\2.1.0\shims"
    Get-ChildItem -LiteralPath $netStandardShims -Recurse -File -Filter "*.dll" |
        Sort-Object FullName |
        ForEach-Object { $paths.Add($_.FullName) }

    $scriptAssemblies = Join-Path $RepoRoot "Library\ScriptAssemblies"
    if (Test-Path -LiteralPath $scriptAssemblies -PathType Container) {
        Get-ChildItem -LiteralPath $scriptAssemblies -File -Filter "*.dll" |
            Where-Object {
                $_.Name -match "^(Unity|UnityEngine|Cinemachine|glTFast|System|Microsoft)\." -and
                $_.Name -notmatch "\.Editor(\.|$)"
            } |
            Sort-Object Name |
            ForEach-Object { $paths.Add($_.FullName) }
    }

    return @($paths | Select-Object -Unique)
}

function Add-ReferenceElement {
    param(
        [System.Xml.XmlElement] $ItemGroup,
        [System.Xml.XmlDocument] $Document,
        [string] $Path
    )

    $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    $reference = $Document.CreateElement("Reference")
    $null = $reference.SetAttribute("Include", $name)

    $hint = $Document.CreateElement("HintPath")
    $hint.InnerText = $Path
    $null = $reference.AppendChild($hint)

    $private = $Document.CreateElement("Private")
    $private.InnerText = "false"
    $null = $reference.AppendChild($private)

    $null = $ItemGroup.AppendChild($reference)
}

function Add-CompileElement {
    param(
        [System.Xml.XmlElement] $ItemGroup,
        [System.Xml.XmlDocument] $Document,
        [string] $Path
    )

    $compile = $Document.CreateElement("Compile")
    $null = $compile.SetAttribute("Include", $Path)
    $null = $ItemGroup.AppendChild($compile)
}

function New-GeneratedProject {
    param(
        [string] $RepoRoot,
        [object] $Mod,
        [string[]] $References,
        [string] $Configuration
    )

    $projectRoot = Join-Path $RepoRoot ("obj\ExternalModBuild\" + $Mod.ModName)
    New-Item -ItemType Directory -Path $projectRoot -Force | Out-Null

    $projectPath = Join-Path $projectRoot ($Mod.AssemblyName + ".csproj")
    $runId = [System.Guid]::NewGuid().ToString("N")
    $runRoot = Join-Path $projectRoot ("build\" + $runId)
    $runOutputRoot = Join-Path $runRoot "bin"
    $runIntermediateRoot = Join-Path $runRoot "obj"
    $runOutputDir = Join-Path $runOutputRoot $Configuration
    $stableOutputDir = Join-Path $projectRoot ("bin\" + $Configuration)

    $document = [System.Xml.XmlDocument]::new()
    $project = $document.CreateElement("Project")
    $null = $project.SetAttribute("Sdk", "Microsoft.NET.Sdk")
    $null = $document.AppendChild($project)

    $propertyGroup = $document.CreateElement("PropertyGroup")
    $null = $project.AppendChild($propertyGroup)

    $properties = [ordered]@{
        TargetFramework = "netstandard2.1"
        AssemblyName = $Mod.AssemblyName
        RootNamespace = ($Mod.AssemblyName -replace "[^A-Za-z0-9_.]", "_")
        LangVersion = "latest"
        Nullable = "disable"
        ImplicitUsings = "disable"
        GenerateAssemblyInfo = "false"
        CopyLocalLockFileAssemblies = "false"
        AppendTargetFrameworkToOutputPath = "false"
        EnableDefaultCompileItems = "false"
        EnableDefaultItems = "false"
        NoWarn = "0169;USG0001;CS1701;CS1702"
        DefineConstants = "BA_GAME_DLLS_IMPORTED;UNITY_2022_3;UNITY_2022_3_OR_NEWER;UNITY_2022;UNITY_STANDALONE;UNITY_STANDALONE_WIN;UNITY_64;NET_STANDARD;NET_STANDARD_2_1;NETSTANDARD;NETSTANDARD2_1"
        OutputPath = (Join-Path $runOutputRoot '$(Configuration)\')
        BaseIntermediateOutputPath = ($runIntermediateRoot.TrimEnd('\') + '\')
        IntermediateOutputPath = (Join-Path $runIntermediateRoot '$(Configuration)\')
    }

    foreach ($entry in $properties.GetEnumerator()) {
        $element = $document.CreateElement($entry.Key)
        $element.InnerText = [string] $entry.Value
        $null = $propertyGroup.AppendChild($element)
    }

    $compileGroup = $document.CreateElement("ItemGroup")
    $null = $project.AppendChild($compileGroup)
    foreach ($source in $Mod.Sources) {
        Add-CompileElement -ItemGroup $compileGroup -Document $document -Path $source.FullName
    }

    $referenceGroup = $document.CreateElement("ItemGroup")
    $null = $project.AppendChild($referenceGroup)
    foreach ($reference in $References) {
        Add-ReferenceElement -ItemGroup $referenceGroup -Document $document -Path $reference
    }

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $writer = [System.Xml.XmlWriter]::Create($projectPath, $settings)
    try {
        $document.Save($writer)
    } finally {
        $writer.Dispose()
    }

    return [pscustomobject]@{
        ProjectPath = $projectPath
        ProjectRoot = $projectRoot
        RunOutputDir = $runOutputDir
        RunIntermediateDir = $runIntermediateRoot
        RunOutputDll = Join-Path $runOutputDir ($Mod.AssemblyName + ".dll")
        StableOutputDll = Join-Path $stableOutputDir ($Mod.AssemblyName + ".dll")
    }
}

function Invoke-ModBuild {
    param(
        [string] $ProjectPath,
        [string] $Configuration,
        [string] $OutputDir,
        [string] $IntermediateDir
    )

    $outputDirValue = ($OutputDir.TrimEnd('\') -replace '\\', '/') + '/'
    $intermediateDirValue = ($IntermediateDir.TrimEnd('\') -replace '\\', '/') + '/'
    $configurationIntermediateDirValue = ((Join-Path $IntermediateDir $Configuration).TrimEnd('\') -replace '\\', '/') + '/'

    & dotnet build $ProjectPath `
        --configuration $Configuration `
        --nologo `
        --verbosity minimal `
        "/p:OutputPath=$outputDirValue" `
        "/p:BaseIntermediateOutputPath=$intermediateDirValue" `
        "/p:MSBuildProjectExtensionsPath=$intermediateDirValue" `
        "/p:IntermediateOutputPath=$configurationIntermediateDirValue"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $ProjectPath"
    }
}

function Copy-StableOutputDll {
    param(
        [string] $RunOutputDll,
        [string] $StableOutputDll
    )

    try {
        $stableOutputDir = Split-Path -Parent $StableOutputDll
        New-Item -ItemType Directory -Path $stableOutputDir -Force | Out-Null
        Copy-Item -LiteralPath $RunOutputDll -Destination $StableOutputDll -Force
        Write-Step ("Output DLL path: " + $StableOutputDll)
        return $StableOutputDll
    } catch {
        Write-BuildWarning ("Could not refresh documented output path '" + $StableOutputDll + "': " + $_.Exception.Message)
        Write-Step ("Output DLL path: " + $RunOutputDll)
        return $RunOutputDll
    }
}

function Copy-ItemWithRetry {
    param(
        [string] $Source,
        [string] $Destination,
        [int] $MaxAttempts = 8,
        [int] $DelayMilliseconds = 250
    )

    $attempt = 0
    while ($true) {
        try {
            Copy-Item -LiteralPath $Source -Destination $Destination -Force
            return
        } catch {
            $attempt++
            if ($attempt -ge $MaxAttempts) {
                throw
            }

            Start-Sleep -Milliseconds ($DelayMilliseconds * $attempt)
        }
    }
}

function Copy-DirectoryUpdate {
    param(
        [string] $Source,
        [string] $Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
        if ($file.Name.EndsWith(".meta", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $relative = Get-RelativePathCompat -BasePath $Source -Path $file.FullName
        $target = Join-Path $Destination $relative
        $targetDir = Split-Path -Parent $target
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        Copy-ItemWithRetry -Source $file.FullName -Destination $target
    }
}

function Install-ModOutput {
    param(
        [object] $Mod,
        [string] $RepoRoot,
        [string] $DllPath,
        [string] $ModsLocalRoot
    )

    $installRoot = Join-Path $ModsLocalRoot $Mod.ModsLocalFolder
    $dllDir = if ([string]::IsNullOrWhiteSpace($Mod.DllTargetSubfolder)) {
        $installRoot
    } else {
        Join-Path $installRoot $Mod.DllTargetSubfolder
    }

    New-Item -ItemType Directory -Path $dllDir -Force | Out-Null
    $dllTarget = Join-Path $dllDir ([System.IO.Path]::GetFileName($DllPath))
    Copy-ItemWithRetry -Source $DllPath -Destination $dllTarget
    Write-Step ("Install target path: " + $dllTarget)

    $thumbnail = Join-Path $Mod.SourceDir "thumbnail.png"
    if (Test-Path -LiteralPath $thumbnail -PathType Leaf) {
        $thumbnailTarget = Join-Path $installRoot "thumbnail.png"
        New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
        Copy-ItemWithRetry -Source $thumbnail -Destination $thumbnailTarget
        Write-Step ("Copied thumbnail path (overwritten if present): " + $thumbnailTarget)
    } else {
        Write-BuildWarning ("Mod '" + $Mod.ModName + "' is missing thumbnail.png at " + $thumbnail)
    }

    $locales = Join-Path $Mod.SourceDir "Locales"
    if (Test-Path -LiteralPath $locales -PathType Container) {
        $localesTarget = Join-Path $installRoot "Locales"
        Copy-DirectoryUpdate -Source $locales -Destination $localesTarget
        Write-Step ("Copied Locales path: " + $localesTarget)
    } else {
        Write-BuildWarning ("Mod '" + $Mod.ModName + "' is missing Locales folder at " + $locales)
    }
}

function Test-BuiltDll {
    param(
        [string] $DllPath,
        [string] $ExpectedAssemblyName,
        [object] $Mod
    )

    if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) {
        throw "Expected output DLL was not created: $DllPath"
    }

    $file = Get-Item -LiteralPath $DllPath
    if ($file.Length -le 0) {
        throw "Output DLL is empty: $DllPath"
    }

    $actualName = [System.Reflection.AssemblyName]::GetAssemblyName($DllPath).Name
    if ($actualName -ne $ExpectedAssemblyName) {
        throw "Output assembly name '$actualName' did not match expected '$ExpectedAssemblyName'."
    }

    $sourceText = ($Mod.Sources | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
    if ($sourceText -match "RegisterModClass|IModBigAmbitions") {
        Write-Step ("Post-build check: " + $ExpectedAssemblyName + " has mod entry-point source markers.")
    } else {
        Write-Step ("Post-build check: no RegisterModClass/IModBigAmbitions marker found in source.")
    }
}

$repoRoot = Get-RepositoryRoot
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $repoRoot "tools\external-build\mods.externalbuild.json"
}
if ([string]::IsNullOrWhiteSpace($ModsLocalRoot)) {
    $ModsLocalRoot = Get-DefaultModsLocalRoot
}

Write-Step ("Detected repo root: " + $repoRoot)
Write-Step ("Config path: " + $ConfigPath)
Write-Step ("Unity Editor path: " + $UnityEditorPath)
Write-Step ("ModsLocal root: " + $ModsLocalRoot)

$overrides = Get-ConfigOverrides -Path $ConfigPath
$detectedMods = Get-DetectedMods -RepoRoot $repoRoot -Overrides $overrides

Write-Step "Detected mod folders:"
foreach ($mod in $detectedMods) {
    if (-not $mod.Enabled) {
        Write-Step ("  - " + $mod.ModName + " skipped: disabled in config")
    } elseif (@($mod.Sources).Count -eq 0) {
        Write-Step ("  - " + $mod.ModName + " skipped: no runtime C# sources under Scripts")
    } else {
        Write-Step ("  - " + $mod.ModName + " (" + @($mod.Sources).Count + " source file(s), assembly " + $mod.AssemblyName + ")")
        if (-not $mod.HasThumbnail) {
            Write-BuildWarning ("Mod '" + $mod.ModName + "' is missing thumbnail.png at " + (Join-Path $mod.SourceDir "thumbnail.png"))
        }
        if (-not $mod.HasLocales) {
            Write-BuildWarning ("Mod '" + $mod.ModName + "' is missing Locales folder at " + (Join-Path $mod.SourceDir "Locales"))
        }
    }
}

if ($List) {
    $buildableMods = @($detectedMods | Where-Object { $_.Enabled -and @($_.Sources).Count -gt 0 })
    Write-Step "Buildable mods:"
    foreach ($mod in $buildableMods) {
        Write-Step ("  - " + $mod.ModName + " | assembly=" + $mod.AssemblyName + " | source=" + $mod.SourceDirRelative)
    }
    Write-Step ("Buildable mod count: " + $buildableMods.Count)
    return
}

Assert-DependencyPaths -RepoRoot $repoRoot -UnityEditorPath $UnityEditorPath

if ($All) {
    $selectedMods = @($detectedMods | Where-Object { $_.Enabled -and @($_.Sources).Count -gt 0 })
} else {
    $selectedMods = @($detectedMods | Where-Object { $_.ModName -ieq $ModName -or $_.AssemblyName -ieq $ModName } | Select-Object -First 1)
    if ($selectedMods.Count -eq 0) {
        throw "Mod '$ModName' was not found. Use -All to build every detected mod."
    }
    if (-not $selectedMods[0].Enabled) {
        throw "Mod '$($selectedMods[0].ModName)' is disabled in external build config."
    }
    if (@($selectedMods[0].Sources).Count -eq 0) {
        throw "Mod '$($selectedMods[0].ModName)' has no runtime C# sources under Scripts."
    }
}

Write-Step "Selected mod(s):"
foreach ($mod in $selectedMods) {
    Write-Step ("  - " + $mod.ModName)
}

$references = Get-ReferenceFiles -RepoRoot $repoRoot -UnityEditorPath $UnityEditorPath
Write-Step ("Reference count: " + $references.Count)

$results = [System.Collections.Generic.List[object]]::new()
foreach ($mod in $selectedMods) {
    Write-Step ("Building " + $mod.ModName + "...")

    try {
        if (-not (Test-Path -LiteralPath $mod.SourceDir -PathType Container)) {
            throw "Source folder not found: $($mod.SourceDir)"
        }

        $buildProject = New-GeneratedProject -RepoRoot $repoRoot -Mod $mod -References $references -Configuration $Configuration
        Write-Step ("Generated project path: " + $buildProject.ProjectPath)

        Invoke-ModBuild `
            -ProjectPath $buildProject.ProjectPath `
            -Configuration $Configuration `
            -OutputDir $buildProject.RunOutputDir `
            -IntermediateDir $buildProject.RunIntermediateDir

        $outputDll = Copy-StableOutputDll -RunOutputDll $buildProject.RunOutputDll -StableOutputDll $buildProject.StableOutputDll
        Test-BuiltDll -DllPath $outputDll -ExpectedAssemblyName $mod.AssemblyName -Mod $mod

        if ($Install) {
            Install-ModOutput -Mod $mod -RepoRoot $repoRoot -DllPath $outputDll -ModsLocalRoot $ModsLocalRoot
        }

        $results.Add([pscustomobject]@{ ModName = $mod.ModName; Status = "Built"; Detail = $outputDll }) | Out-Null
    } catch {
        $results.Add([pscustomobject]@{ ModName = $mod.ModName; Status = "Failed"; Detail = $_.Exception.Message }) | Out-Null
        Write-Error ("[" + $mod.ModName + "] " + $_.Exception.Message)
        if (-not $All) {
            throw
        }
    }
}

Write-Step "Build summary:"
foreach ($result in $results) {
    Write-Step ("  - " + $result.ModName + ": " + $result.Status + " - " + $result.Detail)
}

if (@($results | Where-Object { $_.Status -eq "Failed" }).Count -gt 0) {
    exit 1
}
