using System.Collections.Generic;

namespace TestShop.Domain
{
    /// <summary>訂單。</summary>
    public class Order
    {
        private readonly List<OrderLine> _lines = new List<OrderLine>();
        public Customer Owner { get; set; }
        public decimal Total { get; private set; }

        public void AddLine(OrderLine line)
        {
            _lines.Add(line);
            Total += line.Price * line.Quantity;
        }

        public decimal ApplyDiscount(decimal rate)
        {
            if (rate < 0 || rate > 1) return Total;
            Total = Total * (1 - rate);
            return Total;
        }
    }

    public class OrderLine
    {
        public string Sku { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    public class Customer
    {
        public string Name { get; set; }
        public virtual decimal DiscountRate() { return 0m; }
    }

    public class VipCustomer : Customer
    {
        public override decimal DiscountRate() { return 0.1m; }

        public class Badge { public string Level { get; set; } }
    }
}
