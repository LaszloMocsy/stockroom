using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockroom.Data.Settings;

namespace Stockroom.Data.Configurations;

internal sealed class StoredSettingConfiguration : IEntityTypeConfiguration<StoredSetting>
{
    public void Configure(EntityTypeBuilder<StoredSetting> builder)
    {
        // SettingsStore writes this table with SQL; keep the names in step.
        builder.ToTable("settings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Value).HasColumnType("jsonb");
    }
}
