namespace LustHouse.Core
{
    public static class Analytics
    {
        public static void Track(string eventName, params (string key, object value)[] props) { }
    }
}