<#
  Shared release guards for the two server-hosted UIs:

      \\evergreen\Globeweigh\EvergreenApi\wwwroot\admin   (Globeweigh.TauriAdmin)
      \\evergreen\Globeweigh\EvergreenApi\wwwroot\touch   (Globeweigh.TauriTouch)

  Both are "the app" for every screen that loads them - the installed Tauri shells carry no
  UI of their own. One folder on the server, four-plus PCs reading it.

  WHY THIS FILE EXISTS
  --------------------
  On 2026-08-05 the Touch UI silently rolled back five weeks. The 10:12 release was correct;
  the 14:44 API deploy then overwrote wwwroot\touch\index.html with a stale 2026-07-27 copy
  that `dotnet publish` had swept out of the dev box's source-tree wwwroot. Nothing errored:
  a UI release never purges old bundles (so a screen refreshing mid-deploy can still load its
  assets), so the reverted index.html found its July bundles and ran perfectly - just five
  weeks out of date. The only symptom anywhere in the system was the version string in the
  corner of the screen, spotted by the client five days later.

  Two guards, because the incident needed both:

    Assert-UiNotDowngrade  - a release refuses to replace a newer build with an older one.
    Assert-UiDeployed      - after copying, the server must actually report the build we
                             just shipped, on disk AND over HTTP. A deploy that lands
                             somewhere else, or gets overwritten, fails loudly instead of
                             printing "Deployed OK".

  Plus Get-UiBuild / Assert-UiUnchanged, used by the API deploy as a tripwire so a publish
  can never quietly rewrite a UI folder it does not own.

  Dot-source from a deploy script:  . (Join-Path $repo 'deploy-ui-common.ps1')
#>

# Reads <Path>\version.json (emitted by both Vite builds as {buildId, buildVersion}).
# Returns $null when absent or unreadable - callers decide what that means.
function Get-UiBuild {
    param([Parameter(Mandatory)][string]$Path)

    $file = Join-Path $Path 'version.json'
    if (-not (Test-Path $file)) { return $null }
    try {
        $json = Get-Content $file -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if (-not $json.buildId) { return $null }
        # buildId is an ISO-8601 UTC stamp; compare in UTC so a BST/GMT change can't
        # make an older build look newer.
        $utc = [datetime]::Parse(
            $json.buildId,
            [cultureinfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::AdjustToUniversal)
        [pscustomobject]@{
            BuildId = $utc
            Version = [string]$json.buildVersion
            Source  = $file
        }
    } catch { $null }
}

# Refuses to replace a newer deployed build with an older one. Returns the build being
# shipped so the caller can hand it to Assert-UiDeployed.
#
# In normal use this never fires: a fresh `npm run build` is always newer than what is
# live. It fires on -SkipBuild pushes of a stale dist\, on a rerun of an old checkout,
# and on anything else that would move the server backwards in time.
function Assert-UiNotDowngrade {
    param(
        [Parameter(Mandatory)][string]$Dist,
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][string]$Name,
        [switch]$AllowOlder
    )

    $new = Get-UiBuild $Dist
    if (-not $new) {
        throw "No readable version.json in $Dist - refusing to deploy a build that cannot identify itself."
    }

    $live = Get-UiBuild $Target
    if (-not $live) {
        # First deploy into an empty folder, or a version.json that has been damaged.
        # Nothing to compare against, so nothing to protect - but say so out loud,
        # because a MISSING version.json on a live server is itself suspicious.
        if (Test-Path (Join-Path $Target 'index.html')) {
            Write-Warning "$Name at $Target has an index.html but no readable version.json - cannot check for a rollback. Deploying $($new.Version) anyway."
        } else {
            Write-Host "$Name : no existing build at $Target - first deploy of $($new.Version)." -ForegroundColor DarkGray
        }
        return $new
    }

    if ($live.BuildId -gt $new.BuildId) {
        $msg = @(
            "ROLLBACK BLOCKED - $Name on the server is NEWER than the build you are deploying."
            "    live on server : $($live.Version)  ($($live.BuildId.ToString('u')))"
            "    about to write : $($new.Version)  ($($new.BuildId.ToString('u')))"
            "  Every screen loads this folder, and old bundles are kept for 14 days, so an"
            "  older index.html would load cleanly and run silently out of date."
        ) -join [Environment]::NewLine
        if (-not $AllowOlder) {
            throw ($msg + [Environment]::NewLine + "  Rebuild, or pass -AllowOlder if you are deliberately rolling back.")
        }
        Write-Warning ($msg + [Environment]::NewLine + "  Proceeding: -AllowOlder was passed.")
    } elseif ($live.BuildId -eq $new.BuildId) {
        Write-Host "$Name : server already on $($live.Version) - redeploying the same build." -ForegroundColor DarkGray
    }
    return $new
}

