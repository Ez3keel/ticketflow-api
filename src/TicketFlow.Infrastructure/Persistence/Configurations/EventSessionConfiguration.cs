using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketFlow.Domain.Entities;

namespace TicketFlow.Infrastructure.Persistence.Configurations;

public class EventSessionConfiguration : IEntityTypeConfiguration<EventSession>
{
    public void Configure(EntityTypeBuilder<EventSession> builder)
    {
        builder.ToTable("EventSessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.VenueName).IsRequired().HasMaxLength(200);
        builder.Property(s => s.TicketPrice).HasPrecision(10, 2);

        builder.HasMany(s => s.Seats)
            .WithOne()
            .HasForeignKey(seat => seat.EventSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Seats).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
