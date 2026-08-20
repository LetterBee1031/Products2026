using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;


// 1回のN-back試行を1 Blockとして保持する。
[Serializable]
public class NBackBlockAssignment
{
    // 参加者ID。例: P001
    public string userId;

    // Session番号。1から開始。
    public int sessionId;

    // 参加者内でのBlock通し番号。
    // Sessionが変わってもリセットしない。
    public int blockId;

    // Session内での実施位置。1から開始。
    public int positionInSession;

    // このBlockで実施するN-back条件。
    public int nBack;

    // Session内の実施順序を識別するID。
    // 例: order_2_0_3_1
    public string orderId;
}


// 1 Session分の情報。
[Serializable]
public class NBackSessionAssignment
{
    public int sessionId;
    public string orderId;

    // Session内でのN-back実施順序。
    // 例: [2, 0, 3, 1]
    public int[] order;

    public NBackBlockAssignment[] blocks;
}


// 参加者1人分の割当情報。
[Serializable]
public class NBackParticipantAssignment
{
    public string userId;
    public NBackSessionAssignment[] sessions;
}


// userIdごとに保存するJSONファイルのルートデータ。
[Serializable]
public class NBackParticipantAssignmentData
{
    // 割当生成に使用したSeed。
    public int seed;

    // 1人あたりのSession数。
    public int sessionsPerParticipant;

    // 使用したN-back条件。
    // 例: [0, 1, 2, 3]
    public int[] includedNBacks;

    // このファイルの対象参加者。
    public NBackParticipantAssignment participant;
}


// N-back実験の実施順序とBlock割当を生成する。
//
// 処理概要:
// 1. 指定されたN-back条件の全順列を生成
// 2. 各参加者・各Sessionに順序を割り当てる
// 3. 同一参加者内で実施順序や実施位置の偏りを抑える
// 4. 各N-back試行にBlock番号を付ける
// 5. userIdごとに別々のJSONとして保存する
public class NBackExperimentAssignmentGenerator : MonoBehaviour
{
    [Header("N-Back Conditions")]

    // 1 Session内で実施するN-back条件。
    // 例: [0, 1, 2, 3]
    // 例: [0, 2, 3]
    [SerializeField]
    private List<int> includedNBacks = new List<int> { 0, 1, 2, 3 };


    [Header("Participants")]

    // 割当を生成する参加者数。
    [SerializeField]
    private int participantCount = 12;

    // 最初の参加者番号。
    // 1の場合、P001から開始する。
    [SerializeField]
    private int participantStartNumber = 1;

    [SerializeField]
    private string participantPrefix = "P";

    // IDの数字部分の桁数。
    // 3の場合、P001, P002 ...となる。
    [SerializeField]
    private int participantNumberDigits = 3;


    [Header("Sessions")]

    // 1人あたりのSession数。
    [SerializeField]
    private int sessionsPerParticipant = 3;


    [Header("Random")]

    // 疑似乱数Seed。
    // 同じ設定とSeedなら同じ割当を再現できる。
    [SerializeField]
    private int seed = 20260817;


    [Header("Output")]

    // persistentDataPath以下に作成するフォルダ名。
    [SerializeField]
    private string outputFolder = "Resources/GeneratedNBackAssignments";


    // Unity空間上のUIなどから使用するN-back条件を変更する。
    public void SetIncludedNBacks(List<int> values)
    {
        includedNBacks = new List<int>(values);
    }

    // Unity UIなどから参加者数を変更する。
    public void SetParticipantCount(int value)
    {
        participantCount = value;
    }

    // Unity UIなどからSession数を変更する。
    public void SetSessionsPerParticipant(int value)
    {
        sessionsPerParticipant = value;
    }

    // Unity UIなどからSeedを変更する。
    public void SetSeed(int value)
    {
        seed = value;
    }


