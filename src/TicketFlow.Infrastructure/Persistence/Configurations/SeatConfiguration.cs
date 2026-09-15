using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketFlow.Domain.Entities;

namespace TicketFlow.Infrastructure.Persistence.Configurations;

public class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("Seats");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Row).IsRequired().HasMaxLength(5);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        // Defense in depth: the domain already refuses to add a duplicate seat
        // (Event.AddSeat), but a unique index means the rule holds even if a
        // future write path bypasses the aggregate (a bulk import script, a
        // manual SQL fix), rather than relying on application code alone.
        builder.HasIndex(s => new { s.EventSessionId, s.Row, s.Number }).IsUnique();
    }
}
