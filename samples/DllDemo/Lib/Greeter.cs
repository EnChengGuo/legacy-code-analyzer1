namespace Lib
{
    public class Greeter
    {
        public string Hello(string name) { return Prefix() + name; }
        private string Prefix() { return "Hi, "; }
    }
}