    // 実験割当を生成して、userIdごとにJSONへ保存する。
    [ContextMenu("Generate N-Back Experiment Assignments")]
    public void GenerateAssignments()
    {
        if (!ValidateSettings())
            return;

        int[] conditions = includedNBacks.ToArray();

        // 指定された条件の全順列を生成する。
        // [0,1,2,3] → 24通り
        // [0,2,3]   → 6通り
        List<int[]> allOrders = GeneratePermutations(conditions);

        Debug.Log($"N-back conditions: {string.Join(", ", conditions)}");
        Debug.Log($"Available order patterns: {allOrders.Count}");

        // Seedによって実験割当を再現可能にする。
        System.Random rng = new System.Random(seed);

        // 実験全体で各Orderが何回使用されたか記録する。
        // 参加者間でも特定のOrderに偏らないようにする。
        Dictionary<string, int> globalOrderUsage = new Dictionary<string, int>();

        foreach (int[] order in allOrders)
            globalOrderUsage[GetOrderKey(order)] = 0;

        List<NBackParticipantAssignment> participantAssignments =
            new List<NBackParticipantAssignment>();


        // 参加者ごとの割当を生成する。
        for (int participantIndex = 0;
             participantIndex < participantCount;
             participantIndex++)
        {
            int participantNumber =
                participantStartNumber + participantIndex;

            string userId =
                participantPrefix +
                participantNumber.ToString($"D{participantNumberDigits}");

            // この参加者が既に使用したOrder。
            HashSet<string> usedOrders = new HashSet<string>();

            // 各N-backが各位置で何回実施されたかを記録する。
            //
            // positionUsage[0][0]
            // → 0-backがSessionの1番目に配置された回数
            Dictionary<int, int[]> positionUsage =
                new Dictionary<int, int[]>();

            foreach (int nBack in conditions)
                positionUsage[nBack] = new int[conditions.Length];

            // 条件遷移の使用回数。
            // 例: 0-back → 3-back が何回発生したか。
            Dictionary<string, int> carryoverUsage =
                new Dictionary<string, int>();

            List<NBackSessionAssignment> sessions =
                new List<NBackSessionAssignment>();

            // Block番号は参加者ごとに1から開始し、
            // Sessionが変わってもリセットしない。
            int participantBlockCounter = 1;


            for (int sessionIndex = 0;
                 sessionIndex < sessionsPerParticipant;
                 sessionIndex++)
            {
                // 現在までの割当状況から、
                // 最も偏りが少ないOrderを選択する。
                int[] selectedOrder = SelectBestOrder(
                    allOrders,
                    usedOrders,
                    positionUsage,
                    carryoverUsage,
                    globalOrderUsage,
                    rng);

                string orderKey = GetOrderKey(selectedOrder);
                string orderId = GetOrderId(selectedOrder);

                usedOrders.Add(orderKey);
                globalOrderUsage[orderKey]++;


                // 各N-backの実施位置の使用回数を更新する。
                for (int position = 0;
                     position < selectedOrder.Length;
                     position++)
                {
                    int nBack = selectedOrder[position];
                    positionUsage[nBack][position]++;
                }


                // 条件遷移の使用回数を更新する。
                for (int position = 0;
                     position < selectedOrder.Length - 1;
                     position++)
                {
                    string pairKey = GetPairKey(
                        selectedOrder[position],
                        selectedOrder[position + 1]);

                    if (!carryoverUsage.ContainsKey(pairKey))
                        carryoverUsage[pairKey] = 0;

                    carryoverUsage[pairKey]++;
                }


                // Session内の各N-back試行をBlockとして登録する。
                List<NBackBlockAssignment> blocks =
                    new List<NBackBlockAssignment>();

                for (int position = 0;
                     position < selectedOrder.Length;
                     position++)
                {
                    NBackBlockAssignment block =
                        new NBackBlockAssignment
                        {
                            userId = userId,
                            sessionId = sessionIndex + 1,
                            blockId = participantBlockCounter,
                            positionInSession = position + 1,
                            nBack = selectedOrder[position],
                            orderId = orderId
                        };

                    blocks.Add(block);
                    participantBlockCounter++;
                }


                NBackSessionAssignment session =
                    new NBackSessionAssignment
                    {
                        sessionId = sessionIndex + 1,
                        orderId = orderId,
                        order = (int[])selectedOrder.Clone(),
                        blocks = blocks.ToArray()
                    };

                sessions.Add(session);
            }


            NBackParticipantAssignment participant =
                new NBackParticipantAssignment
                {
                    userId = userId,
                    sessions = sessions.ToArray()
                };

            participantAssignments.Add(participant);
        }


        // Android/HMD実機でも書き込み可能な場所を使用する。
        string folder = Path.Combine(
                Application.dataPath,
                outputFolder);

        Directory.CreateDirectory(folder);


        // 参加者ごとに別々のJSONファイルとして保存する。
        foreach (NBackParticipantAssignment participant
                 in participantAssignments)
        {
            SaveParticipantJson(
                participant,
                conditions,
                folder);
        }

        PrintSummary(participantAssignments);

        Debug.Log(
            $"N-back assignment generation completed. " +
            $"Participants={participantCount}, " +
            $"Sessions={sessionsPerParticipant}, " +
            $"Conditions={conditions.Length}, " +
            $"Seed={seed}");

        Debug.Log($"Output folder: {folder}");
    }


