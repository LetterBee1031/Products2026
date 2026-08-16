using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class NBackPatternData
{
    public string patternId;
    public int nBack;
    public int seed;

    public int totalTrials;
    public int initializationTrials;
    public int evaluationTrials;

    public int targetCount;
    public float targetRate;

    public string[] stimuli;
    public bool[] isTarget;
}

public static class NBackPatternGenerator
{
    /// <summary>
    /// N-back用の刺激系列を生成する。
    ///
    /// デフォルト設定:
    /// - 全60試行
    /// - 最初の3試行はInitialization
    /// - 評価対象57試行
    /// - Target率 1/3
    /// - Target数19
    /// - Target連続禁止
    /// - 最大Non-target連続数 6
    /// - lureを原則除外
    /// </summary>
    public static NBackPatternData Generate(
        int nBack,
        int seed,
        string stimulusPool = "ABCDEFGH",
        char zeroBackTarget = 'X',
        int totalTrials = 60,
        int initializationTrials = 3,
        float targetRate = 1f / 3f,
        int maxNonTargetRun = 6,
        int maxFrequencyDifference = 2,
        int maxAttempts = 10000)
    {
        ValidateParameters(
            nBack,
            stimulusPool,
            zeroBackTarget,
            totalTrials,
            initializationTrials);

        int evaluationTrials = totalTrials - initializationTrials;

        int targetCount =
            Mathf.RoundToInt(evaluationTrials * targetRate);

        var rng = new System.Random(seed);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // 1. Target位置を決定
            bool[] targetMask = GenerateTargetMask(
                totalTrials,
                initializationTrials,
                targetCount,
                maxNonTargetRun,
                rng);

            if (targetMask == null)
                continue;

            // 2. 文字系列を生成
            char[] sequence = GenerateStimulusSequence(
                nBack,
                stimulusPool,
                zeroBackTarget,
                totalTrials,
                initializationTrials,
                targetMask,
                rng);

            if (sequence == null)
                continue;

            // 3. Targetが意図した位置と一致するか確認
            if (!ValidateTargets(
                    sequence,
                    targetMask,
                    nBack,
                    zeroBackTarget,
                    initializationTrials))
            {
                continue;
            }

            // 4. lure確認
            if (!ValidateLures(
                    sequence,
                    targetMask,
                    nBack,
                    initializationTrials))
            {
                continue;
            }

            // 5. 文字頻度確認
            if (!ValidateFrequency(
                    sequence,
                    stimulusPool,
                    nBack,
                    maxFrequencyDifference))
            {
                continue;
            }

            return new NBackPatternData
            {
                nBack = nBack,
                seed = seed,

                totalTrials = totalTrials,
                initializationTrials = initializationTrials,
                evaluationTrials = evaluationTrials,

                targetCount = targetCount,
                targetRate =
                    (float)targetCount / evaluationTrials,

                stimuli = sequence
                    .Select(c => c.ToString())
                    .ToArray(),

                isTarget = targetMask
            };
        }

