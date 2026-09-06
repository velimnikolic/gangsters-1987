using System;
using RoadDemo;
using UnityEngine;

static class Program
{
    static int failures, scenarios;

    static void Check(bool condition, string label)
    {
        if (condition) return;
        failures++;
        Console.WriteLine("FAIL: " + label);
    }

    static DemoSky Sky(float hour = 10f)
    {
        var sky = new DemoSky();
        sky.clock.Hour = hour;
        sky.Frame(0f);
        return sky;
    }

    static void Advance(DemoSky sky, int frames, float dt = 0.125f)
    {
        for (int i = 0; i < frames; i++) sky.Frame(dt);
    }

    static bool Near(Quaternion a, Quaternion b) =>
        MathF.Abs(System.Numerics.Quaternion.Dot(a.Value, b.Value)) > 0.999999f;

    static void Hold(DemoSky sky, int frames, string label, float dt = 0.125f)
    {
        var pose = sky.sun.transform.rotation;
        int writes = sky.sun.transform.Writes;
        Advance(sky, frames, dt);
        Check(pose.Equals(sky.sun.transform.rotation), label + " keeps exact pose");
        Check(writes == sky.sun.transform.Writes, label + " does not rewrite transform");
    }

    static int Main()
    {
        // A stopped camera should see a real hold after each brief transition,
        // through multiple cycles and all five clock speed buttons.
        foreach (int speed in new[] { 1, 2, 4, 8, 16 })
        {
            scenarios++;
            var sky = Sky();
            float dt = speed / 128f;
            int cycle = 30 * 128 / speed;
            Hold(sky, cycle - 1, $"{speed}x initial hold", dt);
            for (int sample = 0; sample < 3; sample++)
            {
                var start = sky.sun.transform.rotation;
                sky.Frame(dt); // sample boundary
                var target = sky.ClockPose();
                Check(!Near(start, target), $"{speed}x advancing clock changes target");
                Advance(sky, 4 * 128 / speed - 1, dt);
                var mid = sky.sun.transform.rotation;
                Check(!mid.Equals(start) && !Near(mid, target), $"{speed}x visible finite transition");
                Advance(sky, 9 * 128 / speed, dt);
                Check(Near(sky.sun.transform.rotation, target), $"{speed}x reaches sample");
                Hold(sky, 17 * 128 / speed - 1, $"{speed}x settled sample {sample}", dt);
            }
        }

        foreach (string freeze in new[] { "pause", "running", "disabled" })
        {
            scenarios++;
            var sky = Sky();
            Advance(sky, 32 * 8);
            var before = sky.sun.transform.rotation;
            sky.clock.Running = freeze != "running";
            sky.clock.isActiveAndEnabled = freeze != "disabled";
            if (freeze != "pause")
            {
                sky.Frame(0.125f);
                Check(Near(sky.sun.transform.rotation, sky.ClockPose()), freeze + " shows exact authored hour");
            }
            Hold(sky, 40, freeze, freeze == "pause" ? 0f : 0.125f);
            sky.clock.Running = sky.clock.isActiveAndEnabled = true;
            sky.Frame(0.125f);
            if (freeze == "pause")
                Check(!before.Equals(sky.sun.transform.rotation), "pause resumes transition");
            else
            {
                Check(Near(sky.sun.transform.rotation, sky.ClockPose()), freeze + " reinitializes from clock");
                Hold(sky, 10, freeze + " resumes with a fresh hold");
            }

            sky.clock.Hour = 17f;
            sky.Frame(0f);
            Check(Near(sky.sun.transform.rotation, sky.ClockPose()), freeze + " paused scrub applies immediately");
            Hold(sky, 10, freeze + " scrub starts fresh hold");
        }

        scenarios++;
        var midnight = Sky(23.75f);
        var initial = midnight.sun.transform.rotation;
        Hold(midnight, 29 * 8, "natural midnight");
        Check(midnight.clock.Hour < 1f && initial.Equals(midnight.sun.transform.rotation),
            "midnight wrap does not look like a manual jump");

        scenarios++;
        var fixedSky = Sky();
        fixedSky.clock = null;
        fixedSky.Frame(0f);
        Hold(fixedSky, 100, "scene without clock");

        scenarios++;
        var capped = Sky();
        capped.sunAnimationSmoothTime = 1000f;
        Advance(capped, 46 * 8);
        Hold(capped, 13 * 8, "long authored transition still leaves a hold");

        scenarios++;
        var coarse = Sky();
        Advance(coarse, 43, 1f);
        Hold(coarse, 16, "low frame rate settles", 1f);

        Console.WriteLine($"{scenarios} scenarios, {failures} failures; sun motion only, no rendered shadow verdict.");
        return failures == 0 ? 0 : 1;
    }
}