    // 現在までの割当状況から最も偏りの少ないOrderを選択する。
    private static int[] SelectBestOrder(
        List<int[]> allOrders,
        HashSet<string> usedOrders,
        Dictionary<int, int[]> positionUsage,
        Dictionary<string, int> carryoverUsage,
        Dictionary<string, int> globalOrderUsage,
        System.Random rng)
    {
        // 原則として同一Participantでは未使用Orderのみを候補にする。
        List<int[]> unusedOrders = allOrders
            .Where(order => !usedOrders.Contains(GetOrderKey(order)))
            .ToList();

        // 全Orderを使い切った場合のみ再利用を許可する。
        List<int[]> candidates =
            unusedOrders.Count > 0 ? unusedOrders : allOrders;

        double bestScore = double.MaxValue;
        List<int[]> bestCandidates = new List<int[]>();

        foreach (int[] order in candidates)
        {
            double score = CalculateOrderScore(
                order,
                positionUsage,
                carryoverUsage,
                globalOrderUsage);

            if (score < bestScore)
            {
                bestScore = score;
                bestCandidates.Clear();
                bestCandidates.Add(order);
            }
            else if (Math.Abs(score - bestScore) < 0.0001)
            {
                bestCandidates.Add(order);
            }
        }

        // 同点の場合のみ疑似ランダムに1つ選択する。
        int selectedIndex = rng.Next(bestCandidates.Count);

        return (int[])bestCandidates[selectedIndex].Clone();
    }


    // Orderの偏りをPenaltyとして数値化する。
    // Scoreが小さいOrderほど優先する。
    private static double CalculateOrderScore(
        int[] order,
        Dictionary<int, int[]> positionUsage,
        Dictionary<string, int> carryoverUsage,
        Dictionary<string, int> globalOrderUsage)
    {
        double score = 0.0;

        // 同じN-backが毎Session同じ位置になることを強く避ける。
        const double positionWeight = 100.0;

        for (int position = 0;
             position < order.Length;
             position++)
        {
            int nBack = order[position];

            score +=
                positionUsage[nBack][position] *
                positionWeight;
        }

        // 同じ条件遷移が繰り返されることを避ける。
        const double carryoverWeight = 20.0;

        for (int position = 0;
             position < order.Length - 1;
             position++)
        {
            string pairKey = GetPairKey(
                order[position],
                order[position + 1]);

            if (carryoverUsage.TryGetValue(
                    pairKey,
                    out int count))
            {
                score += count * carryoverWeight;
            }
        }

        // 実験全体でも特定Orderだけが多く使用されることを避ける。
        const double globalOrderWeight = 5.0;

        string orderKey = GetOrderKey(order);

        if (globalOrderUsage.TryGetValue(
                orderKey,
                out int usageCount))
        {
            score += usageCount * globalOrderWeight;
        }

        return score;
    }


