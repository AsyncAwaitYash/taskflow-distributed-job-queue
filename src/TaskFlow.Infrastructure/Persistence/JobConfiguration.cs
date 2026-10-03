using TaskFlow.Domain.Jobs;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TaskFlow.Infrastructure.Persistence;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("Jobs", table =>
        {
            table.HasCheckConstraint("CK_Jobs_MaxAttempts", "[MaxAttempts] >= 1");
            table.HasCheckConstraint("CK_Jobs_Status", "[Status] BETWEEN 0 AND 6");
        });

        builder.HasKey(job => job.Id);

        builder.Property(job => job.Id)
            .ValueGeneratedNever();

        builder.Property(job => job.Type)
            .HasMaxLength(JobLimits.MaxTypeLength)
            .IsRequired();

        builder.Property(job => job.Payload)
            .IsRequired();

        builder.Property(job => job.MaxAttempts)
            .IsRequired();

        builder.Property(job => job.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(job => job.CorrelationId)
            .HasMaxLength(JobLimits.MaxCorrelationIdLength)
            .IsRequired();

        builder.Property(job => job.LastError)
            .HasMaxLength(JobLimits.MaxErrorMessageLength);

        builder.Property(job => job.CreatedAt).IsRequired();
        builder.Property(job => job.UpdatedAt).IsRequired();

        builder.HasMany(job => job.Attempts)
            .WithOne()
            .HasForeignKey(attempt => attempt.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(job => job.Attempts)
            .HasField("_attempts")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(job => job.CreatedAt)
            .HasDatabaseName("IX_Jobs_CreatedAt");

        builder.HasIndex(job => new { job.Type, job.CreatedAt })
            .HasDatabaseName("IX_Jobs_Type_CreatedAt");

        builder.HasIndex(job => new { job.Status, job.CreatedAt })
            .HasDatabaseName("IX_Jobs_Status_CreatedAt");
    }
}
