using System.Data;
using System.Text;
using Globeweigh.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Globeweigh.Api.Controllers;

/// <summary>
/// Dev-only data sync: clone the whole PROD database into this machine's LOCAL database
/// (wipe + replace, all tables). Backs the TauriAdmin and TauriTouch "Sync Data" buttons.
///
/// Anonymous, not [Authorize]: TauriTouch factory screens run without a login when the
/// device row has Login disabled, so no JWT exists there; the environment gate below is
/// the real protection.
///
/// Hard safety rails — this can DELETE every row in the target DB, so:
///   * the whole controller is disabled outside the Development environment, so it does
///     not even exist on the deployed prod API (which runs as Production);
///   * the target (this API's own GbEvergreen) connection must point at a local SQL server,
///     or the sync refuses to run;
///   * the source (prod) connection string is read from config key
///     ConnectionStrings:GbEvergreenProd — set it in user-secrets or the gitignored dev
///     config, never the committed appsettings.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/dev-sync")]
public class DevDataSyncController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    /// <summary>Per-command timeout. Long enough for a whole-table scan over a slow link, but
    /// finite: with the old "wait for ever" (0) a single blocked query left the Sync Data dialog
    /// spinning with no way out.</summary>
    private const int CommandTimeoutSeconds = 300;

    public DevDataSyncController(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    public record TableSyncResult(string Table, long RowsCopied, string? Error);
    public record SyncResult(bool Success, long DurationMs, List<TableSyncResult> Tables, string? Message);

    /// <summary>Prod connection details entered in the Sync Data dialog. Server + Database are
    /// required; leave User/Password blank to use integrated (Windows) auth. Tables, when non-empty,
    /// restricts preview/sync to the listed "schema.table" names (null/empty = all tables).</summary>
    public record ProdConnInfo(
        string? Server, string? Database, string? User, string? Password, List<string>? Tables);

    /// <summary>List the local (target) user tables as "schema.table", for the table picker.</summary>
    [HttpGet("tables")]
    public async Task<ActionResult<List<string>>> Tables(CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var localCs = _config.GetConnectionString("GbEvergreen");
        if (string.IsNullOrWhiteSpace(localCs))
            return Ok(new List<string>());

        await using var conn = new SqlConnection(localCs);
        await conn.OpenAsync(ct);
        var tables = await GetUserTablesAsync(conn, ct);
        return Ok(tables.Select(t => $"{t.Schema}.{t.Table}").ToList());
    }

    // Selected-table filter, or null when every table should be processed.
    private static HashSet<string>? SelectedTables(ProdConnInfo? body) =>
        body?.Tables is { Count: > 0 } t ? new HashSet<string>(t, StringComparer.OrdinalIgnoreCase) : null;

    /// <summary>Probe the prod connection entered in the dialog (open + a trivial query).</summary>
    [HttpPost("test-connection")]
    public async Task<ActionResult> TestConnection([FromBody] ProdConnInfo body, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var prodCs = BuildProdCs(body);
        if (prodCs is null)
            return Ok(new { success = false, message = "Enter at least a server and a database name." });

        try
        {
            await using var conn = new SqlConnection(prodCs);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(
                "SELECT DB_NAME(), (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0);", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            return Ok(new { success = true, message = $"Connected to {r.GetString(0)} — {r.GetInt32(1)} table(s)." });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    /// <summary>Clone all data from the prod DB into the local DB (wipe + replace).</summary>
    [HttpPost("from-prod")]
    public async Task<ActionResult<SyncResult>> FromProd([FromBody] ProdConnInfo body, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var localCs = _config.GetConnectionString("GbEvergreen");
        // Prefer the connection entered in the dialog; fall back to a configured prod string.
        var prodCs = BuildProdCs(body) ?? _config.GetConnectionString("GbEvergreenProd");

        if (string.IsNullOrWhiteSpace(prodCs))
            return BadRequest(new
            {
                message = "No prod connection. Enter the prod server, database, user and password " +
                          "in the dialog (or set ConnectionStrings:GbEvergreenProd in appsettings.Development.json)."
            });

        if (string.IsNullOrWhiteSpace(localCs) || !IsLocalServer(localCs))
            return BadRequest(new
            {
                message = "The target (GbEvergreen) connection does not point at a local SQL server. " +
                          "Refusing to sync to be safe."
            });

        var started = DateTime.UtcNow;
        var results = new List<TableSyncResult>();

        await using var src = new SqlConnection(prodCs);
        await using var dst = new SqlConnection(localCs);
        try
        {
            await src.OpenAsync(ct);
            await dst.OpenAsync(ct);
        }
        catch (Exception ex)
        {
            return Ok(new SyncResult(false, (long)(DateTime.UtcNow - started).TotalMilliseconds,
                results, $"Could not connect: {ex.Message}"));
        }

        // Only the prod tables that also exist locally are touched. FK constraints are disabled
        // for the whole operation so per-table inserts/updates/deletes can run in any order.
        var tables = await GetUserTablesAsync(src, ct);
        var localTables = new HashSet<(string, string)>(await GetUserTablesAsync(dst, ct));
        var selected = SelectedTables(body);

        await SetAllConstraintsAsync(dst, enabled: false, ct);
        try
        {
            foreach (var (schema, table) in tables)
            {
                ct.ThrowIfCancellationRequested();
                if (!localTables.Contains((schema, table)))
                    continue; // no local table to sync into
                if (selected != null && !selected.Contains($"{schema}.{table}"))
                    continue; // not in the chosen subset

                var name = $"{schema}.{table}";
                try
                {
                    var (ins, upd, del, err) = await ApplyTableDiffAsync(src, dst, schema, table, ct);
                    if (err != null)
                        results.Add(new TableSyncResult(name, 0, err));
                    else if (ins + upd + del > 0)
                        results.Add(new TableSyncResult(name, ins + upd + del, null));
                    // Unchanged tables are skipped — not reported.
                }
                catch (Exception ex)
                {
                    results.Add(new TableSyncResult(name, 0, ex.Message));
                    // A failed bulk copy can leave IDENTITY_INSERT switched on for this table on
                    // the shared dst connection; clear it so the next table doesn't inherit the error.
                    await ResetIdentityInsertAsync(dst, $"[{schema}].[{table}]");
                }
            }
        }
        finally
        {
            // Not ct: a cancelled request must still leave the local DB with its foreign keys
            // switched back on, otherwise every later sync starts from an unconstrained database.
            await SetAllConstraintsAsync(dst, enabled: true, CancellationToken.None);
        }

        var elapsed = (long)(DateTime.UtcNow - started).TotalMilliseconds;
        var failures = results.Count(r => r.Error != null);
        var changed = results.Count(r => r.Error == null);
        var msg = failures > 0
            ? $"Synced with {failures} table error(s)."
            : changed == 0
                ? "Already up to date — local matches prod."
                : $"Synced {changed} changed table(s) from PROD.";
        return Ok(new SyncResult(failures == 0, elapsed, results, msg));
    }

    public record TablePreview(string Table, long Inserts, long Updates, long Deletes);
    public record PreviewResult(bool Success, long DurationMs, List<TablePreview> Tables, string? Message);

    /// <summary>Dry-run: compare prod (source) against local (target) and report, per table,
    /// how many rows would be inserted / updated / deleted by a sync. Rows are matched on the
    /// primary key; "updated" is a per-row CHECKSUM(*) mismatch. Only changing tables are returned.</summary>
    [HttpPost("preview")]
    public async Task<ActionResult<PreviewResult>> Preview([FromBody] ProdConnInfo body, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var localCs = _config.GetConnectionString("GbEvergreen");
        var prodCs = BuildProdCs(body) ?? _config.GetConnectionString("GbEvergreenProd");

        if (string.IsNullOrWhiteSpace(prodCs))
            return Ok(new PreviewResult(false, 0, new(), "No prod connection — test the connection first."));
        if (string.IsNullOrWhiteSpace(localCs))
            return Ok(new PreviewResult(false, 0, new(), "No local (GbEvergreen) connection configured."));

        var started = DateTime.UtcNow;
        await using var src = new SqlConnection(prodCs);
        await using var dst = new SqlConnection(localCs);
        try
        {
            await src.OpenAsync(ct);
            await dst.OpenAsync(ct);
        }
        catch (Exception ex)
        {
            return Ok(new PreviewResult(false, Elapsed(started), new(), $"Could not connect: {ex.Message}"));
        }

        var prodTables = await GetUserTablesAsync(src, ct);
        var localTables = new HashSet<(string, string)>(await GetUserTablesAsync(dst, ct));
        var selected = SelectedTables(body);
        var results = new List<TablePreview>();

        foreach (var (schema, table) in prodTables)
        {
            ct.ThrowIfCancellationRequested();
            if (selected != null && !selected.Contains($"{schema}.{table}"))
                continue; // not in the chosen subset
            var name = $"{schema}.{table}";
            try
            {
                // Table doesn't exist locally yet → every prod row is an insert.
                if (!localTables.Contains((schema, table)))
                {
                    var all = await CountAsync(src, schema, table, ct);
                    if (all > 0) results.Add(new TablePreview(name, all, 0, 0));
                    continue;
                }

                long inserts = 0, updates = 0, deletes = 0;
                var pk = await GetPrimaryKeyColumnsAsync(src, schema, table, ct);

                if (pk.Count == 0)
                {
                    // No key to match rows on — fall back to a row-count delta.
                    var (p, l) = (await CountAsync(src, schema, table, ct), await CountAsync(dst, schema, table, ct));
                    inserts = Math.Max(0, p - l);
                    deletes = Math.Max(0, l - p);
                }
                else
                {
                    try
                    {
                        var hashExpr = RowHashExpr(await GetCopyableColumnsAsync(src, schema, table, ct));
                        var prodMap = await GetKeyIndexAsync(src, schema, table, pk, hashExpr, ct);
                        var localMap = await GetKeyIndexAsync(dst, schema, table, pk, hashExpr, ct);
                        foreach (var kv in prodMap)
                        {
                            if (!localMap.TryGetValue(kv.Key, out var lr)) inserts++;
                            else if (lr.Chk != kv.Value.Chk) updates++;
                        }
                        foreach (var key in localMap.Keys)
                            if (!prodMap.ContainsKey(key)) deletes++;
                    }
                    catch
                    {
                        // CHECKSUM(*) rejects some column types (xml/text/...) — fall back to counts.
                        var (p, l) = (await CountAsync(src, schema, table, ct), await CountAsync(dst, schema, table, ct));
                        inserts = Math.Max(0, p - l);
                        deletes = Math.Max(0, l - p);
                    }
                }

                if (inserts + updates + deletes > 0)
                    results.Add(new TablePreview(name, inserts, updates, deletes));
            }
            catch (Exception ex)
            {
                results.Add(new TablePreview($"{name} — preview error: {ex.Message}", 0, 0, 0));
            }
        }

        // Busiest tables first.
        results.Sort((a, b) =>
            (b.Inserts + b.Updates + b.Deletes).CompareTo(a.Inserts + a.Updates + a.Deletes));

        var msg = results.Count == 0
            ? "No changes — local already matches prod."
            : $"{results.Count} table(s) would change.";
        return Ok(new PreviewResult(true, Elapsed(started), results, msg));
    }

    // ---- helpers ---------------------------------------------------------------

    private static long Elapsed(DateTime start) => (long)(DateTime.UtcNow - start).TotalMilliseconds;

    /// <summary>Build a prod connection string from the dialog input, or null if Server/Database
    /// are missing. SQL auth when a user is given, otherwise integrated security.</summary>
    private static string? BuildProdCs(ProdConnInfo? c)
    {
        if (c is null || string.IsNullOrWhiteSpace(c.Server) || string.IsNullOrWhiteSpace(c.Database))
            return null;

        var b = new SqlConnectionStringBuilder
        {
            DataSource = c.Server.Trim(),
            InitialCatalog = c.Database.Trim(),
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 15,
        };
        if (!string.IsNullOrWhiteSpace(c.User))
        {
            b.UserID = c.User.Trim();
            b.Password = c.Password ?? "";
        }
        else
        {
            b.IntegratedSecurity = true;
        }
        return b.ConnectionString;
    }

    private static bool IsLocalServer(string connectionString)
    {
        try
        {
            var ds = new SqlConnectionStringBuilder(connectionString).DataSource.ToLowerInvariant();
            // Strip the instance (e.g. "(local)\sqlexpress" -> "(local)").
            var host = ds.Split('\\')[0].Split(',')[0].Trim();
            return host is "(local)" or "localhost" or "127.0.0.1" or "." or ""
                   || host == Environment.MachineName.ToLowerInvariant();
        }
        catch
        {
            return false;
        }
    }

    private static async Task<List<(string Schema, string Table)>> GetUserTablesAsync(
        SqlConnection conn, CancellationToken ct)
    {
        const string sql =
            "SELECT s.name, t.name FROM sys.tables t " +
            "JOIN sys.schemas s ON t.schema_id = s.schema_id " +
            "WHERE t.is_ms_shipped = 0 AND t.name <> 'sysdiagrams' " +
            "ORDER BY s.name, t.name;";
        var list = new List<(string, string)>();
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    /// <summary>Columns the sync leaves alone on every run, as "schema.table.column". They are
    /// dropped from the copyable set, so they are neither hashed, read, updated nor bulk-inserted:
    /// the local value stays whatever it is (NULL on a fresh insert) and the table never shows as
    /// changed because of them.
    ///
    /// dbo.Setting.SmtpPassword is DPAPI-protected in machine scope (Auth/DpapiSecret.cs): the
    /// blob prod holds only decrypts on evergreen, so copying it here replaced the password typed
    /// in on this PC with one it cannot read, and Email Setup "lost" its password after every
    /// sync. The local blob is kept instead; the password is typed in once per dev PC.</summary>
    private static readonly HashSet<string> NeverSyncedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "dbo.Setting.SmtpPassword",
    };

    /// <summary>Columns we can insert into (name + base type): excludes computed columns,
    /// rowversion/timestamp, and the NeverSyncedColumns.</summary>
    private static async Task<List<(string Name, string Type)>> GetCopyableColumnsAsync(
        SqlConnection conn, string schema, string table, CancellationToken ct)
    {
        const string sql =
            "SELECT c.name, ty.name FROM sys.columns c " +
            "JOIN sys.types ty ON c.user_type_id = ty.user_type_id " +
            "WHERE c.object_id = OBJECT_ID(@t) AND c.is_computed = 0 " +
            "AND ty.name NOT IN ('timestamp', 'rowversion') " +
            "ORDER BY c.column_id;";
        var cols = new List<(string, string)>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@t", $"[{schema}].[{table}]");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var name = r.GetString(0);
            if (NeverSyncedColumns.Contains($"{schema}.{table}.{name}")) continue;
            cols.Add((name, r.GetString(1)));
        }
        return cols;
    }

    private static bool IsStringType(string t) =>
        t is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext";

    /// <summary>A collation-independent row hash over the copyable columns. String columns are
    /// converted to nvarchar so the hash is identical on prod and local regardless of each
    /// database's collation/code page — without this, every string row hashes differently when the
    /// two DB collations differ, showing up as phantom "updates" a sync can never clear. Non-copyable
    /// columns (rowversion/timestamp/computed) are excluded, so they never trigger false changes.</summary>
    private static string RowHashExpr(IReadOnlyList<(string Name, string Type)> cols)
    {
        var parts = cols.Select(c =>
            IsStringType(c.Type) ? $"CONVERT(nvarchar(max), [{c.Name}])" : $"[{c.Name}]");
        return $"BINARY_CHECKSUM({string.Join(", ", parts)})";
    }

    private static async Task<long> CountAsync(SqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT COUNT_BIG(*) FROM [{schema}].[{table}];", conn)
            { CommandTimeout = CommandTimeoutSeconds };
        var o = await cmd.ExecuteScalarAsync(ct);
        return o is long l ? l : Convert.ToInt64(o);
    }

    private static async Task<List<string>> GetPrimaryKeyColumnsAsync(
        SqlConnection conn, string schema, string table, CancellationToken ct)
    {
        const string sql =
            "SELECT c.name FROM sys.indexes i " +
            "JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id " +
            "JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id " +
            "WHERE i.is_primary_key = 1 AND i.object_id = OBJECT_ID(@t) " +
            "ORDER BY ic.key_ordinal;";
        var cols = new List<string>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@t", $"[{schema}].[{table}]");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) cols.Add(r.GetString(0));
        return cols;
    }

    /// <summary>Apply only the row-level differences for one table: insert new rows, update
    /// changed rows, delete removed ones (matched on primary key, change detected via a
    /// collation-independent row hash). Tables without a usable primary key (or column types the
    /// hash rejects) fall back to a full wipe + replace.
    ///
    /// Both sides are compared key + hash first, and only the keys that actually differ are then
    /// pulled in full. An unchanged table therefore costs two narrow scans and no row transfer —
    /// the earlier version streamed every column of every row of every table across the wire on
    /// each run, which is what made a no-op sync of dbo.CodicoPallet (~100k rows) take a minute.</summary>
    private static async Task<(long Inserts, long Updates, long Deletes, string? Error)> ApplyTableDiffAsync(
        SqlConnection src, SqlConnection dst, string schema, string table, CancellationToken ct)
    {
        var full = $"[{schema}].[{table}]";
        var typed = await GetCopyableColumnsAsync(src, schema, table, ct);
        if (typed.Count == 0) return (0, 0, 0, "No copyable columns");
        var cols = typed.Select(c => c.Name).ToList();
        var hashExpr = RowHashExpr(typed);

        var pk = await GetPrimaryKeyColumnsAsync(src, schema, table, ct);
        var pkUsable = pk.Count > 0 && pk.All(k => cols.Contains(k, StringComparer.OrdinalIgnoreCase));
        if (!pkUsable)
            return (await WipeAndReplaceAsync(src, dst, schema, table, cols, ct), 0, 0, null);

        Dictionary<string, (long Chk, object?[] Pk)> prodIndex, localIndex;
        try
        {
            prodIndex = await GetKeyIndexAsync(src, schema, table, pk, hashExpr, ct);
            localIndex = await GetKeyIndexAsync(dst, schema, table, pk, hashExpr, ct);
        }
        catch
        {
            // Hash unsupported for some column type → replace the whole table instead.
            return (await WipeAndReplaceAsync(src, dst, schema, table, cols, ct), 0, 0, null);
        }

        var insertKeys = new List<object?[]>();
        var updateKeys = new List<object?[]>();
        foreach (var kv in prodIndex)
        {
            if (!localIndex.TryGetValue(kv.Key, out var lr)) insertKeys.Add(kv.Value.Pk);
            else if (lr.Chk != kv.Value.Chk) updateKeys.Add(kv.Value.Pk);
        }
        // Keys only in the local index aren't in prod → delete them.
        var deletes = localIndex.Where(kv => !prodIndex.ContainsKey(kv.Key)).Select(kv => kv.Value.Pk).ToList();

        if (insertKeys.Count == 0 && updateKeys.Count == 0 && deletes.Count == 0)
            return (0, 0, 0, null); // table already matches prod — nothing to transfer

        var inserts = await FetchRowsAsync(src, full, cols, pk, insertKeys, ct);
        var updates = await FetchRowsAsync(src, full, cols, pk, updateKeys, ct);

        try
        {
            // Deletes, then updates, then inserts. Rows are matched on the primary key, but a
            // table can also carry a UNIQUE constraint on another column (dbo.FeatureFlag.Name):
            // when local has the same Name under a different Id, inserting the prod row before the
            // stale local one is gone violates that constraint. Clearing removed rows first, and
            // rewriting changed rows before adding new ones, keeps every step inside the constraint.
            if (deletes.Count > 0) await ApplyDeletesAsync(dst, full, pk, deletes, ct);
            if (updates.Count > 0) await ApplyUpdatesAsync(dst, full, typed, pk, updates, ct);
            if (inserts.Count > 0) await BulkInsertAsync(dst, full, cols, inserts, ct);
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            // Still a duplicate key (e.g. two rows swapping a unique value) — the only order that
            // always works is an empty table, so replace the whole thing. The bulk copy that just
            // failed may have left IDENTITY_INSERT on for this table; clear it or the replace fails.
            await ResetIdentityInsertAsync(dst, full);
            return (await WipeAndReplaceAsync(src, dst, schema, table, cols, ct), 0, 0, null);
        }

        return (inserts.Count, updates.Count, deletes.Count, null);
    }

    /// <summary>Read the full rows behind a set of primary keys, in batches small enough to stay
    /// under SQL Server's 2100-parameter limit.</summary>
    private static async Task<List<object?[]>> FetchRowsAsync(
        SqlConnection src, string full, List<string> cols, List<string> pk, List<object?[]> keys,
        CancellationToken ct)
    {
        var rows = new List<object?[]>(keys.Count);
        if (keys.Count == 0) return rows;

        const int batchSize = 500;
        var colList = string.Join(", ", cols.Select(c => $"[{c}]"));

        for (var offset = 0; offset < keys.Count; offset += batchSize)
        {
            var batch = keys.GetRange(offset, Math.Min(batchSize, keys.Count - offset));
            // Single-column keys collapse to one IN list; composite keys need an OR'd group per row.
            var where = pk.Count == 1
                ? $"[{pk[0]}] IN ({string.Join(", ", batch.Select((_, i) => $"@k{i}"))})"
                : string.Join(" OR ", batch.Select((_, i) =>
                    "(" + string.Join(" AND ", pk.Select((c, j) => $"[{c}] = @k{i}_{j}")) + ")"));

            await using var cmd = new SqlCommand($"SELECT {colList} FROM {full} WHERE {where};", src)
                { CommandTimeout = CommandTimeoutSeconds };
            for (var i = 0; i < batch.Count; i++)
                for (var j = 0; j < pk.Count; j++)
                    cmd.Parameters.AddWithValue(pk.Count == 1 ? $"@k{i}" : $"@k{i}_{j}", batch[i][j] ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var values = new object?[cols.Count];
                for (var i = 0; i < cols.Count; i++) values[i] = r.IsDBNull(i) ? null : r.GetValue(i);
                rows.Add(values);
            }
        }
        return rows;
    }

    private static string BuildKey(object?[] values, int[] idx)
    {
        var sb = new StringBuilder();
        foreach (var i in idx)
        {
            sb.Append(i < 0 || values[i] is null ? "\0" : Convert.ToString(values[i]));
            sb.Append('|');
        }
        return sb.ToString();
    }

    /// <summary>Index a table by primary key → (collation-independent row hash, typed key values),
    /// so rows on either side can be matched by key and compared for content changes. Only the key
    /// columns and the hash cross the wire, so this stays cheap on 100k-row tables.</summary>
    private static async Task<Dictionary<string, (long Chk, object?[] Pk)>> GetKeyIndexAsync(
        SqlConnection conn, string schema, string table, List<string> pk, string hashExpr, CancellationToken ct)
    {
        var pkList = string.Join(", ", pk.Select(c => $"[{c}]"));
        var sql = $"SELECT {pkList}, {hashExpr} AS __chk FROM [{schema}].[{table}];";
        var idx = Enumerable.Range(0, pk.Count).ToArray();
        var map = new Dictionary<string, (long, object?[])>(StringComparer.Ordinal);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var pkv = new object?[pk.Count];
            for (var i = 0; i < pk.Count; i++) pkv[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            var chk = r.IsDBNull(pk.Count) ? 0L : Convert.ToInt64(r.GetValue(pk.Count));
            map[BuildKey(pkv, idx)] = (chk, pkv);
        }
        return map;
    }

    private static async Task<long> WipeAndReplaceAsync(
        SqlConnection src, SqlConnection dst, string schema, string table, List<string> cols, CancellationToken ct)
    {
        var full = $"[{schema}].[{table}]";
        await using (var del = new SqlCommand($"DELETE FROM {full};", dst) { CommandTimeout = CommandTimeoutSeconds })
            await del.ExecuteNonQueryAsync(ct);

        var colList = string.Join(", ", cols.Select(c => $"[{c}]"));
        await using var read = new SqlCommand($"SELECT {colList} FROM {full};", src) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await read.ExecuteReaderAsync(ct);
        using var bulk = NewBulk(dst, full);
        foreach (var c in cols) bulk.ColumnMappings.Add(c, c);
        await bulk.WriteToServerAsync(reader, ct);
        return bulk.RowsCopied;
    }

    private static async Task BulkInsertAsync(
        SqlConnection dst, string full, List<string> cols, List<object?[]> rows, CancellationToken ct)
    {
        var dt = new DataTable();
        foreach (var c in cols) dt.Columns.Add(c, typeof(object));
        foreach (var row in rows)
        {
            var dr = dt.NewRow();
            for (var i = 0; i < cols.Count; i++) dr[i] = row[i] ?? DBNull.Value;
            dt.Rows.Add(dr);
        }
        using var bulk = NewBulk(dst, full);
        foreach (var c in cols) bulk.ColumnMappings.Add(c, c);
        await bulk.WriteToServerAsync(dt, ct);
    }

    private static SqlBulkCopy NewBulk(SqlConnection dst, string full) => new(dst,
        SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock, null)
    {
        DestinationTableName = full,
        BulkCopyTimeout = CommandTimeoutSeconds,
        BatchSize = 5000,
    };

    /// <summary>SqlBulkCopy with KeepIdentity issues SET IDENTITY_INSERT ON for the target table
    /// and only switches it OFF again when the copy completes. When the copy throws (a unique-key
    /// violation, say) the setting stays ON for the session — and every table shares the one dst
    /// connection, so each later table then fails with "IDENTITY_INSERT is already ON for table X.
    /// Cannot perform SET operation". One bad table used to poison the rest of the run this way.
    /// Errors are swallowed: the table may have no identity column, or the flag was never set.</summary>
    private static async Task ResetIdentityInsertAsync(SqlConnection dst, string full)
    {
        try
        {
            await using var cmd = new SqlCommand($"SET IDENTITY_INSERT {full} OFF;", dst);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Nothing to undo.
        }
    }

    private static async Task ApplyUpdatesAsync(
        SqlConnection dst, string full, List<(string Name, string Type)> typed, List<string> pk,
        List<object?[]> rows, CancellationToken ct)
    {
        var pkSet = new HashSet<string>(pk, StringComparer.OrdinalIgnoreCase);
        var setCols = typed.Where(c => !pkSet.Contains(c.Name)).ToList();
        if (setCols.Count == 0) return; // key-only table — nothing to update

        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < typed.Count; i++) colIndex[typed[i].Name] = i;
        var typeOf = typed.ToDictionary(c => c.Name, c => c.Type, StringComparer.OrdinalIgnoreCase);

        var setClause = string.Join(", ", setCols.Select((c, i) => $"[{c.Name}] = @s{i}"));
        var whereClause = string.Join(" AND ", pk.Select((c, i) => $"[{c}] = @k{i}"));
        var sql = $"UPDATE {full} SET {setClause} WHERE {whereClause};";

        foreach (var row in rows)
        {
            await using var cmd = new SqlCommand(sql, dst) { CommandTimeout = CommandTimeoutSeconds };
            for (var i = 0; i < setCols.Count; i++)
                AddTypedParam(cmd, $"@s{i}", row[colIndex[setCols[i].Name]], setCols[i].Type);
            for (var i = 0; i < pk.Count; i++)
                AddTypedParam(cmd, $"@k{i}", row[colIndex[pk[i]]], typeOf.GetValueOrDefault(pk[i], ""));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>Add a parameter carrying the column's real SQL type. AddWithValue infers the type
    /// from the value, and a null infers nvarchar — which SQL Server refuses to assign to a
    /// varbinary/binary/uniqueidentifier/xml column ("Implicit conversion from data type nvarchar to
    /// varbinary(max) is not allowed"), so nulling out e.g. dbo.GlobeweighUser.Signature failed the
    /// whole table. Naming the type up front makes nulls typed and non-nulls unambiguous.</summary>
    private static void AddTypedParam(SqlCommand cmd, string name, object? value, string sqlTypeName)
    {
        var p = new SqlParameter(name, value ?? DBNull.Value);
        var t = MapSqlDbType(sqlTypeName);
        if (t is not null)
        {
            p.SqlDbType = t.Value;
            // "max" columns: -1 tells ADO.NET this isn't a length-capped parameter.
            if (value is null or DBNull &&
                t is SqlDbType.VarBinary or SqlDbType.VarChar or SqlDbType.NVarChar)
                p.Size = -1;
        }
        cmd.Parameters.Add(p);
    }

    /// <summary>sys.types name → SqlDbType. Returns null for anything unrecognised (alias/CLR
    /// types), leaving ADO.NET to infer as before.</summary>
    private static SqlDbType? MapSqlDbType(string t) => t.ToLowerInvariant() switch
    {
        "char" => SqlDbType.Char,
        "varchar" => SqlDbType.VarChar,
        "nchar" => SqlDbType.NChar,
        "nvarchar" => SqlDbType.NVarChar,
        "text" => SqlDbType.Text,
        "ntext" => SqlDbType.NText,
        "binary" => SqlDbType.Binary,
        "varbinary" => SqlDbType.VarBinary,
        "image" => SqlDbType.Image,
        "bit" => SqlDbType.Bit,
        "tinyint" => SqlDbType.TinyInt,
        "smallint" => SqlDbType.SmallInt,
        "int" => SqlDbType.Int,
        "bigint" => SqlDbType.BigInt,
        "decimal" or "numeric" => SqlDbType.Decimal,
        "money" => SqlDbType.Money,
        "smallmoney" => SqlDbType.SmallMoney,
        "float" => SqlDbType.Float,
        "real" => SqlDbType.Real,
        "date" => SqlDbType.Date,
        "datetime" => SqlDbType.DateTime,
        "datetime2" => SqlDbType.DateTime2,
        "smalldatetime" => SqlDbType.SmallDateTime,
        "datetimeoffset" => SqlDbType.DateTimeOffset,
        "time" => SqlDbType.Time,
        "uniqueidentifier" => SqlDbType.UniqueIdentifier,
        "xml" => SqlDbType.Xml,
        "sql_variant" => SqlDbType.Variant,
        _ => null,
    };

    private static async Task ApplyDeletesAsync(
        SqlConnection dst, string full, List<string> pk, List<object?[]> keys, CancellationToken ct)
    {
        var whereClause = string.Join(" AND ", pk.Select((c, i) => $"[{c}] = @k{i}"));
        var sql = $"DELETE FROM {full} WHERE {whereClause};";
        foreach (var pkv in keys)
        {
            await using var cmd = new SqlCommand(sql, dst) { CommandTimeout = CommandTimeoutSeconds };
            for (var i = 0; i < pk.Count; i++)
                cmd.Parameters.AddWithValue($"@k{i}", pkv[i] ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task SetAllConstraintsAsync(SqlConnection conn, bool enabled, CancellationToken ct)
    {
        // sp_msforeachtable is undocumented but reliable on on-prem SQL Server.
        var sql = enabled
            ? "EXEC sp_msforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';"
            : "EXEC sp_msforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';";
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = CommandTimeoutSeconds };
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
