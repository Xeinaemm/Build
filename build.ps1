[CmdletBinding()]
Param(
    [Parameter(Position = 0, Mandatory = $false, ValueFromRemainingArguments = $true)]
    [string[]]$ScriptArgs
)

if (!$PSScriptRoot) {
    $PSScriptRoot = Split-Path $MyInvocation.MyCommand.Path -Parent
}

$TOOLS_DIR = Join-Path $PSScriptRoot "tools"
$ADDINS_DIR = Join-Path $TOOLS_DIR "Addins"
$MODULES_DIR = Join-Path $TOOLS_DIR "Modules"
$NUGET_EXE = Join-Path $TOOLS_DIR "nuget.exe"
$CAKE_EXE = Join-Path $TOOLS_DIR "Cake/Cake.exe"
$ADDINS_PACKAGES_CONFIG = Join-Path $ADDINS_DIR "packages.config"
$MODULES_PACKAGES_CONFIG = Join-Path $MODULES_DIR "packages.config"

# Save nuget.exe path to environment to be available to child processed
$ENV:NUGET_EXE = $NUGET_EXE

# Restore tools from NuGet?
if (Test-Path $TOOLS_DIR) {
    Push-Location
    Set-Location $TOOLS_DIR
    Invoke-Expression "&`"$NUGET_EXE`" install -ExcludeVersion -Verbosity quiet -OutputDirectory `"$TOOLS_DIR`""
    Pop-Location
}

# Restore addins from NuGet
if (Test-Path $ADDINS_PACKAGES_CONFIG) {
    Push-Location
    Set-Location $ADDINS_DIR
    Invoke-Expression "&`"$NUGET_EXE`" install -ExcludeVersion -Verbosity quiet -OutputDirectory `"$ADDINS_DIR`""
    Pop-Location
}

# Restore modules from NuGet
if (Test-Path $MODULES_PACKAGES_CONFIG) {
    Push-Location
    Set-Location $MODULES_DIR
    Invoke-Expression "&`"$NUGET_EXE`" install -ExcludeVersion -Verbosity quiet -OutputDirectory `"$MODULES_DIR`""
    Pop-Location
}

$cakeArguments = @("build.cake");
$cakeArguments += $ScriptArgs

# Start Cake
&$CAKE_EXE $cakeArguments
exit $LASTEXITCODE