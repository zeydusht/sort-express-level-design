// Command line front-end of the shared simulator (Assets/Sortex/Scripts/Core/Simulator.cs).
//
//   mcs -optimize+ -out:Tools/sim/sim.exe Assets/Sortex/Scripts/Core/*.cs Tools/sim/Sim.cs
//   mono Tools/sim/sim.exe <level.json> [runs] [seed]
//
// Prints one JSON line (solvable, win rate, how players lose, time used). SIM_TRACE=1 prints the moves.
using System;
using System.IO;
using Sortex.Core;

static class Sim
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("usage: sim <level.json> [runs] [seed]");
            return 1;
        }
        var level = LevelData.FromJson(File.ReadAllText(args[0]));
        var settings = new SimSettings
        {
            Runs = args.Length > 1 ? int.Parse(args[1]) : 1000,
            Seed = args.Length > 2 ? int.Parse(args[2]) : 1
        };
        var sim = new Simulator();
        if (Environment.GetEnvironmentVariable("SIM_TRACE") == "1") sim.Trace = s => Console.Error.WriteLine(s);
        var report = sim.Run(level, settings);
        Console.WriteLine(report.ToJson());
        return 0;
    }
}
