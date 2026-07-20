# Rxns framework build script
#
# Builds the rxns layers + the AppStatus.Host stack, optionally rebuilds the
# portal SPA bundle and runs the host test suite.
#
# Usage:
#   .\build-rxns.ps1                  # core + host
#   .\build-rxns.ps1 -Test           # ... + run Rxns.AppStatus.Host.Tests (net10)
#   .\build-rxns.ps1 -Portal         # ... + rebuild the AppStatus Web bundle (npm)
#   .\build-rxns.ps1 -All            # ... + Rxns.Azure + Rxns.Windows
#   .\build-rxns.ps1 -Config Release # Release config (default Debug for dev loop)
param(
    [string]$Config = "Debug",
    [switch]$Test,
    [switch]$Portal,
    [switch]$All,
    # Optional MSTest filter passed through `dotnet test --filter`. Supports the
    # usual TestCategory=Scanner, FullyQualifiedName~AiEngineScanner, etc.
    [string]$Filter
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "=== Rxns Framework Build ($Config) ===" -ForegroundColor Cyan

# Short clear summaries from dotnet build's chatty output (no parser-trap chars).
function Build-Project($name, $proj, $extraArgs) {
    Write-Host "Building $name..." -ForegroundColor Yellow
    $out = dotnet build $proj -c $Config -v minimal @extraArgs 2>&1
    $tail = $out | Select-String -Pattern "error CS|Build succeeded|Build FAILED|Error\(s\)" |
            Select-Object -Last 6
    $tail | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "$name build failed" }
}

# Core layers - order matters because later projects reference earlier output dirs.
Build-Project "Rxns core"             "$root\src\Rxns\Rxns.csproj"           @()
# Rxns.Autofac.csproj has a quirky <TargetFramework>netstandard2.1</TargetFramework>
# on a second PropertyGroup that overrides the <TargetFrameworks>netstandard2.0;netstandard2.1</TargetFrameworks>
# on the first. Force the netstandard2.0 build explicitly so net48 consumers
# can reference it.
Build-Project "Rxns.Autofac (ns2.0)"  "$root\src\Rxns.Autofac\Rxns.Autofac.csproj" @("-f", "netstandard2.0")
Build-Project "Rxns.Autofac (ns2.1)"  "$root\src\Rxns.Autofac\Rxns.Autofac.csproj" @()
Build-Project "Rxns.NewtonsoftJson"   "$root\src\Rxns.NewtonsoftJson\Rxns.NewtonsoftJson.csproj" @()
Build-Project "Rxns.WebApiNET5"       "$root\Rxns.WebApiNET5\Rxns.WebApiNET5.csproj" @()
Build-Project "Rxns.AppStatus.Host"          "$root\Rxns.AppStatus.Host\Rxns.AppStatus.Host.csproj" @()
Build-Project "Rxns.AppStatus.Host.Launcher" "$root\Rxns.AppStatus.Host.Launcher\Rxns.AppStatus.Host.Launcher.csproj" @()

# Optional: rebuild the AppStatus portal SPA bundle.
if ($Portal) {
    Write-Host "Building AppStatus portal bundle (npm run build)..." -ForegroundColor Yellow
    Push-Location "$root\Rxns.AppSatus\Web"
    try {
        if (-not (Test-Path "node_modules")) {
            npm install
            if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
        }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }
    } finally { Pop-Location }
}

# Optional: full suite (Azure / Windows may have NuGet quirks).
if ($All) {
    Write-Host "Building Rxns.Azure (optional)..." -ForegroundColor Yellow
    dotnet build "$root\Rxns.Azure\Rxns.Azure.csproj" -c $Config 2>&1 | Out-Null
    Write-Host "Building Rxns.Windows (optional)..." -ForegroundColor Yellow
    dotnet build "$root\Rxns.Windows\Rxns.Windows.csproj" -c $Config 2>&1 | Out-Null
}

# In-process MSTest behaviour fixture for the host.
#
# Output policy: stream `dotnet test` straight to the console. Don't capture into
# a variable and don't `Select-Object -Last 30` it — that hides every failing
# assertion's `because:` message above the cutoff and makes debugging a guess.
# Avoid `2>&1` too: under PowerShell 5.1, redirecting native stderr wraps each
# line in an ErrorRecord and poisons $LASTEXITCODE checks ("Test Run Failed."
# bleeds out as a NativeCommandError before we even read the summary).
if ($Test) {
    Build-Project "Rxns.AppStatus.Host.Tests" "$root\Rxns.AppStatus.Host.Tests\Rxns.AppStatus.Host.Tests.csproj" @()
    Write-Host "Running Rxns.AppStatus.Host.Tests..." -ForegroundColor Yellow

    # Hard caps so a misbehaving test can't wedge the build:
    #   --blame-hang-timeout 60s   per-test budget; vstest kills the runner if
    #                               a single test exceeds it and dumps the stack.
    #   RunConfiguration.TestSessionTimeout=120000  wall-clock cap on the whole
    #                               run (2 min). dotnet test honours this via the
    #                               `-- key=value` runsettings overrides.
    $testArgs = @(
        "$root\Rxns.AppStatus.Host.Tests\Rxns.AppStatus.Host.Tests.csproj",
        "-c", $Config, "--no-build",
        "--logger", "console;verbosity=normal",
        "--blame-hang-timeout", "60000ms",
        "--blame-hang-dump-type", "none"
    )
    if ($Filter) {
        Write-Host ("  applying --filter `"$Filter`"") -ForegroundColor Gray
        $testArgs += @("--filter", $Filter)
    }
    # `--` followed by runsettings overrides; must come AFTER all dotnet-test args.
    $testArgs += @("--", "RunConfiguration.TestSessionTimeout=120000")

    dotnet test @testArgs
    if ($LASTEXITCODE -ne 0) { throw "Host tests FAILED (exit $LASTEXITCODE). See output above for failed-test details." }
}

Write-Host "Rxns framework build complete." -ForegroundColor Green
