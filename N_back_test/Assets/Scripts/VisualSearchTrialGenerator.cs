using System;

// Unity全体の乱数状態を変更せず、条件と配置に同じ実験Seedを使用する。
public sealed class VisualSearchTrialGenerator
{
    public int Seed { get; }
    public Random Random { get; }
    private bool secondInPair;
    private bool firstPresent;

    public VisualSearchTrialGenerator(bool useRandomSeed, int randomSeed)
    {
        Seed = useRandomSeed ? randomSeed : Guid.NewGuid().GetHashCode();
        Random = new Random(Seed);
    }

    public void BeginBlock()
    {
        secondInPair = false;
    }

    // 2試行ごとにPresent/Absentを1回ずつ、順序だけランダムにする。
    // 途中終了時の条件数の差は最大1、同一条件の連続は最大2回。
    public bool NextTargetPresent()
    {
        if (!secondInPair)
            firstPresent = Random.Next(2) == 0;
        bool result = secondInPair ? !firstPresent : firstPresent;
        secondInPair = !secondInPair;
        return result;
    }

    // 奇数のDistractor数では、多い方の種類をランダムに決定する。
    public static void GetCounts(int setSize, bool targetPresent, Random random,
        out int redSpheres, out int redCubes, out int blueSpheres)
    {
        if (setSize < 3) throw new ArgumentOutOfRangeException(nameof(setSize));
        redSpheres = targetPresent ? 1 : 0;
        int distractors = setSize - redSpheres;
        redCubes = distractors / 2;
        if (distractors % 2 != 0) redCubes += random.Next(2);
        blueSpheres = distractors - redCubes;
    }
}
