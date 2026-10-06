using System;

// Unityに依存しない実験条件の検証。VisualSearchTrialGenerator.csと共にコンパイルして実行する。
public static class VisualSearchTrialGeneratorChecks
{
    public static void Main()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var generator = new VisualSearchTrialGenerator(true, seed);
            var replay = new VisualSearchTrialGenerator(true, seed);
            for (int block = 0; block < 4; block++)
            {
                generator.BeginBlock();
                replay.BeginBlock();
                int balance = 0;
                int run = 0;
                bool previous = false;
                for (int trial = 0; trial < 101; trial++)
                {
                    bool present = generator.NextTargetPresent();
                    Check(present == replay.NextTargetPresent(), "同一Seedで条件が一致しない");
                    balance += present ? 1 : -1;
                    Check(Math.Abs(balance) <= 1, "条件数が偏っている");
                    run = trial > 0 && previous == present ? run + 1 : 1;
                    previous = present;
                    Check(run <= 2, "同一条件が3回以上連続している");
                    foreach (int size in new[] { 3, 4, 5, 15, 25, 26 })
                    {
                        VisualSearchTrialGenerator.GetCounts(size, present, generator.Random,
                            out int target, out int cube, out int sphere);
                        VisualSearchTrialGenerator.GetCounts(size, present, replay.Random,
                            out int replayTarget, out int replayCube, out int replaySphere);
                        Check(target == (present ? 1 : 0), "Target数が不正");
                        Check(target + cube + sphere == size, "Set Sizeと生成数が一致しない");
                        Check(Math.Abs(cube - sphere) <= 1, "Distractor数の差が大きい");
                        Check(target == replayTarget && cube == replayCube && sphere == replaySphere,
                            "同一Seedで刺激数が一致しない");
                        Check(generator.Random.NextDouble() == replay.Random.NextDouble(),
                            "配置用乱数の再現性が失われている");
                    }
                }
            }
        }
        bool rejected = false;
        try { VisualSearchTrialGenerator.GetCounts(0, true, new Random(), out _, out _, out _); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "不正な刺激数を拒否しない");
        Console.WriteLine("PASS: 100 seeds, 4 blocks, 101 trials, 6 set sizes; balance, counts and reproducibility.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
