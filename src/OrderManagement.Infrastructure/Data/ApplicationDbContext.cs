using Microsoft.EntityFrameworkCore;
using OrderManagement.Domain.Entities;
using System.Data;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using OrderManagement.Infrastructure.Identity;

namespace OrderManagement.Infrastructure.Data

{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) 
            : base(options)
        {
        }

        public DbSet<Product> Products => Set<Product>();

        public DbSet<User> DomainUsers => Set<User>();

        public DbSet<Order> Orders => Set<Order>();

        public DbSet<OrderItem> OrderItems => Set<OrderItem>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);


            var account = modelBuilder.Entity<ApplicationUser>();

            account.HasOne(a => a.User)
                .WithOne()
                .HasForeignKey<ApplicationUser>(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            account.HasIndex(account => account.UserId)
                .IsUnique();

            account.HasIndex(a => a.NormalizedEmail)
                .IsUnique();

            // Products
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



            // Users
            var user = modelBuilder.Entity<User>();

            user.ToTable("Users");

            user.HasKey(u => u.Id);

            user.Property(u => u.Name)
                .IsRequired()
                .HasMaxLength(200);

            user.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(320);

            user.HasIndex(u => u.Email)
                .IsUnique();

            user.Property(u => u.CreatedAt)
                .IsRequired();


            // Orders
            var order = modelBuilder.Entity<Order>();

            order.ToTable("Orders");

            order.HasKey(o => o.Id);

            order.Property(o => o.TotalAmount)
                .HasPrecision(18, 2);

            order.Property(o => o.CreatedAt)
                .IsRequired();

            order.HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);


            // Order items
            var orderItem = modelBuilder.Entity<OrderItem>();

            orderItem.ToTable("OrderItems");

            orderItem.HasKey(i => i.Id);

            orderItem.Property(i => i.Quantity)
                .IsRequired();

            orderItem.Property(i => i.UnitPrice)
                .HasPrecision(18, 2);

            orderItem.Ignore(i => i.LineTotal);

            orderItem.HasOne(i => i.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            orderItem.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            orderItem.HasIndex(i => new { i.OrderId, i.ProductId })
                .IsUnique();
                }
            }
}
