using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Touch device registry. Each physical touch screen resolves its dbo.Device row by the
// machine hostname (reported by the Tauri os plugin); the screen-mode flags on that row
// drive what the TauriTouch app shows on boot. Mirrors the WPF UtilitiesShared.RegisterDevice
// (create-on-miss keyed by DeviceName). Anonymous: a screen must read its own mode BEFORE any
// login (Weigh-In/Out screens never log in), and the Super Admin config is passcode-gated in
// the client — matching the unauthenticated WPF Touch app.
[ApiController]
[AllowAnonymous]
[Route("api/device")]
public class DeviceController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public DeviceController(GbEvergreenContext db) => _db = db;

    // GET /api/device/by-name/{deviceName} — resolve (create on first sight) this screen's row.
    [HttpGet("by-name/{deviceName}")]
    public async Task<ActionResult<DeviceDto>> ByName(string deviceName, CancellationToken ct = default)
    {
        var name = (deviceName ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = "Device name is required." });

        var device = await _db.Set<Device>().FirstOrDefaultAsync(d => d.DeviceName == name, ct);
        if (device == null)
        {
            device = new Device
            {
                DeviceName = name,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                ApplicationName = "TauriTouch",
                CurrentVersion = "",
                VersionOnServer = "",
                DateCreated = DateTime.Now,
                DateOfLastPing = DateTime.Now,
            };
            _db.Add(device);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            device.DateOfLastPing = DateTime.Now;
            // Refresh the caller's IP too — makes "which PC claims this row?" diagnosable
            // (several PCs falling back to the same DEV-TOUCH name shows up right here).
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(ip)) device.IpAddress = ip;
            await _db.SaveChangesAsync(ct);
        }

        return Dto(device);
    }

    // GET /api/device — all devices (for the Super Admin config device picker / browser fallback).
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DeviceDto>>> List(CancellationToken ct = default)
    {
        var rows = await _db.Set<Device>().AsNoTracking()
            .OrderBy(d => d.DeviceName)
            .ToListAsync(ct);
        return Ok(rows.Select(Dto).Select(r => r.Value!));
    }

    // PUT /api/device/{id}/config — Super Admin sets the four screen-mode flags.
    [HttpPut("{id:int}/config")]
    public async Task<ActionResult<DeviceDto>> SetConfig(int id, [FromBody] DeviceConfigInput input, CancellationToken ct = default)
    {
        var device = await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device == null) return NotFound();

        device.IsWeighIn = input.IsWeighIn;
        device.IsWeighOut = input.IsWeighOut;
        device.Factory = input.Factory;
        device.SampleWeight = input.SampleWeight;
        device.Production = input.Production;
        device.ProductionSummary = input.ProductionSummary;
        device.Camera = input.Camera;
        device.ManualStock = input.ManualStock;
        device.Weighscale = input.Weighscale;
        device.Printer = input.Printer;
        device.LoginEnabled = input.LoginEnabled;
        device.RelayCard = input.RelayCard;
        device.ProductionAlarm = input.ProductionAlarm;
        device.HaulierOwnScreen = input.HaulierOwnScreen;
        device.DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim();
        await _db.SaveChangesAsync(ct);
        return Dto(device);
    }

    /// <summary>Per-PC hardware setup (weighscale / relay / printer), saved from the setup
    /// screens. Full replace of the three fields — the client always sends all of them.</summary>
    public record HardwareInput(string? WeighscaleConfig, string? RelayConfig, string? PrinterName);

    // PUT /api/device/{id}/hardware
    [HttpPut("{id:int}/hardware")]
    public async Task<ActionResult<DeviceDto>> SetHardware(int id, [FromBody] HardwareInput input, CancellationToken ct = default)
    {
        var device = await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device == null) return NotFound();

        device.WeighscaleConfig = string.IsNullOrWhiteSpace(input.WeighscaleConfig) ? null : input.WeighscaleConfig;
        device.RelayConfig = string.IsNullOrWhiteSpace(input.RelayConfig) ? null : input.RelayConfig;
        device.PrinterName = string.IsNullOrWhiteSpace(input.PrinterName) ? null : input.PrinterName.Trim();
        await _db.SaveChangesAsync(ct);
        return Dto(device);
    }

    private static ActionResult<DeviceDto> Dto(Device d) => new DeviceDto
    {
        Id = d.Id,
        DeviceName = d.DeviceName,
        IsWeighIn = d.IsWeighIn,
        IsWeighOut = d.IsWeighOut,
        Factory = d.Factory,
        SampleWeight = d.SampleWeight,
        Production = d.Production,
        ProductionSummary = d.ProductionSummary,
        Camera = d.Camera,
        ManualStock = d.ManualStock,
        Weighscale = d.Weighscale,
        Printer = d.Printer,
        LoginEnabled = d.LoginEnabled,
        RelayCard = d.RelayCard,
        ProductionAlarm = d.ProductionAlarm,
        HaulierOwnScreen = d.HaulierOwnScreen,
        DisplayName = d.DisplayName,
        WeighscaleConfig = d.WeighscaleConfig,
        RelayConfig = d.RelayConfig,
        PrinterName = d.PrinterName,
    };
}

public class DeviceDto
{
    public int Id { get; set; }
    public string? DeviceName { get; set; }
    public bool IsWeighIn { get; set; }
    public bool IsWeighOut { get; set; }
    public bool Factory { get; set; }
    public bool SampleWeight { get; set; }
    public bool Production { get; set; }
    public bool ProductionSummary { get; set; }
    public bool Camera { get; set; }
    public bool ManualStock { get; set; }
    public bool Weighscale { get; set; }
    public bool Printer { get; set; }
    public bool LoginEnabled { get; set; }
    public bool RelayCard { get; set; }
    public bool ProductionAlarm { get; set; }
    public bool HaulierOwnScreen { get; set; }
    public string? DisplayName { get; set; }
    public string? WeighscaleConfig { get; set; }
    public string? RelayConfig { get; set; }
    public string? PrinterName { get; set; }
}

public class DeviceConfigInput
{
    public bool IsWeighIn { get; set; }
    public bool IsWeighOut { get; set; }
    public bool Factory { get; set; }
    public bool SampleWeight { get; set; }
    public bool Production { get; set; }
    public bool ProductionSummary { get; set; }
    public bool Camera { get; set; }
    public bool ManualStock { get; set; }
    public bool Weighscale { get; set; }
    public bool Printer { get; set; }
    public bool LoginEnabled { get; set; } = true;
    public bool RelayCard { get; set; }
    public bool ProductionAlarm { get; set; }
    public bool HaulierOwnScreen { get; set; }
    public string? DisplayName { get; set; }
}
