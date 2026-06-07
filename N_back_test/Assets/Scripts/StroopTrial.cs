using System;

/// <summary>
/// Stroop課題で使用する提示条件。
/// </summary>
public enum StroopCondition
{
    Congruent,
    Neutral,
    Incongruent
}

/// <summary>
/// 刺激の表示色および回答ボタンに対応する色。
/// </summary>
public enum StroopColor
{
    Red,
    Blue,
    Green,
    Yellow
}

/// <summary>
/// 1試行分の実験条件を保持するデータクラス。
/// </summary>
[Serializable]
public class StroopTrial
{
    // 試行の提示条件。
    public StroopCondition condition;

    // 条件内での試行番号。1から開始する。
    public int trialIndex;

    // Practice試行の場合はtrue。
    public bool isPractice;

    public StroopTrial(StroopCondition condition, int trialIndex, bool isPractice)
    {
        this.condition = condition;
        this.trialIndex = trialIndex;
        this.isPractice = isPractice;
    }
}
