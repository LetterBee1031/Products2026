using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DebugInScene : MonoBehaviour
{
    private const int MaxLogLines = 10; // 表示するログの最大行数
    private string logText = "";
    private float lastLogTime = 0f;
    public GameObject DebugText;
    public GameObject DebugWindow;

    void Awake()
    {
        Application.logMessageReceived += LoggedCb;  // ログ出力時のコールバックを登録
    }

    private void Update()
    {
        if(Time.time - lastLogTime > 3f){
            logText = "";
        }
        
    }

    // Start と Updateは省略

    public void LoggedCb(string logstr, string stacktrace, LogType type)
    {
        logText += logstr + "\n";
        string[] logLines = logText.Split('\n');
        if (logLines.Length > MaxLogLines)
        {
            logText = string.Join("\n", logLines, logLines.Length - MaxLogLines, MaxLogLines);
        }

        DebugText.GetComponent<TextMeshProUGUI>().text += logstr;
        DebugText.GetComponent<TextMeshProUGUI>().text += "\n";
        // 常にTextの最下部（最新）を表示するように強制スクロール
        DebugWindow.GetComponent<ScrollRect>().verticalNormalizedPosition = 0;

        // 最後にログを表示した時刻を更新
        lastLogTime = Time.time;
    }
}
