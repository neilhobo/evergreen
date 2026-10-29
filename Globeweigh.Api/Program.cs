using System.Text;
using System.Threading.RateLimiting;
using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Auditing;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Machine-specific overrides (connection string etc.) for dev runs on boxes whose
// SQL setup differs from the repo default — e.g. the evergreen server (localhost\NTPRO,
// SQL auth). Gitignored; create it next to appsettings.json on that machine only.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.Services.AddDbContext<GbEvergreenContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("GbEvergreen")));

// Who is changing things in this request, for the audit trail the context writes in SaveChanges.
// Scoped: one per request, filled by UseAuditScope below, read by GbEvergreenContext.
builder.Services.AddScoped<AuditScope>();

// Relay device talker — single shared TCP connection, serialised internally.
builder.Services.AddSingleton<RelayService>();

// Weighbridge door intercom — one long-lived listener on its event stream, shared with the controller.
builder.Services.AddSingleton<IntercomService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IntercomService>());

// ---- Auth (JWT bearer) ----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<JwtTokenService>();

// Outbound email via the account set up in Super Admin > Email Setup.
builder.Services.AddScoped<EmailSender>();
// Delivery notes rendered to PDF by headless Chrome, for the dispatch confirmation's attachments.
// A singleton: it keeps recent renders so a note is not rendered once per address.
builder.Services.AddSingleton<DeliveryNotePdfRenderer>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            // Sessions never time out: once signed in, a user stays signed in until they
            // log out. Tokens are issued without an exp claim (see JwtTokenService), so
            // lifetime validation must be off and an exp claim must not be required.
            ValidateLifetime = false,
            RequireExpirationTime = false
        };
        // Browsers can't set an Authorization header on a WebSocket, so the VNC
        // proxy passes the JWT as ?access_token= — accept it on that path only.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"].ToString();
                if (token.Length > 0 && ctx.HttpContext.Request.Path.Value?.EndsWith("/vnc") == true)
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// CORS: the web UI runs from the Vite dev server (and inside WebView2) on a different
// origin during development, so allow any origin for the experiment.
const string CorsPolicy = "GateWebDev";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Globeweigh-App")));

// Rate limit the login endpoints. Since 2026-09-16 this API is reachable from the public
// internet through a Cloudflare tunnel with no Access policy in front, and the credential
// check is cheap, so an unthrottled /api/auth/*-login is an open brute-force target.
// Partition by the REAL client IP: behind the tunnel every request arrives from localhost,
// so a socket-address partition would lump the whole internet into one bucket and lock out
// the LAN. Trust Cloudflare's CF-Connecting-IP and fall back to the socket for direct LAN
// hits. Someone mistyping a password stays well under the limit; a script gets 429.
const string LoginRateLimitPolicy = "login";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(LoginRateLimitPolicy, httpContext =>
    {
        var clientIp = httpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(clientIp))
            clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 15,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

// Identity stamp on every response. Dev UIs check for it so a foreign API squatting on the
// dev port (another project's dev server took it twice — 5199, then 5299) fails with a
// clear message instead of a confusing stream of 404/403s. OnStarting so it survives
// error responses and short-circuits (redirects, static files) too.
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        ctx.Response.Headers["X-Globeweigh-App"] = "Evergreen";
        return Task.CompletedTask;
    });
    await next();
});

// Swagger available in all environments for now (handy for testing on the LAN).
app.UseSwagger();
app.UseSwaggerUI();

// No HTTPS redirect: LAN/WebView2 clients hit it directly during bring-up.

// The three UIs are Vite builds with relative asset paths (base: "./", so they can also be
// served from a sub-path and from inside the Tauri shell). That means the entry document
// only works at a URL ending in "/": reached as "/admin", the browser resolves
// "./assets/index-*.js" against the site ROOT, 404s, and the boot splash spins forever
// (index.html's watchdog then reloads the same slash-less URL 3x and gives up). So send
// the slash-less form to the canonical one.
//
// This is middleware doing an EXACT path match, deliberately not MapGet: route templates
// treat "/admin" and "/admin/" as the same route, so an endpoint matches the canonical
// form too and redirects it to itself forever. That took the admin UI down in prod on
// 2026-09-15 (ERR_TOO_MANY_REDIRECTS) — endpoint routing wins over the static-file
// middleware here (see the fallback note below), so "/admin/" never reached index.html.
// Running ahead of UseDefaultFiles leaves "/admin/" on the path it already took.
// PathBase keeps this correct under an IIS virtual directory.
var uiRoots = new[] { "/gate-web", "/admin", "/touch" };
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value;
    if (path is not null && uiRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
    {
        ctx.Response.Redirect($"{ctx.Request.PathBase}{path}/{ctx.Request.QueryString}");
        return;
    }
    await next();
});

// Serve the built web UI from wwwroot/gate-web (a UI release is just a file copy).
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // UI releases are a file copy, so clients (esp. the WebView2 kiosk shell) must not
    // serve a stale shell. Content-hashed bundles (…/assets/index-XXXX.js) are immutable —
    // cache them hard. Entry documents (index.html) and version.json change in place on
    // every deploy, so force revalidation; otherwise WinINet heuristically caches them and
    // the screens never pick up new UI.
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        var path = ctx.Context.Request.Path.Value ?? string.Empty;
        if (path.Contains("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            headers["Cache-Control"] = "public, max-age=31536000, immutable";
        }
        else if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                 || path.EndsWith("version.json", StringComparison.OrdinalIgnoreCase))
        {
            headers["Cache-Control"] = "no-cache, must-revalidate";
            headers["Pragma"] = "no-cache";
        }
    }
});

app.UseCors(CorsPolicy);
app.UseAuthentication();
// AFTER UseAuthentication — before it there is no user on the request to attribute changes to.
app.UseAuditScope();
app.UseAuthorization();
// Applies only where [EnableRateLimiting] names a policy — see AuthController.
app.UseRateLimiter();
app.UseWebSockets();
app.MapControllers();
// Embedded-noVNC bridge for the Devices page (see DeviceVncProxy).
app.MapGet("/api/devices/{id:int}/vnc", Globeweigh.Api.Services.DeviceVncProxy.Handle)
    .RequireAuthorization();
// These fallbacks BYPASS the UseStaticFiles options above (endpoint routing matches
// "/touch/" etc. before the static-file middleware), so without their own options the
// entry document goes out with NO Cache-Control — WebView2 then heuristically caches it
// and kiosk screens keep loading a stale UI (endless "Update available" pill). Same
// no-cache treatment as index.html gets from UseStaticFiles.
var entryDoc = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, must-revalidate";
        ctx.Context.Response.Headers["Pragma"] = "no-cache";
    },
};
app.MapFallbackToFile("gate-web/{*path:nonfile}", "gate-web/index.html", entryDoc);
// Tauri admin UI (Globeweigh.TauriAdmin) deployed to wwwroot/admin via deploy-admin-ui.ps1.
app.MapFallbackToFile("admin/{*path:nonfile}", "admin/index.html", entryDoc);
// Tauri touch UI (Globeweigh.TauriTouch) deployed to wwwroot/touch via deploy-touch-ui.ps1.
app.MapFallbackToFile("touch/{*path:nonfile}", "touch/index.html", entryDoc);

app.Run();
