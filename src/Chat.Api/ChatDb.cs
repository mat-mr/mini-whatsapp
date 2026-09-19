using Chat.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chat.Api;

public class ChatDb : DbContext
{
    public ChatDb(DbContextOptions<ChatDb> options) : base(options)
    {
    }

    public DbSet<Message> Messages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Message>()
            .HasIndex(m => new { m.From, m.To, m.SentAt });
    }
}
