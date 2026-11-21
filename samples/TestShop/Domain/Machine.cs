using System.Collections.Generic;

namespace TestShop.Domain
{
    public interface IItemSource { int Count(); }

    public class BaseForm { protected string Title; }

    public class Machine_Form : BaseForm, IItemSource
    {
        private Machine_CountItem_Parameters _params = new Machine_CountItem_Parameters();   // 組合
        private List<Customer> _watchers = new List<Customer>();                       // 聚合
        public Pricing Pricing { get; set; }                                           // 關聯
        public int Count() { return 0; }

        public class Machine_CountItem_Parameters
        {
            public int ItemCount;
            public double CalculateItemParam(int n) { return n * 1.5; }
            public void Reset() { ItemCount = 0; }
        }
    }

    public class Machine_Form_Special : Machine_Form { public void Extra() { } }
    public class Machine_Form_Special2 : Machine_Form_Special { }
}
