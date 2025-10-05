using System;

namespace FCG.Payments.Api.Models
{
#nullable enable
    public class Payment
    {
        public Guid Id { get; set; }
        public string? UserId { get; set; }
        public decimal Amount { get; set; }
        public string? Status { get; set; }
        public DateTime Date { get; set; }
    }
}