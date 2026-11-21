using System.Collections.Generic;

namespace TestShop.Domain
{
    public interface IWaferSource { int Count(); }

    public class BaseForm { protected string Title; }

    public class AOI_Form : BaseForm, IWaferSource
    {
        private AOI_CountWafer_Parameters _params = new AOI_CountWafer_Parameters();   // 組合
        private List<Customer> _watchers = new List<Customer>();                       // 聚合
        public Pricing Pricing { get; set; }                                           // 關聯
        public int Count() { return 0; }

        public class AOI_CountWafer_Parameters
        {
            public int WaferCount;
            public double CalculateWaferParam(int n) { return n * 1.5; }
            public void Reset() { WaferCount = 0; }
        }
    }

    public class AOI_Form_Special : AOI_Form { public void Extra() { } }
    public class AOI_Form_Special2 : AOI_Form_Special { }
}
