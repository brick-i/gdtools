using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

// Лаунчер Geometry Dash тулзы.
// Без аргументов - меню. С аргументами - пробрасывает их в gd_currency.exe.
static class Launcher
{
    static string Dir;
    static string Tool;

    static void Main(string[] raw)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        Dir = Path.GetDirectoryName(new Uri(AssemblyPath()).LocalPath);
        Tool = Path.Combine(Dir, "gd_currency.exe");

        if (raw.Length > 0) { Run(args: raw); return; }

        Console.WriteLine();
        Console.WriteLine("=======================================================");
        Console.WriteLine("  GEOMETRY DASH - накрутка валюты");
        Console.WriteLine("=======================================================");

        while (true)
        {
            Run("wallet");
            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------------");
            Console.WriteLine("  1) накрутить в запущенной игре   live <стат> <число>");
            Console.WriteLine("  2) кошелёк                       (текущие значения)");
            Console.WriteLine("  3) патч сейва                    set <стат> <число>  (игра ЗАКРЫТА)");
            Console.WriteLine("  4) закрыть игру -> накрутить -> запустить   setrun <стат> <число>");
            Console.WriteLine("  5) справка");
            Console.WriteLine("  0) выход");
            Console.WriteLine();
            Console.WriteLine("  Можно просто писать: orbs 500000    (сокращение для п.1)");
            Console.WriteLine("-------------------------------------------------------");
            Console.Write("> ");
            string line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();
            if (line.Length == 0) continue;

            string low = line.ToLowerInvariant();
            if (low == "0" || low == "q" || low == "выход" || low == "exit") break;

            if (low == "1")
            {
                Console.Write("  что крутим? (например orbs): ");
                string what = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(what)) continue;
                Console.Write("  сколько: ");
                string how = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(how)) continue;
                Run("live", what.Trim(), how.Trim());
            }
            else if (low == "2") { /* wallet уже напечатан сверху */ }
            else if (low == "3")
            {
                Console.Write("  какой стат (например orbs): ");
                string what = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(what)) continue;
                Console.Write("  сколько: ");
                string how = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(how)) continue;
                Run("set", "GS_value/" + StatId(what.Trim()), how.Trim());
            }
            else if (low == "4")
            {
                Console.Write("  что крутим? (например orbs): ");
                string what = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(what)) continue;
                Console.Write("  сколько: ");
                string how = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(how)) continue;
                Run("setrun", what.Trim(), how.Trim());
            }
            else if (low == "5") Run("-h");
            else if (low.StartsWith("wallet") || low.StartsWith("-h") || low.StartsWith("help")
                  || low.StartsWith("info") || low.StartsWith("stats")) Run(line);
            else if (low.StartsWith("live ")) Run(line.Split(' '));
            else if (low.StartsWith("setrun ")) Run(line.Split(' '));
            else if (low.StartsWith("nodedump ") || low.StartsWith("statmap ")) Run(line.Split(' '));
            else Run("live", line);          // "orbs 500000" -> live orbs 500000
        }
        Console.WriteLine();
    }

    // "orbs" -> "14". Просто спрашиваем у основной тулзы через find-stats-like вывод.
    static string StatId(string name)
    {
        int dummy;
        string id = Map(name, out dummy);
        return id ?? name;
    }

    static string Map(string name, out int id)
    {
        Dictionary<string, string> a = new Dictionary<string, string>();
        a["orbs"] = "14"; a["diamonds"] = "13"; a["double"] = "29"; a["doublecoins"] = "29";
        a["diamonds2"] = "29"; a["diamonds2nd"] = "29"; a["d2"] = "29"; a["shards"] = "29";
        a["gem2"] = "29"; a["usercoins"] = "12"; a["uc"] = "12"; a["coins2"] = "12";
        a["moons"] = "28"; a["moon"] = "28"; a["stars"] = "6"; a["coins"] = "8";
        a["secretcoins"] = "8"; a["secret"] = "8"; a["attempts"] = "2"; a["practice"] = "2";
        a["jumps"] = "1"; a["levels"] = "3"; a["completed"] = "3"; a["demons"] = "5";
        a["keys"] = "21"; a["totalorbs"] = "22";
        string s = name.Trim().ToLowerInvariant();
        if (a.ContainsKey(s)) { id = int.Parse(a[s]); return a[s]; }
        if (int.TryParse(s, out id)) return s;
        return null;
    }

    static string AssemblyPath()
    {
        return typeof(Launcher).Assembly.Location;
    }

    static void Run(params string[] args)
    {
        Console.WriteLine();
        try
        {
            string all = string.Join(" ", args).Trim();
            if (all.Length == 0) return;
            ProcessStartInfo psi = new ProcessStartInfo(Tool, "\"" + all + "\"");
            psi.WorkingDirectory = Dir;
            psi.UseShellExecute = false;
            Console.WriteLine("> gd_currency " + all);
            Console.WriteLine();
            Process p = Process.Start(psi);
            if (p != null) p.WaitForExit();
        }
        catch (Exception e) { Console.WriteLine("[!] " + e.Message); }
        Console.WriteLine();
        Thread.Sleep(300);
    }
}