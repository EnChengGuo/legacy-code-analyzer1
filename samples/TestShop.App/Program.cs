using System;
using TestShop.Domain;
using TestShop.Services;

namespace TestShop.App
{
    static class Program
    {
        static void Main()
        {
            IOrderService svc = new OrderService();
            var order = svc.Create(new VipCustomer { Name = "A" });
            Console.WriteLine(order.Total);
        }
    }
}
