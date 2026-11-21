using TestShop.Domain;

namespace TestShop.Services
{
    public interface IOrderService { Order Create(Customer c); }

    public class OrderService : IOrderService
    {
        public Order Create(Customer c)
        {
            var o = new Order { Owner = c };
            o.ApplyDiscount(c.DiscountRate());
            return o;
        }

        public void Unused() { }
    }
}