    // 指定されたN-back条件の全順列を生成する。
    private static List<int[]> GeneratePermutations(int[] source)
    {
        List<int[]> result = new List<int[]>();
        int[] values = (int[])source.Clone();

        GeneratePermutationsRecursive(
            values,
            0,
            result);

        return result;
    }


    // 再帰的に全順列を生成する。
    private static void GeneratePermutationsRecursive(
        int[] values,
        int index,
        List<int[]> result)
    {
        if (index == values.Length)
        {
            result.Add((int[])values.Clone());
            return;
        }

        for (int i = index;
             i < values.Length;
             i++)
        {
            Swap(values, index, i);

            GeneratePermutationsRecursive(
                values,
                index + 1,
                result);

            Swap(values, index, i);
        }
    }


    // 参加者1人分のJSONを保存する。
    private void SaveParticipantJson(
        NBackParticipantAssignment participant,
        int[] conditions,
        string folder)
    {
        NBackParticipantAssignmentData data =
            new NBackParticipantAssignmentData
            {
                seed = seed,
                sessionsPerParticipant = sessionsPerParticipant,
                includedNBacks = conditions,
                participant = participant
            };

        string json =
            JsonUtility.ToJson(data, true);

        // 例:
        // P001_nback_assignment.json
        string fileName =
            $"{participant.userId}_nback_assignment.json";

        string path =
            Path.Combine(
                folder,
                fileName);

        File.WriteAllText(
            path,
            json);

        Debug.Log(
            $"Saved JSON: {participant.userId} -> {path}");
    }


    // 生成結果をConsoleに表示する。
    private static void PrintSummary(
        List<NBackParticipantAssignment> participants)
    {
        foreach (NBackParticipantAssignment participant in participants)
        {
            Debug.Log($"===== {participant.userId} =====");

            foreach (NBackSessionAssignment session in participant.sessions)
            {
                string orderText = string.Join(
                    " -> ",
                    session.order.Select(
                        n => $"{n}-back"));

                Debug.Log(
                    $"Session {session.sessionId}: " +
                    $"{orderText} [{session.orderId}]");

                foreach (NBackBlockAssignment block in session.blocks)
                {
                    Debug.Log(
                        $"Block {block.blockId}: " +
                        $"{block.nBack}-back " +
                        $"(Position {block.positionInSession})");
                }
            }
        }
    }


    // 設定値を検証する。
    private bool ValidateSettings()
    {
        if (includedNBacks == null ||
            includedNBacks.Count == 0)
        {
            Debug.LogError(
                "includedNBacks is empty.");

            return false;
        }

        // 現在は0～3-backのみ対応する。
        foreach (int nBack in includedNBacks)
        {
            if (nBack < 0 || nBack > 3)
            {
                Debug.LogError(
                    $"Invalid nBack value: {nBack}");

                return false;
            }
        }

        // 同一N-back条件の重複指定は禁止する。
        if (includedNBacks.Distinct().Count()
            != includedNBacks.Count)
        {
            Debug.LogError(
                "includedNBacks contains duplicate values.");

            return false;
        }

        if (participantCount <= 0)
        {
            Debug.LogError(
                "participantCount must be greater than 0.");

            return false;
        }

        if (sessionsPerParticipant <= 0)
        {
            Debug.LogError(
                "sessionsPerParticipant must be greater than 0.");

            return false;
        }

        if (participantNumberDigits <= 0)
        {
            Debug.LogError(
                "participantNumberDigits must be greater than 0.");

            return false;
        }

        return true;
    }


    // [2,0,3,1] → "2_0_3_1"
    private static string GetOrderKey(int[] order)
    {
        return string.Join("_", order);
    }


    // [2,0,3,1] → "order_2_0_3_1"
    private static string GetOrderId(int[] order)
    {
        return "order_" + GetOrderKey(order);
    }


    // 2-back → 3-back → "2_3"
    private static string GetPairKey(
        int first,
        int second)
    {
        return $"{first}_{second}";
    }


    // 配列内の2要素を交換する。
    private static void Swap(
        int[] values,
        int a,
        int b)
    {
        (values[a], values[b]) =
            (values[b], values[a]);
    }
}
