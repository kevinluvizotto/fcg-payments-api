using System;
using Microsoft.EntityFrameworkCore;

namespace FCG.Payments.Api.Models
{
    public class PaymentsDbContext : DbContext
    {
        public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
            : base(options)
        {
        }

        public DbSet<Payment> Payments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Payment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
                entity.Property(e => e.UserId).HasColumnType("nvarchar(50)");
                entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
                entity.Property(e => e.Status).HasColumnType("nvarchar(50)");
                entity.Property(e => e.Date).HasColumnType("datetime");
            });
        }
    }
}