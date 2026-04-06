using MesterX.Domain.Entities.Mall;
using Microsoft.EntityFrameworkCore;

namespace MesterX.Infrastructure.Data;

// ─── Partial extension of existing MesterXDbContext ───────────────────────
// أضف هذا للـ MesterXDbContext الحالي (partial class)
public partial class MesterXDbContext
{
    // ── MallX DbSets ──────────────────────────────────────────────────────
    public DbSet<Mall>                  Malls                  => Set<Mall>();
    public DbSet<MallCustomer>          MallCustomers          => Set<MallCustomer>();
    public DbSet<CustomerAddress>       CustomerAddresses      => Set<CustomerAddress>();
    public DbSet<CustomerRefreshToken>  CustomerRefreshTokens  => Set<CustomerRefreshToken>();
    public DbSet<Cart>                  Carts                  => Set<Cart>();
    public DbSet<CartItem>              CartItems              => Set<CartItem>();
    public DbSet<MallOrder>             MallOrders             => Set<MallOrder>();
    public DbSet<StoreOrder>            StoreOrders            => Set<StoreOrder>();
    public DbSet<StoreOrderItem>        StoreOrderItems        => Set<StoreOrderItem>();
    public DbSet<OrderStatusHistory>    OrderStatusHistory     => Set<OrderStatusHistory>();
}

// ─── Model Builder Extension ──────────────────────────────────────────────
// استدعِ هذا من داخل OnModelCreating الحالي:
//   modelBuilder.ApplyMallXConfiguration();
public static class MallXModelBuilderExtension
{
    public static void ApplyMallXConfiguration(this ModelBuilder mb)
    {
        // ── Mall ──────────────────────────────────────────────────────────
        mb.Entity<Mall>(e =>
        {
            e.ToTable("malls");
            e.HasIndex(m => m.Slug).IsUnique();
        });

        // ── MallCustomer ──────────────────────────────────────────────────
        mb.Entity<MallCustomer>(e =>
        {
            e.ToTable("mall_customers");
            e.HasIndex(c => c.Email).IsUnique();
            e.Property(c => c.Tier)
             .HasConversion<string>();

            e.HasOne(c => c.Mall)
             .WithMany(m => m.Customers)
             .HasForeignKey(c => c.MallId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.Cart)
             .WithOne(cart => cart.Customer)
             .HasForeignKey<Cart>(cart => cart.CustomerId);

            e.HasMany(c => c.Addresses)
             .WithOne(a => a.Customer)
             .HasForeignKey(a => a.CustomerId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(c => c.RefreshTokens)
             .WithOne(t => t.Customer)
             .HasForeignKey(t => t.CustomerId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(c => c.Orders)
             .WithOne(o => o.Customer)
             .HasForeignKey(o => o.CustomerId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── CustomerAddress ───────────────────────────────────────────────
        mb.Entity<CustomerAddress>(e => e.ToTable("customer_addresses"));

        // ── CustomerRefreshToken ──────────────────────────────────────────
        mb.Entity<CustomerRefreshToken>(e => e.ToTable("customer_refresh_tokens"));

        // ── Cart ──────────────────────────────────────────────────────────
        mb.Entity<Cart>(e =>
        {
            e.ToTable("carts");
            e.HasIndex(c => c.CustomerId).IsUnique();

            e.HasMany(c => c.Items)
             .WithOne(i => i.Cart)
             .HasForeignKey(i => i.CartId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── CartItem ──────────────────────────────────────────────────────
        mb.Entity<CartItem>(e =>
        {
            e.ToTable("cart_items");
            e.HasIndex(i => new { i.CartId, i.ProductId }).IsUnique();
        });

        // ── MallOrder ─────────────────────────────────────────────────────
        mb.Entity<MallOrder>(e =>
        {
            e.ToTable("mall_orders");
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.Property(o => o.Status).HasConversion<string>();
            e.Property(o => o.FulfillmentType).HasConversion<string>();
            e.Property(o => o.PaymentMethod).HasConversion<string>();

            e.HasMany(o => o.StoreOrders)
             .WithOne(so => so.MallOrder)
             .HasForeignKey(so => so.MallOrderId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(o => o.StatusHistory)
             .WithOne(h => h.MallOrder)
             .HasForeignKey(h => h.MallOrderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── StoreOrder ────────────────────────────────────────────────────
        mb.Entity<StoreOrder>(e =>
        {
            e.ToTable("store_orders");
            e.Property(so => so.Status).HasConversion<string>();

            e.HasMany(so => so.Items)
             .WithOne(i => i.StoreOrder)
             .HasForeignKey(i => i.StoreOrderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── StoreOrderItem ────────────────────────────────────────────────
        mb.Entity<StoreOrderItem>(e => e.ToTable("store_order_items"));

        // ── OrderStatusHistory ────────────────────────────────────────────
        mb.Entity<OrderStatusHistory>(e => e.ToTable("order_status_history"));

        // ── Extend Tenant (Store fields) ──────────────────────────────────
        mb.Entity<MesterX.Domain.Entities.Core.Tenant>(e =>
        {
            e.Property<Guid?>("MallId");
            e.Property<string>("StoreType").HasMaxLength(20).HasDefaultValue("Retail");
            e.Property<int>("FloorNumber").HasDefaultValue(1);
            e.Property<string?>("StoreQr");
            e.Property<decimal>("Commission").HasPrecision(5, 4).HasDefaultValue(0.05m);
        });
    }
}
