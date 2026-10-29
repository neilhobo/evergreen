using System;
using System.Collections.Generic;

namespace Globeweigh.Api.Data;

public partial class GlobeweighUser
{
    public int Id { get; set; }

    public int UserTypeId { get; set; }

    public string? FirstName { get; set; }

    public string? Surname { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? Passcode { get; set; }

    public int RoleId { get; set; }

    public bool Disabled { get; set; }

    public bool CostingAccess { get; set; }

    // Sees the Oil Tanks page — set in Super Admin > Oil Tanks.
    // See Migrations/AddGlobeweighUserOilTanksAccess.sql and OilTanksController.
    public bool OilTanksAccess { get; set; }

    public byte[]? Signature { get; set; }

    public byte[]? SignatureImage { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime? DateDeleted { get; set; }
}
