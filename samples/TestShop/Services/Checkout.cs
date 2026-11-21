using System;
using System.Collections.Generic;
using TestShop.Domain;

namespace TestShop.Services
{
    public enum CheckoutState { Idle, Paying, Paid, Failed }

    public class PaymentGateway
    {
        public bool Charge(decimal amount) { return amount > 0; }
        public void Refund(decimal amount) { }
    }

    public class Checkout
    {
        private readonly PaymentGateway _gateway = new PaymentGateway();
        private readonly OrderService _orders = new OrderService();
        private CheckoutState _state = CheckoutState.Idle;
        private decimal _lastAmount;

        public decimal Run(Customer c, List<OrderLine> lines, string mode)
        {
            var order = _orders.Create(c);
            foreach (var line in lines)
            {
                order.AddLine(line);
            }
            _state = CheckoutState.Paying;
            try
            {
                if (_gateway.Charge(order.Total))
                {
                    _state = CheckoutState.Paid;
                    _lastAmount = order.Total;
                }
                else
                {
                    _state = CheckoutState.Failed;
                    return 0m;
                }
            }
            catch (InvalidOperationException)
            {
                _state = CheckoutState.Failed;
            }
            switch (mode)
            {
                case "vip":
                    order.ApplyDiscount(0.1m);
                    break;
                case "none":
                    break;
                default:
                    order.ApplyDiscount(0m);
                    break;
            }
            return _lastAmount;
        }

        public void Cancel()
        {
            if (_state == CheckoutState.Paid)
            {
                _gateway.Refund(_lastAmount);
                _state = CheckoutState.Idle;
            }
            else if (_state == CheckoutState.Paying)
            {
                _state = CheckoutState.Failed;
            }
        }
    }
}
