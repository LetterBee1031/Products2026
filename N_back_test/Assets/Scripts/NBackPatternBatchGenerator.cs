using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class NBackPatternBatchGenerator : MonoBehaviour
{
    [Header("Pattern")]
    [SerializeField]
    private string stimulusPool = "ABCDEFGH";

    [SerializeField]
    private string zeroBackTarget = "X";

    [SerializeField]
    private int patternsPerCondition = 10;

    [Header("Trials")]
    [SerializeField]
    private int totalTrials = 60;

    [SerializeField]
    private int initializationTrials = 3;

    [SerializeField]
    private int maxNonTargetRun = 6;

    [SerializeField]
    private int maxFrequencyDifference = 2;

    [Header("Random")]
    [SerializeField]
    private int baseSeed = 20260814;

    [Header("Output")]
    [SerializeField]
    private string outputFolder =
        "GeneratedNBackPatterns";

    [ContextMenu("Generate N-Back Patterns")]
    public void GeneratePatterns()
    {
        if (string.IsNullOrEmpty(zeroBackTarget))
        {
            Debug.LogError(
                "zeroBackTarget is empty.");
            return;
        }

        char targetCharacter =
            zeroBackTarget[0];

        string folder =
            Path.Combine(
                Application.dataPath,
                outputFolder);

        Directory.CreateDirectory(folder);

        int generatedCount = 0;

        for (int nBack = 0;
             nBack <= 3;
             nBack++)
        {
            string conditionFolder =
                Path.Combine(
                    folder,
                    $"{nBack}back");

            Directory.CreateDirectory(
                conditionFolder);

            for (int i = 1;
                 i <= patternsPerCondition;
                 i++)
            {
                int seed =
                    baseSeed
                    + nBack * 10000
                    + i;

                NBackPatternData pattern =
                    NBackPatternGenerator.Generate(
                        nBack: nBack,
                        seed: seed,
                        stimulusPool: stimulusPool,
                        zeroBackTarget:
                            targetCharacter,
                        totalTrials:
                            totalTrials,
                        initializationTrials:
                            initializationTrials,
                        targetRate:
                            1f / 3f,
                        maxNonTargetRun:
                            maxNonTargetRun,
                        maxFrequencyDifference:
                            maxFrequencyDifference);

                pattern.patternId =
                    $"{nBack}back_{i:00}";

                string json =
                    JsonUtility.ToJson(
                        pattern,
                        true);

                string path =
                    Path.Combine(
                        conditionFolder,
                        $"{pattern.patternId}.json");

                File.WriteAllText(
                    path,
                    json);

                generatedCount++;

                Debug.Log(
                    $"Generated: " +
                    $"{pattern.patternId} " +
                    $"Target={pattern.targetCount}/" +
                    $"{pattern.evaluationTrials} " +
                    $"({pattern.targetRate:P2})");
            }
        }

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif

        Debug.Log(
            $"N-back pattern generation completed. " +
            $"Total = {generatedCount}");
    }
}