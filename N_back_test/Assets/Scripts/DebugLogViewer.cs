using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DebugLogViewer : MonoBehaviour
{
    [Header("UI")]
    // ログ表示用TextMeshPro
    public TextMeshProUGUI logText;

    [Header("Settings")]
    // 最大表示行数
    public int maxLines = 20;

    // ログ保持Queue
    private readonly Queue<string> logQueue = new Queue<string>();

    void Start()
    {
        // Consoleログ受信イベント登録
        Application.logMessageReceived += HandleLog;
        UpdateLogText();
    }

    void OnDestroy()
    {
        // イベント解除
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(
        string logString,
        string stackTrace,
        LogType type
    )
    {
        string prefix = "";

        // ログ種別ごとのプレフィックス
        switch (type)
        {
            case LogType.Log:
                prefix = "[LOG]";
                break;

            case LogType.Warning:
                prefix = "[WARN]";
                break;

            case LogType.Error:
                prefix = "[ERROR]";
                break;

            case LogType.Exception:
                prefix = "[EXCEPTION]";
                break;
        }

        // 表示文字列作成
        string finalLog = $"{prefix} {logString}";

        // Queueへ追加
        logQueue.Enqueue(finalLog);

        // 最大行数超過なら古いログ削除
        while (logQueue.Count > maxLines)
        {
            logQueue.Dequeue();
        }

        UpdateLogText();
    }

    void UpdateLogText()
    {
        // Text未設定なら終了
        if (logText == null)
        {
            return;
        }

        string combined = "";

        // Queue内容連結
        foreach (string log in logQueue)
        {
            combined += log + "\n";
        }

        // UI反映
        logText.text = combined;
    }
}