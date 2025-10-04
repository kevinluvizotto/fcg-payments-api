using Microsoft.EntityFrameworkCore;

namespace FCG.Payments.Api
{
    public class PaymentsDbContext : DbContext
    {
        public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
            : base(options)
        {
        }

        public DbSet<Payment> Payments { get; set; }
    }

    public class Payment
    {
        public Guid Id { get; set; }
        public required string UserId { get; set; } // Adicionado 'required'
        public decimal Amount { get; set; }
        public required string Status { get; set; } // Adicionado 'required'
        public DateTime Date { get; set; }
    }
}