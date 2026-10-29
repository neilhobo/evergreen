# Shared migration engine. Dot-sourced by migrate-db.ps1, which supplies the
# project-specific bits and inherits this scope:
#
#   $sqlDir         folder holding the .sql scripts
#   $migrationGlobs ordered globs deciding which files count as migrations
#   $historyTable   bookkeeping table in the target database
#   $historyColumn  its script-name column
#   $runnerPath     path of the calling migrate-db.ps1, for the hint messages
#   $ConnectionString / $Target and the switches from migrate-db.ps1's param block
#
# This file is deliberately identical across every project that has a
# "npm run deploy:api" - if you fix something here, copy it to the others.

if (-not $sqlDir)         { throw "migrate-run.ps1: caller must set `$sqlDir." }
if (-not $migrationGlobs) { $migrationGlobs = @('*.sql') }
if (-not $historyTable)   { $historyTable = '__SqlMigrationsApplied' }
if (-not $historyColumn)  { $historyColumn = 'ScriptName' }
if (-not $runnerPath)     { $runnerPath = $PSCommandPath }

$csb = New-Object System.Data.Common.DbConnectionStringBuilder
$csb.set_ConnectionString($ConnectionString)
$server = $csb['Server']
if (-not $server) { $server = $csb['Data Source'] }
$database = $csb['Database']
if (-not $database) { $database = $csb['Initial Catalog'] }
$user = $csb['User Id']
if (-not $user) { $user = $csb['User ID'] }
$password = $csb['Password']
$trusted = ($csb['Trusted_Connection'] -eq 'True') -or ($csb['Integrated Security'] -eq 'True')
if (-not $server -or -not $database) { throw "Could not read Server/Database out of the '$Target' connection string." }

# Find sqlcmd - not always on PATH on dev machines.
$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
if (-not $sqlcmd) {
    $sqlcmd = Get-ChildItem "C:\Program Files\Microsoft SQL Server" -Recurse -Filter sqlcmd.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $sqlcmd) { throw "sqlcmd not found. Install SQL Server tools or add sqlcmd to PATH." }

$auth = if ($trusted) { @('-E') } else { @('-U', $user, '-P', $password) }
$conn = @('-S', $server, '-d', $database) + $auth + @('-l', '30')

function Invoke-Sql {
    param([string]$Query, [string]$File, [switch]$Quiet)
    $sqlArgs = $conn + @('-b')
    if ($Query) { $sqlArgs += @('-Q', $Query) }
    if ($File)  { $sqlArgs += @('-i', $File) }
    if ($Quiet) { $sqlArgs += @('-h', '-1', '-W') }
    & $sqlcmd @sqlArgs 2>&1
}

function Normalize-Name {
    param([string]$Name)
    if ($Name -notlike '*.sql') { return "$Name.sql" }
    return $Name
}

Write-Host "==> $Target : $database on $server" -ForegroundColor Cyan

# ---- migration history ------------------------------------------------------

$historyRaw = (Invoke-Sql -Quiet -Query "SET NOCOUNT ON; SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables WHERE name = '$historyTable') THEN 1 ELSE 0 END;") -join ''
if ($LASTEXITCODE -ne 0) { throw "Could not reach $database on $server.`n$historyRaw" }
$historyExists = $historyRaw.Trim() -eq '1'

$applied = @{}
if ($historyExists) {
    $appliedRaw = Invoke-Sql -Quiet -Query "SET NOCOUNT ON; SELECT $historyColumn FROM dbo.$historyTable;"
    if ($LASTEXITCODE -ne 0) { throw "Failed to read dbo.$historyTable." }
    foreach ($line in $appliedRaw) {
        $name = ([string]$line).Trim()
        if ($name) { $applied[$name] = $true }
    }
}

# The globs are ordered (e.g. Create* before Add*), so keep that order rather
# than re-sorting the whole set by name.
$scripts = @()
$seen = @{}
foreach ($glob in $migrationGlobs) {
    foreach ($f in (Get-ChildItem (Join-Path $sqlDir $glob) -ErrorAction SilentlyContinue | Sort-Object Name)) {
        if (-not $seen.ContainsKey($f.FullName)) { $seen[$f.FullName] = $true; $scripts += $f }
    }
}

# -Only narrows what runs, not what counts as history: the first-run guard below must
# still see the whole folder, or a single new script can never pass it.
$allScripts = $scripts
if ($Only) {
    $wanted = @($Only | ForEach-Object { (Normalize-Name $_).ToLower() })
    $matched = @($scripts | Where-Object { $wanted -contains $_.Name.ToLower() })
    $missing = @($wanted | Where-Object { $_ -notin @($matched | ForEach-Object { $_.Name.ToLower() }) })
    if ($missing) { throw "No such migration script(s): $($missing -join ', ')" }
    $scripts = $matched
}

# A file whose header carries "-- @manual" is never applied automatically. It is
# reported as outstanding on every deploy until someone runs it deliberately
# with -IncludeManual. That is for heavy data backfills and one-off fixes: a
# deploy window, with the app offline, is the wrong place for them.
$manual = @()
if (-not $IncludeManual) {
    $auto = @()
    foreach ($s in $scripts) {
        if ((Get-Content $s.FullName -Raw) -match '(?m)^\s*--\s*@manual\b') {
            if (-not $applied.ContainsKey($s.Name)) { $manual += $s.Name }
        } else {
            $auto += $s
        }
    }
    $scripts = @($auto)
}

