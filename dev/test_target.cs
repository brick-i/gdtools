// test_target.cs — процесс-жертва для тестов live/mem-*: держит число в памяти,
// берёт его из value.txt раз в секунду (чтобы тест был детерминированным).
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

class TestTarget
{
    static volatile int val = 1;

    static void Main(string[] args)
    {
        string file = args.Length > 0 ? args[0] : "value.txt";
        string last = null;
        Console.WriteLine("pid=" + Process.GetCurrentProcess().Id);
        for (int i = 0; i < 900; i++)
        {
            try
            {
                // применяем только когда файл реально изменился, иначе затрём патч извне
                string s = File.ReadAllText(file).Trim();
                int n;
                if (s != last && int.TryParse(s, out n)) { val = n; last = s; }
            }
            catch { }
            Console.WriteLine("val=" + val);
            Console.Out.Flush();
            Thread.Sleep(1000);
        }
    }
}
