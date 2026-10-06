using TMPro;
using UnityEngine;

public class VisualSearchUIController : MonoBehaviour
{
    [SerializeField] private TMP_Text messageText; // XR用World Space Canvasの説明表示。

    public void ShowBlockStart(string difficulty, int blockId) =>
        Show($"{difficulty}（Block {blockId}）\n赤い球体があるか回答してください。");
    public void ShowPracticeFeedback(bool correct) => Show(correct ? "正解です" : "不正解です");
    public void ShowBlockEnd() => Show("ブロック終了");
    public void ShowExperimentEnd() => Show("視覚探索課題が終了しました");
    public void ShowError(string message) => Show("課題を停止しました\n" + message);
    public void Clear() => Show("");

    private void Show(string message)
    {
        if (messageText != null) messageText.text = message;
    }
}
