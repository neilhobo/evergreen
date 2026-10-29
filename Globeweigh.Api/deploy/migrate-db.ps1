# Applies Globeweigh.Api\Data\Migrations scripts that haven't been run yet
# against a chosen database (local / prod). Applied scripts are recorded in a
# __SqlMigrationsApplied table in that database, so each script runs exactly
# once per DB.
#
# Every *.sql there is applied in file-name order EXCEPT files whose header
# carries "-- @manual" - the data backfills and one-off corrections. Those are
# reported as outstanding on every deploy until someone runs one deliberately,
# rather than being run inside a deploy window with the warehouse waiting.
#
# Usage:
#   npm run migrate:local
#   npm run migrate:prod -- -List        # show what is outstanding, change nothing
#   npm run migrate:prod -- -Baseline    # record existing scripts as already applied
#   npm run migrate:prod -- -Baseline -Except AddStockTransfer.sql
#   npm run migrate:prod                 # apply outstanding scripts (prompts first)
#   powershell -File Globeweigh.Api\deploy\migrate-db.ps1 -Target prod -IncludeManual -Only BackfillManualPalletsScanned.sql

param(
    [ValidateSet('local', 'prod')]
    [string]$Target = 'local',

    # Overrides -Target. Full ADO.NET connection string.
    [string]$ConnectionString,

    # Where the live API is deployed. -Target prod reads its connection string
    # from this share, so it can never drift from what the app actually uses.
    [string]$ServerRoot = '\\evergreen\Globeweigh\EvergreenApi',

    # Show outstanding scripts and exit without touching the database.
    [switch]$List,

    # Record outstanding scripts as applied WITHOUT running them - used once per
    # existing database to say "this is where its schema already is".
    [switch]$Baseline,

    # With -Baseline: leave these scripts outstanding instead of marking them
    # applied, because the database genuinely hasn't had them yet.
    [string[]]$Except,

    # Limit the run (or the baseline) to these script names.
    [string[]]$Only,

    # Also run files marked "-- @manual".
    [switch]$IncludeManual,

    # This target is a brand-new, empty database - apply everything rather than
    # stopping to ask for a baseline.
    [switch]$FirstRun,

    # Skip the confirmation prompt on non-local targets.
    [switch]$Yes
)

$ErrorActionPreference = "Stop"

$apiDir = Resolve-Path "$PSScriptRoot\.."

# ---- what this project's migrations look like -------------------------------

$sqlDir         = Resolve-Path "$apiDir\Data\Migrations"
$migrationGlobs = @('*.sql')
$historyTable   = '__SqlMigrationsApplied'
$historyColumn  = 'ScriptName'
$runnerPath     = $PSCommandPath

# ---- connection -------------------------------------------------------------

# The server's appsettings.json is written from the server's point of view, so
# its connection string reads "Server=localhost\NTPRO". Run that verbatim from a
# dev box and it resolves to the DEV machine's SQL instead - the migration would
# report success having touched the wrong database (or none at all). So a local
# alias is ALWAYS rewritten to the deploy share's host, and if we can't work out
# that host we refuse rather than guess.
function Resolve-ServerAlias {
    param([string]$Cs, [string]$UncPath)

    $csb = New-Object System.Data.Common.DbConnectionStringBuilder
    $csb.set_ConnectionString($Cs)
    $srv = $csb['Server']
    if (-not $srv) { $srv = $csb['Data Source'] }
    if (-not $srv) { throw "No Server= in the connection string read from $UncPath." }

    # "localhost\NTPRO", "(local)\X", ".\X", "localhost", "." - all mean
    # "whatever box this config is sitting on".
    if ($srv -notmatch '^\s*(\(local\)|localhost|\.)(\\(?<inst>.+))?\s*$') { return $Cs }
    $instance = $Matches['inst']   # grab it before the next -match overwrites $Matches

    if ($UncPath -notmatch '^\\\\(?<h>[^\\]+)\\') {
        throw "'$srv' in $UncPath is server-relative and -ServerRoot ('$UncPath') isn't a UNC path, so the real host is unknown. Pass -ConnectionString explicitly."
    }
    $newServer = $Matches['h']
    if ($instance) { $newServer = "$newServer\$instance" }

    $csb['Server'] = $newServer
    Write-Host "    (server-relative '$srv' resolved to '$newServer' via $UncPath)" -ForegroundColor DarkGray
    return $csb.ConnectionString
}

function Get-TargetConnectionString {
    param([string]$Name)

    # An env var wins, so a build agent never needs a checked-in password.
    $envName = "EVERGREEN_$($Name.ToUpper())_CONNECTION"
    $fromEnv = [Environment]::GetEnvironmentVariable($envName)
    if ($fromEnv) { return $fromEnv }

    if ($Name -eq 'prod') {
        # Read what the live API is actually configured with. The repo's own
        # appsettings.json is the dev copy - it is deliberately excluded from
        # the deploy for exactly that reason.
        $settings = Join-Path $ServerRoot 'appsettings.json'
        if (-not (Test-Path $settings)) {
            throw "Can't reach $settings. Is Tailscale up / the share reachable? Or set $envName."
        }
        $cs = (Get-Content $settings -Raw | ConvertFrom-Json).ConnectionStrings.GbEvergreen
        if (-not $cs) { throw "No 'GbEvergreen' connection string in $settings (or set $envName)." }
        return Resolve-ServerAlias $cs $ServerRoot
    }

    $settings = Resolve-Path "$apiDir\appsettings.json"
    $cs = (Get-Content $settings -Raw | ConvertFrom-Json).ConnectionStrings.GbEvergreen
    if (-not $cs) { throw "No 'GbEvergreen' connection string in $settings (or set $envName)." }
    return $cs
}

if (-not $ConnectionString) { $ConnectionString = Get-TargetConnectionString $Target }

. "$PSScriptRoot\migrate-run.ps1"
