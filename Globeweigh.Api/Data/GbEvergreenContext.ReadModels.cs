using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Data;

// Read-model wiring for the admin UI. Each admin grid is backed by a keyless
// read-model (a vwXxx database view or a table read read-only) with its own
// IEntityTypeConfiguration in Data/ReadModels/, applied here in one sweep so a
// new module is purely additive — no edits to this file or the context.
public partial class GbEvergreenContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GbEvergreenContext).Assembly);
    }
}
