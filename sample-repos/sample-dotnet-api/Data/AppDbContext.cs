using Microsoft.EntityFrameworkCore;
using SampleApi.Models;

namespace SampleApi.Data;

/// <summary>
/// Entity Framework database context for the application
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
        });

        // Product configuration
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Price).HasPrecision(18, 2);
        });

        // Seed data
        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = 1,
                Username = "admin",
                Email = "admin@example.com",
                PasswordHash = "hashed_password",
                FirstName = "Admin",
                LastName = "User",
                Role = UserRole.Admin
            }
        );

        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, Name = "Widget", Description = "A useful widget", Price = 9.99m, StockQuantity = 100, Category = "Gadgets" },
            new Product { Id = 2, Name = "Gadget", Description = "An amazing gadget", Price = 19.99m, StockQuantity = 50, Category = "Gadgets" }
        );
    }
}
