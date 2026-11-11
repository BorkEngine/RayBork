#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Rebuilds the committed SPIR-V blobs beside their GLSL sources. The runtime never compiles shaders
# and CI only consumes the committed .spv files, so this is a development-time tool that needs the
# Vulkan SDK.
$repoRoot = Split-Path -Parent $PSScriptRoot
$shaderRoot = Join-Path $repoRoot 'src/RayBork/RayBork.Engine/Shaders'

# glslc rejects -g0 in the installed Vulkan SDK and emits no debug information unless -g is passed,
# so leaving it out keeps the blobs reproducible without adding debug sections.
$compileArguments = @('--target-env=vulkan1.2', '-O', '-Werror')
$stageBySuffix = @{
    '.vert.glsl' = 'vert'
    '.frag.glsl' = 'frag'
}

function Resolve-GlslcPath {
    $vulkanSdk = [Environment]::GetEnvironmentVariable('VULKAN_SDK')
    if (-not [string]::IsNullOrWhiteSpace($vulkanSdk)) {
        $candidates = @(
            (Join-Path $vulkanSdk 'Bin\glslc.exe'),
            (Join-Path $vulkanSdk 'bin/glslc')
        )
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }

    $command = Get-Command -Name 'glslc' -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $command) {
        return $command.Source
    }

    throw ('glslc was not found. The Vulkan SDK is required to regenerate shader SPIR-V: set ' +
        'VULKAN_SDK or put glslc on PATH. The committed .spv files are the build inputs, so a ' +
        'normal build and CI never need glslc.')
}

function Get-ShaderStage([System.IO.FileInfo]$source) {
    foreach ($suffix in $stageBySuffix.Keys) {
        if ($source.Name.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $stageBySuffix[$suffix]
        }
    }

    throw ("Cannot infer the shader stage from '$($source.Name)'. A shader source must end in " +
        "$($stageBySuffix.Keys -join ' or ').")
}

if (-not (Test-Path -LiteralPath $shaderRoot -PathType Container)) {
    throw "Shader source directory '$shaderRoot' does not exist."
}

$glslc = Resolve-GlslcPath
$sources = @(Get-ChildItem -LiteralPath $shaderRoot -Filter '*.glsl' -File | Sort-Object FullName)
if ($sources.Count -eq 0) {
    throw "No GLSL sources were found under '$shaderRoot'."
}

$failures = [System.Collections.Generic.List[string]]::new()

foreach ($source in $sources) {
    $stage = Get-ShaderStage $source
    $output = Join-Path $source.DirectoryName ([System.IO.Path]::GetFileNameWithoutExtension($source.Name) + '.spv')
    $sourceRelative = [System.IO.Path]::GetRelativePath($repoRoot, $source.FullName)
    $outputRelative = [System.IO.Path]::GetRelativePath($repoRoot, $output)

    & $glslc @compileArguments "-fshader-stage=$stage" -o $output $source.FullName
    if ($LASTEXITCODE -ne 0) {
        $failures.Add("glslc failed with exit code $LASTEXITCODE for '$sourceRelative'.")
        continue
    }

    $size = (Get-Item -LiteralPath $output).Length
    Write-Output "$sourceRelative -> $outputRelative ($size bytes)"
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Compiled $($sources.Count) shader sources with '$glslc'."
