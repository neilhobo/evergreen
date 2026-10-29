using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Data;

public partial class GbEvergreenContext : DbContext
{
    public GbEvergreenContext(DbContextOptions<GbEvergreenContext> options)
        : base(options)
    {
    }

    public virtual DbSet<GlobeweighUser> GlobeweighUsers { get; set; }

    public virtual DbSet<Setting> Settings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GlobeweighUser>(entity =>
        {
            entity.ToTable("GlobeweighUser");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DateCreated)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.DateDeleted).HasColumnType("datetime");
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.Passcode).HasMaxLength(10);
            entity.Property(e => e.Password).HasMaxLength(50);
            entity.Property(e => e.Surname).HasMaxLength(100);
            entity.Property(e => e.Username).HasMaxLength(100);
        });

        modelBuilder.Entity<Setting>(entity =>
        {
            entity.ToTable("Setting");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BulkDensity).HasColumnType("decimal(9,2)");
            entity.Property(e => e.GateRelayIpAddress).HasMaxLength(50);
            entity.Property(e => e.InvoiceHeaderText).HasMaxLength(50);
            entity.Property(e => e.WeighbridgeMoxaIpAddress).HasMaxLength(50);
            entity.Property(e => e.SmtpHost).HasMaxLength(200);
            entity.Property(e => e.SmtpSecurity).HasMaxLength(20);
            entity.Property(e => e.SmtpUsername).HasMaxLength(200);
            entity.Property(e => e.SmtpPassword).HasMaxLength(1000);
            entity.Property(e => e.EmailFromAddress).HasMaxLength(200);
            entity.Property(e => e.EmailFromName).HasMaxLength(100);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
