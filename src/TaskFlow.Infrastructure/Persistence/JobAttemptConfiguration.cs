using TaskFlow.Domain.Jobs;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TaskFlow.Infrastructure.Persistence;

internal sealed class JobAttemptConfiguration : IEntityTypeConfiguration<JobAttempt>
{
    public void Configure(EntityTypeBuilder<JobAttempt> builder)
    {
        builder.ToTable("JobAttempts", table =>
        {
            table.HasCheckConstraint(
                "CK_JobAttempts_Outcome",
                "[Outcome] IS NULL OR [Outcome] BETWEEN 0 AND 2");
        });

        builder.HasKey(attempt => attempt.Id);

        // Job.StartProcessing creates the id. If EF thought the store generated it, a new attempt
        // on an already tracked job would be treated as an existing row and updated instead of inserted.
        builder.Property(attempt => attempt.Id)
            .ValueGeneratedNever();

        builder.Property(attempt => attempt.AttemptNumber)
            .IsRequired();

        builder.Property(attempt => attempt.StartedAt)
            .IsRequired();

        builder.Property(attempt => attempt.WorkerId)
            .HasMaxLength(JobLimits.MaxWorkerIdLength)
            .IsRequired();

        builder.Property(attempt => attempt.Outcome)
            .HasConversion<int>();

        builder.Property(attempt => attempt.ErrorType)
            .HasMaxLength(JobLimits.MaxErrorTypeLength);

        builder.Property(attempt => attempt.ErrorMessage)
            .HasMaxLength(JobLimits.MaxErrorMessageLength);

        builder.Property(attempt => attempt.Duration)
            .HasConversion(
                duration => duration.HasValue ? duration.Value.Ticks : (long?)null,
                ticks => ticks.HasValue ? TimeSpan.FromTicks(ticks.Value) : null);

        builder.HasIndex(attempt => new { attempt.JobId, attempt.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("UX_JobAttempts_JobId_AttemptNumber");
    }
}