# Confirms the copy actually took: the target folder must now report $Expected, and the
# live URL must serve it too. Catches a deploy that landed in the wrong folder, was
# overwritten mid-flight, or is being served from somewhere other than we think.
function Assert-UiDeployed {
    param(
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)][string]$Name,
        [string]$Url
    )

    $onDisk = Get-UiBuild $Target
    if (-not $onDisk) {
        throw "$Name : copied to $Target but no readable version.json is there afterwards."
    }
    if ($onDisk.BuildId -ne $Expected.BuildId) {
        throw "$Name : copy finished but $Target reports $($onDisk.Version), not the $($Expected.Version) just deployed. Something else is writing to this folder."
    }

    if (-not $Url) { return }
    $versionUrl = ($Url.TrimEnd('/')) + '/version.json'

    # Fetch and check separately, on purpose. Not reaching the site (VPN down, IIS
    # stopped) is a different problem from the site serving the wrong build: the first
    # is a warning - the files on disk are already correct - the second is a failure.
    # Do NOT merge these into one try with typed catches: Windows PowerShell 5.1 cannot
    # resolve [System.Net.Http.HttpRequestException], so the catch clause itself throws.
    try {
        # no-store: IIS must not hand us a cached copy of the very file we are verifying.
        $r = Invoke-WebRequest $versionUrl -UseBasicParsing -TimeoutSec 20 -Headers @{ 'Cache-Control' = 'no-store' }
    } catch {
        # A status code means the site ANSWERED and said no - version.json is not being
        # served from where we just deployed, which is a genuine broken release.
        # No status code means we never reached it at all: warn and move on.
        $status = $null
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        if ($status) {
            throw "$Name : $versionUrl returned HTTP $status. The build is on disk at $Target but is not being served from there."
        }
        Write-Warning "$Name deployed to disk as $($Expected.Version), but $versionUrl could not be checked: $($_.Exception.Message)"
        return
    }

    $served = $null
    try { $served = ($r.Content | ConvertFrom-Json).buildVersion } catch { }
    if (-not $served) {
        throw "$Name : $versionUrl did not return a version.json - the UI may not be served from $Target. Got: $($r.Content -replace '\s+',' ' -replace '^(.{120}).*','$1...')"
    }
    if ($served -ne $Expected.Version) {
        throw "$Name : $versionUrl is serving $served, not the $($Expected.Version) just deployed."
    }
    Write-Host "Verified - $Url is serving $($Expected.Version)." -ForegroundColor Green
}

# Tripwire for deploys that are NOT meant to touch a UI folder (the API publish).
# Snapshot before, compare after: if the folder moved at all, something shipped a UI
# it does not own - which is exactly how the 2026-08-05 rollback happened.
function Assert-UiUnchanged {
    param(
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][string]$Name,
        $Before
    )

    $after = Get-UiBuild $Target
    $b = if ($Before) { $Before.Version } else { '(none)' }
    $a = if ($after)  { $after.Version }  else { '(none)' }
    if ($b -ne $a) {
        throw "$Name was changed by a deploy that must not touch it: $b -> $a. The server-hosted UI is released only by its own deploy-*-ui.ps1."
    }
}
