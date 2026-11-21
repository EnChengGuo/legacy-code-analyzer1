namespace TestShop.Domain
{
    public class Pricing
    {
        private int _written;          // 只寫不讀
        private int _used;

        public int Score(int a, bool b, int[] xs)
        {
            _used = 1;
            int s = 0;
            if (a > 0 && b)              // if +1, && +1
            {
                for (int i = 0; i < xs.Length; i++)   // for +1(巢狀 2)
                    s += xs[i];
            }
            switch (a)
            {
                case 1: s++; break;      // case +1
                case 2: s--; break;      // case +1
            }
            _written = s;
            return b ? s : -s + _used;   // ?: +1
        }

        private void Orphan() { }        // 私有死碼
        public void Helper(Cycle1 c) { }
    }

    public class Cycle1 { public Cycle2 Other; }
    public class Cycle2 { public Cycle1 Other; }
}