        throw new Exception(
            $"N-back pattern generation failed. " +
            $"nBack={nBack}, seed={seed}");
    }

    // =========================================================
    // Target位置生成
    // =========================================================

    private static bool[] GenerateTargetMask(
        int totalTrials,
        int initializationTrials,
        int targetCount,
        int maxNonTargetRun,
        System.Random rng)
    {
        int evaluationTrials =
            totalTrials - initializationTrials;

        const int segmentCount = 3;

        for (int attempt = 0; attempt < 1000; attempt++)
        {
            bool[] mask = new bool[totalTrials];

            // Target数を3区間へなるべく均等に割り振る
            int baseCount = targetCount / segmentCount;
            int remainder = targetCount % segmentCount;

            int[] quotas = new int[segmentCount];

            for (int i = 0; i < segmentCount; i++)
                quotas[i] = baseCount;

            // 19 Targetの場合 6,6,7 になる。
            // 7個になる区間はランダムにする。
            List<int> segmentOrder =
                Enumerable.Range(0, segmentCount).ToList();

            Shuffle(segmentOrder, rng);

            for (int i = 0; i < remainder; i++)
                quotas[segmentOrder[i]]++;

            bool failed = false;

            for (int segment = 0;
                 segment < segmentCount;
                 segment++)
            {
                int start =
                    initializationTrials +
                    evaluationTrials * segment
                    / segmentCount;

                int end =
                    initializationTrials +
                    evaluationTrials * (segment + 1)
                    / segmentCount;

                List<int> candidates =
                    Enumerable.Range(start, end - start)
                        .ToList();

                Shuffle(candidates, rng);

                int selected = 0;

                foreach (int position in candidates)
                {
                    if (selected >= quotas[segment])
                        break;

                    // Target連続禁止
                    if (position > initializationTrials &&
                        mask[position - 1])
                    {
                        continue;
                    }

                    if (position + 1 < totalTrials &&
                        mask[position + 1])
                    {
                        continue;
                    }

                    mask[position] = true;
                    selected++;
                }

                if (selected != quotas[segment])
                {
                    failed = true;
                    break;
                }
            }

            if (failed)
                continue;

            if (CountTargets(mask) != targetCount)
                continue;

            if (HasConsecutiveTargets(
                    mask,
                    initializationTrials))
            {
                continue;
            }

            if (GetMaximumNonTargetRun(
                    mask,
                    initializationTrials) >
                maxNonTargetRun)
            {
                continue;
            }

            return mask;
        }

        return null;
    }

    // =========================================================
    // 刺激文字生成
    // =========================================================

    private static char[] GenerateStimulusSequence(
        int nBack,
        string stimulusPool,
        char zeroBackTarget,
        int totalTrials,
        int initializationTrials,
        bool[] targetMask,
        System.Random rng)
    {
        char[] sequence = new char[totalTrials];

        Dictionary<char, int> counts =
            stimulusPool
                .Distinct()
                .ToDictionary(c => c, c => 0);

        // -------------------------------
        // Initialization trials
        // -------------------------------

        for (int t = 0;
             t < initializationTrials;
             t++)
        {
            List<char> candidates =
                stimulusPool.Distinct().ToList();

            // 初期化区間でも極端な即時反復を避ける
            if (t > 0 && candidates.Count > 1)
                candidates.Remove(sequence[t - 1]);

            char selected =
                ChooseBalancedCharacter(
                    candidates,
                    counts,
                    rng);

            sequence[t] = selected;
            counts[selected]++;
        }

        // -------------------------------
        // Evaluation trials
        // -------------------------------

        for (int t = initializationTrials;
             t < totalTrials;
             t++)
        {
            bool isTarget = targetMask[t];

            // =================================
            // 0-back
            // =================================
            if (nBack == 0)
            {
                if (isTarget)
                {
                    sequence[t] = zeroBackTarget;
                }
                else
                {
                    List<char> candidates =
                        stimulusPool
                            .Distinct()
                            .Where(c => c != zeroBackTarget)
                            .ToList();

                    // 不要な即時反復を抑える
                    if (t > 0 &&
                        candidates.Count > 1)
                    {
                        candidates.Remove(sequence[t - 1]);
                    }

                    if (candidates.Count == 0)
                        return null;

                    char selected =
                        ChooseBalancedCharacter(
                            candidates,
                            counts,
                            rng);

                    sequence[t] = selected;

                    if (counts.ContainsKey(selected))
                        counts[selected]++;
                }

                continue;
            }

            // =================================
            // 1～3-back
            // =================================

            int referenceIndex = t - nBack;

            if (referenceIndex < 0)
                return null;

            // Target
            if (isTarget)
            {
                char targetCharacter =
                    sequence[referenceIndex];

                sequence[t] = targetCharacter;

                if (counts.ContainsKey(targetCharacter))
                    counts[targetCharacter]++;

                continue;
            }

            // Non-target
            HashSet<char> forbidden =
                new HashSet<char>();

            // 正規のN-back一致は禁止
            forbidden.Add(
                sequence[t - nBack]);

            // -----------------------------
            // lure : n - 1
            // -----------------------------
            if (nBack > 1)
            {
                int lureMinus =
                    t - (nBack - 1);

                if (lureMinus >= 0)
                    forbidden.Add(
                        sequence[lureMinus]);
            }

            // -----------------------------
            // lure : n + 1
            // -----------------------------
            int lurePlus =
                t - (nBack + 1);

            if (lurePlus >= 0)
                forbidden.Add(
                    sequence[lurePlus]);

            // -----------------------------
            // 2/3-backで不要な即時反復を禁止
            // -----------------------------
            if (nBack >= 2 && t > 0)
                forbidden.Add(
                    sequence[t - 1]);

            List<char> allowed =
                stimulusPool
                    .Distinct()
                    .Where(c => !forbidden.Contains(c))
                    .ToList();

            if (allowed.Count == 0)
                return null;

            char chosen =
                ChooseBalancedCharacter(
                    allowed,
                    counts,
                    rng);

            sequence[t] = chosen;
            counts[chosen]++;
        }

        return sequence;
    }

    // =========================================================
    // 文字選択
    // =========================================================

    /// <summary>
    /// 使用回数が最も少ない文字を優先し、
    /// 同率の場合はランダムに選択する。
    /// </summary>
    private static char ChooseBalancedCharacter(
        List<char> candidates,
        Dictionary<char, int> counts,
        System.Random rng)
    {
        if (candidates == null ||
            candidates.Count == 0)
        {
            throw new ArgumentException(
                "No candidate characters.");
        }

        int minCount =
            candidates.Min(c =>
                counts.TryGetValue(c, out int count)
                    ? count
                    : 0);

        List<char> leastUsed =
            candidates
                .Where(c =>
                    (counts.TryGetValue(
                        c,
                        out int count)
                        ? count
                        : 0)
                    == minCount)
                .ToList();

        return leastUsed[
            rng.Next(leastUsed.Count)];
    }

    // =========================================================
    // Target Validation
    // =========================================================

    private static bool ValidateTargets(
        char[] sequence,
        bool[] expectedTargets,
        int nBack,
        char zeroBackTarget,
        int initializationTrials)
    {
        for (int t = initializationTrials;
             t < sequence.Length;
             t++)
        {
            bool actualTarget;

            if (nBack == 0)
            {
                actualTarget =
                    sequence[t] == zeroBackTarget;
            }
            else
            {
                int referenceIndex = t - nBack;

                if (referenceIndex < 0)
                    return false;

                actualTarget =
                    sequence[t]
                    == sequence[referenceIndex];
            }

            if (actualTarget != expectedTargets[t])
                return false;
        }

        return true;
    }

    // =========================================================
    // Lure Validation
    // =========================================================

    private static bool ValidateLures(
        char[] sequence,
        bool[] targetMask,
        int nBack,
        int initializationTrials)
    {
        if (nBack == 0)
            return true;

        for (int t = initializationTrials;
             t < sequence.Length;
             t++)
        {
            // Target自体についてはlure判定しない
            if (targetMask[t])
                continue;

            char current = sequence[t];

            // n-1 lure
            if (nBack > 1)
            {
                int index =
                    t - (nBack - 1);

                if (index >= 0 &&
                    current == sequence[index])
                {
                    return false;
                }
            }

            // n+1 lure
            int plusIndex =
                t - (nBack + 1);

            if (plusIndex >= 0 &&
                current == sequence[plusIndex])
            {
                return false;
            }

            // 2/3-backの即時反復
            if (nBack >= 2 &&
                t > 0 &&
                current == sequence[t - 1])
            {
                return false;
            }
        }

        return true;
    }

    // =========================================================
    // Frequency Validation
    // =========================================================

    private static bool ValidateFrequency(
        char[] sequence,
        string stimulusPool,
        int nBack,
        int maxDifference)
    {
        List<int> frequencies = new();

        foreach (char c in stimulusPool.Distinct())
        {
            int count =
                sequence.Count(x => x == c);

            frequencies.Add(count);
        }

        int min = frequencies.Min();
        int max = frequencies.Max();

        return (max - min) <= maxDifference;
    }

    // =========================================================
    // Target pattern validation
    // =========================================================

    private static int CountTargets(
        bool[] mask)
    {
        return mask.Count(x => x);
    }

    private static bool HasConsecutiveTargets(
        bool[] mask,
        int startIndex)
    {
        for (int i = startIndex + 1;
             i < mask.Length;
             i++)
        {
            if (mask[i] && mask[i - 1])
                return true;
        }

        return false;
    }

    private static int GetMaximumNonTargetRun(
        bool[] mask,
        int startIndex)
    {
        int current = 0;
        int max = 0;

        for (int i = startIndex;
             i < mask.Length;
             i++)
        {
            if (mask[i])
            {
                current = 0;
            }
            else
            {
                current++;
                max = Mathf.Max(max, current);
            }
        }

        return max;
    }

    // =========================================================
    // Utility
    // =========================================================

    private static void Shuffle<T>(
        IList<T> list,
        System.Random rng)
    {
        for (int i = list.Count - 1;
             i > 0;
             i--)
        {
            int j = rng.Next(i + 1);

            (list[i], list[j]) =
                (list[j], list[i]);
        }
    }

    private static void ValidateParameters(
        int nBack,
        string stimulusPool,
        char zeroBackTarget,
        int totalTrials,
        int initializationTrials)
    {
        if (nBack < 0 || nBack > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nBack),
                "nBack must be between 0 and 3.");
        }

        if (string.IsNullOrEmpty(stimulusPool))
        {
            throw new ArgumentException(
                "stimulusPool is empty.");
        }

        if (stimulusPool.Distinct().Count() < 5)
        {
            Debug.LogWarning(
                "A larger stimulus pool is recommended " +
                "for avoiding unintended matches.");
        }

        if (nBack == 0 &&
            stimulusPool.Contains(zeroBackTarget))
        {
            throw new ArgumentException(
                "zeroBackTarget must not be included " +
                "in stimulusPool.");
        }

        if (initializationTrials < 3)
        {
            throw new ArgumentException(
                "initializationTrials must be at least 3 " +
                "for 0-3 back common design.");
        }

        if (totalTrials <= initializationTrials)
        {
            throw new ArgumentException(
                "totalTrials must be larger than " +
                "initializationTrials.");
        }
    }
}