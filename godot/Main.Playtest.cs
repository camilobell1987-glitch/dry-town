using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// --playtest N: play N weeks the way a player would see them, every live week on screen at top speed
/// with the planner giving the orders, and print how smoothly each week drew. For checking that big
/// cities stay playable over a long game.
/// </summary>
public partial class Main
{
    private async System.Threading.Tasks.Task Playtest(int weeks, string? zoom)
    {
        if (zoom != null) _map.ZoomCentre(float.Parse(zoom));
        var all = new List<double>();
        ulong began = Time.GetTicksMsec();
        for (int week = 0; week < weeks; week++)
        {
            if (!W.Player.Alive) { GD.Print($"PLAYTEST the player's outfit is finished in week {W.Week}"); break; }
            _shell.Execute("auto");
            EndWeek();
            SetSpeed(8);
            var frames = new List<double>();
            ulong last = Time.GetTicksUsec(), started = Time.GetTicksMsec();
            while (_map.Live)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                ulong now = Time.GetTicksUsec();
                frames.Add((now - last) / 1000.0);
                last = now;
                if (Time.GetTicksMsec() - started > 600_000) { GD.Print($"PLAYTEST week {W.Week} stuck after 10 minutes"); GetTree().Quit(1); return; }
            }
            // Let the Sunday report settle before the next Monday.
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            all.AddRange(frames);
            var sorted = frames.OrderBy(f => f).ToList();
            double median = sorted[sorted.Count / 2];
            GD.Print($"PLAYTEST week {week + 1}: {frames.Count} frames, median {median:F1} ms, 99th {sorted[(int)(sorted.Count * 0.99)]:F1} ms, worst {sorted[^1]:F1} ms, " +
                     $"hitches {frames.Count(f => f > median * 2.5)}, memory {OS.GetStaticMemoryUsage() / 1048576.0:F0} MB, objects {Performance.GetMonitor(Performance.Monitor.ObjectCount)}, " +
                     $"turf {W.TurfOf(Me).Count()}, outfits {W.Outfits.Count()}, small gangs {W.SmallGangs.Count()}");
        }
        var s = all.OrderBy(f => f).ToList();
        GD.Print($"PLAYTEST done: {s.Count} frames in {(Time.GetTicksMsec() - began) / 1000.0:F0} s, median {s[s.Count / 2]:F1} ms, 99th {s[(int)(s.Count * 0.99)]:F1} ms, worst {s[^1]:F1} ms");
        GetTree().Quit();
    }
}