$pending  = @($scripts | Where-Object { -not $applied.ContainsKey($_.Name) })
$upToDate = $scripts.Count - $pending.Count

function Write-ManualWarning {
    if ($manual.Count -gt 0) {
        Write-Host ""
        Write-Warning "OUTSTANDING MANUAL MIGRATION(S) on '$Target': $($manual -join ', ')"
        Write-Warning "These are never auto-applied. To run one:"
        Write-Warning "  powershell -File $runnerPath -Target $Target -IncludeManual -Only $($manual[0])"
    }
}

if ($pending.Count -eq 0) {
    Write-Host "==> Up to date ($upToDate scripts already applied)." -ForegroundColor Green
    Write-ManualWarning
    exit 0
}

Write-Host "    $($pending.Count) outstanding, $upToDate already applied:"
foreach ($p in $pending) { Write-Host "      $($p.Name)" }
Write-Host ""

if ($List) { Write-ManualWarning; exit 0 }

# ---- helpers that write -----------------------------------------------------

function Ensure-HistoryTable {
    $ensure = @"
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '$historyTable')
    CREATE TABLE dbo.$historyTable (
        $historyColumn NVARCHAR(260) NOT NULL PRIMARY KEY,
        AppliedUtc DATETIME2 NOT NULL CONSTRAINT DF_${historyTable}_AppliedUtc DEFAULT (SYSUTCDATETIME())
    );
"@
    Invoke-Sql -Query $ensure | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to ensure migrations tracking table." }
}

function Record-Applied {
    param([string]$Name)
    $escaped = $Name.Replace("'", "''")
    Invoke-Sql -Query "IF NOT EXISTS (SELECT 1 FROM dbo.$historyTable WHERE $historyColumn = N'$escaped') INSERT INTO dbo.$historyTable ($historyColumn) VALUES (N'$escaped');" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to record $Name in dbo.$historyTable." }
}

# ---- baseline ---------------------------------------------------------------

if ($Baseline) {
    $keepPending = @()
    if ($Except) { $keepPending = @($Except | ForEach-Object { (Normalize-Name $_).ToLower() }) }

    Ensure-HistoryTable
    $marked = 0
    foreach ($script in $pending) {
        if ($keepPending -contains $script.Name.ToLower()) {
            Write-Host "  -- $($script.Name) left outstanding" -ForegroundColor Yellow
            continue
        }
        Record-Applied $script.Name
        $marked++
    }
    Write-Host ""
    Write-Host "==> Baselined: $marked script(s) recorded as already applied on '$Target'." -ForegroundColor Green
    if ($keepPending) { Write-Host "    $($pending.Count - $marked) left outstanding - run the migration again to apply them." }
    Write-ManualWarning
    exit 0
}

# ---- first run against a database that predates the tracking table ----------
#
# The table merely existing isn't enough: on a database shared with another
# repo's runner it can already be there with none of THIS project's scripts in
# it. Either way every script looks outstanding, and re-running a whole schema
# line against a database that already has it is not a call a deploy should
# make on its own.

$anyOfMineRecorded = @($allScripts | Where-Object { $applied.ContainsKey($_.Name) }).Count -gt 0
if (-not $anyOfMineRecorded -and $Target -ne 'local' -and -not $FirstRun) {
    Write-Host "'$Target' has no migration history for this project, so every script above looks outstanding." -ForegroundColor Yellow
    Write-Host "If that database already has this schema, record what it has before applying anything:" -ForegroundColor Yellow
    Write-Host "    npm run migrate:$Target -- -Baseline" -ForegroundColor Yellow
    Write-Host "  or, keeping the genuinely outstanding ones:" -ForegroundColor Yellow
    Write-Host "    npm run migrate:$Target -- -Baseline -Except SomeScript.sql,OtherScript.sql" -ForegroundColor Yellow
    Write-Host "If it really is a new, empty database, apply the lot with:" -ForegroundColor Yellow
    Write-Host "    npm run migrate:$Target -- -FirstRun" -ForegroundColor Yellow
    throw "Refusing to run $($pending.Count) scripts against '$Target' with no migration history."
}

# ---- apply ------------------------------------------------------------------

if (-not $Yes -and $Target -ne 'local') {
    $answer = Read-Host "Apply the $($pending.Count) script(s) above to '$Target' ($database)? [y/N]"
    if ($answer -notmatch '^\s*(y|yes)\s*$') { throw "Aborted - nothing was applied to '$Target'." }
}

Ensure-HistoryTable

$ran = 0; $fail = 0
foreach ($script in $pending) {
    Write-Host "  -> $($script.Name) ... " -NoNewline
    $output = Invoke-Sql -File $script.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED" -ForegroundColor Red
        Write-Host $output
        $fail++
        break
    }
    Record-Applied $script.Name
    Write-Host "ok" -ForegroundColor Green
    $ran++
}

Write-Host ""
if ($fail -gt 0) {
    Write-Host "==> $ran applied, $upToDate already up to date, $fail FAILED" -ForegroundColor Red
    exit 1
}
Write-Host "==> $ran newly applied, $upToDate already up to date." -ForegroundColor Green
Write-ManualWarning

exit 0
