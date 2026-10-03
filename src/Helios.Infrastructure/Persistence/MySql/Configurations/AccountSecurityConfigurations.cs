using Helios.Domain.Identity;
using Helios.Infrastructure.Persistence.MySql.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

public sealed class AccountTokenConfiguration : IEntityTypeConfiguration<AccountToken>
{
    public void Configure(EntityTypeBuilder<AccountToken> builder)
    {
        builder.ToTable("account_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => new { t.UserId, t.Purpose, t.TokenHash });
        builder.HasOne<HeliosUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UserAuthenticatorConfiguration : IEntityTypeConfiguration<UserAuthenticator>
{
    public void Configure(EntityTypeBuilder<UserAuthenticator> builder)
    {
        builder.ToTable("user_authenticators");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.SecretEnvelope).HasMaxLength(256).IsRequired();
        builder.Property(a => a.KeyId).HasMaxLength(64).IsRequired();
        builder.HasIndex(a => a.UserId).IsUnique();
        builder.HasOne<HeliosUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> builder)
    {
        builder.ToTable("recovery_codes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.CodeHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(c => new { c.UserId, c.Scope, c.CodeHash });
        builder.HasOne<HeliosUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
