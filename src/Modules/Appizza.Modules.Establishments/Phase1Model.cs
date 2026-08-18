using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Establishments;

#pragma warning disable CA1725 // Compact mapping declarations use a conventional short builder name.

public sealed class Establishment : IVersionedEntity
{
    public Guid Id { get; set; }
    public string PublicCode { get; set; } = null!;
    public string? LegalName { get; set; }
    public string TradeName { get; set; } = null!;
    public string? TaxIdentifier { get; set; }
    public string Timezone { get; set; } = "America/Sao_Paulo";
    public string CurrencyCode { get; set; } = "BRL";
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public long Version { get; set; }
}

public sealed class Address
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public string Street { get; set; } = null!;
    public string? Number { get; set; }
    public string? Complement { get; set; }
    public string? District { get; set; }
    public string City { get; set; } = null!;
    public string State { get; set; } = null!;
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "BR";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BusinessHour
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public short DayOfWeek { get; set; }
    public TimeOnly OpeningTime { get; set; }
    public TimeOnly ClosingTime { get; set; }
    public bool Active { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class EstablishmentSetting
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public string SettingKey { get; set; } = null!;
    public string? SettingValue { get; set; }
    public string ValueType { get; set; } = null!;
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class Phase1SettingKeys
{
    public const string MaximumTableDevices = "devices.max_active_table_devices_per_table";
    public const string SessionOpeningMode = "session.opening_mode";
    public const string TableReleaseMode = "table.release_mode";
    public const string CpfRetentionDays = "privacy.cpf_retention_days";
    public const string DeliveryAutoConfirmationEnabled = "delivery.auto_confirmation_enabled";
    public const string DeliveryAutoConfirmationMinutes = "delivery.auto_confirmation_minutes";
    public const string DeliveryAutoContestationWindowMinutes = "delivery.auto_contestation_window_minutes";
}

public sealed class EstablishmentConfiguration : IEntityTypeConfiguration<Establishment>
{
    public void Configure(EntityTypeBuilder<Establishment> b)
    {
        b.ToTable("establishment", "establishments", t => t.HasCheckConstraint("ck_establishment_status", "status in ('active','blocked','inactive')"));
        b.HasKey(x => x.Id); b.Property(x => x.PublicCode).HasMaxLength(80); b.Property(x => x.TradeName).HasMaxLength(200);
        b.Property(x => x.LegalName).HasMaxLength(200); b.Property(x => x.TaxIdentifier).HasMaxLength(30); b.Property(x => x.Timezone).HasMaxLength(80);
        b.Property(x => x.CurrencyCode).HasMaxLength(3); b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.PublicCode).IsUnique(); b.HasIndex(x => x.TaxIdentifier).IsUnique().HasFilter("tax_identifier is not null"); b.HasIndex(x => x.Status);
    }
}

public sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> b)
    {
        b.ToTable("address", "establishments"); b.HasKey(x => x.Id); b.Property(x => x.Street).HasMaxLength(200); b.Property(x => x.Number).HasMaxLength(40);
        b.Property(x => x.Complement).HasMaxLength(120); b.Property(x => x.District).HasMaxLength(120); b.Property(x => x.City).HasMaxLength(120);
        b.Property(x => x.State).HasMaxLength(80); b.Property(x => x.PostalCode).HasMaxLength(20); b.Property(x => x.CountryCode).HasMaxLength(2); b.HasIndex(x => x.EstablishmentId);
    }
}

public sealed class BusinessHourConfiguration : IEntityTypeConfiguration<BusinessHour>
{
    public void Configure(EntityTypeBuilder<BusinessHour> b)
    {
        b.ToTable("business_hour", "establishments", t => { t.HasCheckConstraint("ck_business_hour_day", "day_of_week between 0 and 6"); t.HasCheckConstraint("ck_business_hour_range", "opening_time <> closing_time"); });
        b.HasKey(x => x.Id); b.HasIndex(x => new { x.EstablishmentId, x.DayOfWeek, x.DisplayOrder });
    }
}

public sealed class EstablishmentSettingConfiguration : IEntityTypeConfiguration<EstablishmentSetting>
{
    public void Configure(EntityTypeBuilder<EstablishmentSetting> b)
    {
        b.ToTable("setting", "establishments", t => t.HasCheckConstraint("ck_setting_value_type", "value_type in ('string','integer','boolean')")); b.HasKey(x => x.Id);
        b.Property(x => x.SettingKey).HasMaxLength(160); b.Property(x => x.ValueType).HasMaxLength(30); b.HasIndex(x => new { x.EstablishmentId, x.SettingKey }).IsUnique();
    }
}
