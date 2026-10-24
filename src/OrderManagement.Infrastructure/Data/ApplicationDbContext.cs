using Microsoft.EntityFrameworkCore;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Data

{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) 
            : base(options)
        {
        }

        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var product = modelBuilder.Entity<Product>();

            product.ToTable("Products");

            product.HasKey(p => p.Id);

            product.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(200);

            product.Property(p => p.Description)
                .IsRequired()
                .HasMaxLength(2000);

            product.Property(p => p.Price)
                .HasPrecision(18, 2);

            product.Property(p => p.CreatedAt)
                .IsRequired();

        }
    }
}
