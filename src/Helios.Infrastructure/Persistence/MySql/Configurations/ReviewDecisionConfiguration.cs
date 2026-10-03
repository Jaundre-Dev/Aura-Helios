using Helios.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

public sealed class ReviewDecisionConfiguration : IEntityTypeConfiguration<ReviewDecision>
{
    public void Configure(EntityTypeBuilder<ReviewDecision> builder)
    {
        builder.ToTable("review_decisions");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Decision).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.CorrectionsJson).HasColumnType("json");
        builder.Property(d => d.Reason).HasMaxLength(500);

        builder.HasIndex(d => new { d.ApiRequestId, d.CreatedAt });
        builder.HasIndex(d => d.WorkspaceId);

        builder.HasOne<ApiRequest>()
            .WithMany()
            .HasForeignKey(d => d.ApiRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
