using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Inspectorで必要な参照と値を設定済みとして、ブロック進行・回答・送信を行う。
public class VisualSearchExperimentManager : MonoBehaviour
{
    public enum Difficulty { Practice, Low, Medium, High }

    [Header("参照")]
    [SerializeField] private RequestSender requestSender; // 既存の参加者ID・通信設定を利用する。
    [SerializeField] private VisualSearchStimulusSpawner stimulusSpawner; // 刺激生成・削除担当。
    [SerializeField] private VisualSearchUIController uiController; // 任意。未設定でも実施可能。
    [Header("ブロック")]
    [SerializeField] private Difficulty[] blockOrder =
        { Difficulty.Practice, Difficulty.Low, Difficulty.Medium, Difficulty.High }; // 実施順序。
    [SerializeField, Min(0)] private int firstBlockId; // 実施順に採番。難易度とは独立。
    [SerializeField, Min(0.01f)] private float blockDurationSeconds = 120f; // 各ブロックの制限時間。
    [SerializeField] private bool startOnStart = false; // 有効ならPlay開始時に実験開始。
    [Header("刺激数")]
    [SerializeField, Min(3)] private int practiceSetSize = 5; // 練習の総刺激数。
    [SerializeField, Min(3)] private int lowSetSize = 5; // Lowの総刺激数。
    [SerializeField, Min(3)] private int mediumSetSize = 15; // Mediumの総刺激数。
    [SerializeField, Min(3)] private int highSetSize = 25; // Highの総刺激数。
    [Header("待機時間（秒）")]
    [SerializeField, Min(0)] private float trialStartDelay = 1f; // 各試行の提示前待機。
    [SerializeField, Min(0)] private float blockMessageSeconds = 1f; // ブロック前後の説明表示。
    [SerializeField, Min(0)] private float practiceFeedbackSeconds = 0.75f; // 練習のみ正誤を表示。
    [Header("乱数")]
    [SerializeField] private bool useRandomSeed = true; // trueなら指定Seed、falseなら実行時に生成。
    [SerializeField] private int randomSeed = 12345; // 条件と配置の再現に使用するSeed。
    [Header("回答入力（Button Action）")]
    [SerializeField] private InputActionReference answerPresentAction; // 「赤い球あり」に割り当てるAction。
    [SerializeField] private InputActionReference answerAbsentAction; // 「赤い球なし」に割り当てるAction。

    public bool IsRunning { get; private set; }         // 外部UI等が実行状態を参照するためのプロパティ。終了処理が完了するまでtrueを維持する。
    public int ActualRandomSeed { get; private set; }   // 自動生成Seedを使った場合も含め、今回の実験で実際に使用した値を公開する。
    private InputAction presentInput, absentInput;      // Inspectorの参照から取得したAction本体。イベント登録と解除に同じインスタンスを使う。
    private bool enabledPresent, enabledAbsent;         // このManagerが有効化したActionだけを終了時に無効化するための記録。
    // 中断要求、回答待ち、ボタン解放済み、回答確定、参加者の「あり」回答。
    private bool stopRequested, awaitingAnswer, inputArmed, answered, answerPresent;
    
    private double stimulusOnsetTime, responseTime;     // 提示と回答に同じ単調増加時計を使う。日時変更やTime.timeScaleの影響を受けない。

    private void Start()
    {
        if (startOnStart) StartExperiment();
    }

    // UIボタンまたはPlay中のコンテキストメニューから開始する。
    [ContextMenu("Start Visual Search")]
    public void StartExperiment()
    {
        if (!Application.isPlaying || IsRunning || !isActiveAndEnabled) return;
        presentInput = answerPresentAction.action;
        absentInput = answerAbsentAction.action;
        presentInput.performed += OnPresent;
        absentInput.performed += OnAbsent;
        // 自分で有効化したActionだけを終了時に無効化する。
        enabledPresent = !presentInput.enabled;
        enabledAbsent = !absentInput.enabled;
        if (enabledPresent) presentInput.Enable();
        if (enabledAbsent) absentInput.Enable();
        stopRequested = false;
        IsRunning = true;
        // Managerの無効化時にも終了通知を送れるよう、RequestSender上で進行させる。
        requestSender.StartCoroutine(RunExperiment());
    }

    // 説明 → 開始通知 → 試行の反復 → 終了通知の順で実施する。
    private IEnumerator RunExperiment()
    {
        var generator = new VisualSearchTrialGenerator(useRandomSeed, randomSeed);
        ActualRandomSeed = generator.Seed;
        try
        {
            for (int index = 0; index < blockOrder.Length && !stopRequested; index++)
            {
                Difficulty difficulty = blockOrder[index];
                int blockId = firstBlockId + index; // IDは難易度ではなく実施順から採番する。
                int setSize = GetSetSize(difficulty); // ブロック中のInspector変更から固定する。
                double duration = blockDurationSeconds;
                bool practice = difficulty == Difficulty.Practice;
                generator.BeginBlock();
                if (uiController != null) uiController.ShowBlockStart(difficulty.ToString(), blockId);
                yield return Delay(blockMessageSeconds);
                if (stopRequested) break;
                // 説明表示後に計時開始。試行中の待機・通信時間も制限時間に含む。
                double blockStart = Time.realtimeSinceStartupAsDouble;
                yield return requestSender.PostStatusFlag(difficulty.ToString(), blockId.ToString());
                int trialIndex = 0;
                // 制限時間は試行間で判定し、開始済みの試行は回答まで続行する。
                while (!stopRequested && Time.realtimeSinceStartupAsDouble - blockStart < duration)
                {
                    trialIndex++;
                    bool targetPresent = generator.NextTargetPresent();
                    stimulusSpawner.ClearStimuli();
                    if (uiController != null) uiController.Clear();
                    yield return Delay(trialStartDelay);
                    if (stopRequested) break;
                    // 全刺激を配置できなければ部分提示せず、ブロック終了へ進む。
                    if (!stimulusSpawner.TryPrepare(setSize, targetPresent, generator.Random))
                    {
                        Debug.LogWarning("Visual Search: stimulus placement failed.");
                        stopRequested = true;
                        break;
                    }
                    answered = false;
                    inputArmed = false;
                    // 同時提示の直後から、入力コールバックで回答するまでの時間を計測する。
                    stimulusSpawner.ShowStimuli();
                    stimulusOnsetTime = Time.realtimeSinceStartupAsDouble;
                    awaitingAnswer = true;
                    while (!answered && !stopRequested) yield return null;
                    awaitingAnswer = false;
                    if (stimulusSpawner != null) stimulusSpawner.ClearStimuli();
                    if (!answered) break; // 中断された未回答試行は計測結果として送らない。
                    bool correct = answerPresent == targetPresent;
                    float reactionMs = (float)((responseTime - stimulusOnsetTime) * 1000.0);
                    // 送信完了を待つ。通信エラーのログ出力はRequestSenderに任せる。
                    yield return requestSender.SendVisualSearchResult(blockId, difficulty.ToString(), trialIndex,
                        practice, targetPresent, correct, reactionMs, generator.Seed);
                    if (practice && !stopRequested)
                    {
                        if (uiController != null) uiController.ShowPracticeFeedback(correct);
                        yield return Delay(practiceFeedbackSeconds);
                    }
                }
                yield return requestSender.PostStatusFlag("block_end", blockId.ToString());
                if (!stopRequested)
                {
                    if (uiController != null) uiController.ShowBlockEnd();
                    yield return Delay(blockMessageSeconds);
                }
            }
            if (!stopRequested && uiController != null) uiController.ShowExperimentEnd();
        }
        // 終了時に刺激と入力購読を片付け、次回開始に備える。
        finally
        {
            awaitingAnswer = false;
            if (stimulusSpawner != null) stimulusSpawner.ClearStimuli();
            ReleaseInput();
            IsRunning = false;
        }
    }
    private int GetSetSize(Difficulty difficulty)
    {
        switch (difficulty)
        {
            case Difficulty.Low: return lowSetSize;
            case Difficulty.Medium: return mediumSetSize;
            case Difficulty.High: return highSetSize;
            default: return practiceSetSize;
        }
    }
    // timeScaleに依存せず待機し、中断要求があれば抜ける。
    private IEnumerator Delay(float seconds)
    {
        double until = Time.realtimeSinceStartupAsDouble + seconds;
        while (!stopRequested && Time.realtimeSinceStartupAsDouble < until) yield return null;
    }

    private void Update()
    {
        // 前試行から押し続けているボタンを回答にしない。
        if (awaitingAnswer && !inputArmed && !presentInput.IsPressed() && !absentInput.IsPressed())
            inputArmed = true;
    }
    private void OnPresent(InputAction.CallbackContext context) => AcceptAnswer(true);
    private void OnAbsent(InputAction.CallbackContext context) => AcceptAnswer(false);
    private void AcceptAnswer(bool present)
    {
        if (!awaitingAnswer || !inputArmed || answered || stopRequested) return;
        answerPresent = present;
        responseTime = Time.realtimeSinceStartupAsDouble;
        answered = true;
        awaitingAnswer = false; // 最初の1回答だけを採用する。
    }
    // 入力と刺激を止める。進行中の通信の後にblock_endを送るため、コルーチンは強制停止しない。
    [ContextMenu("Stop Visual Search")]
    public void StopExperiment()
    {
        stopRequested = true;
        awaitingAnswer = false;
        if (stimulusSpawner != null) stimulusSpawner.ClearStimuli();
    }
    private void ReleaseInput()
    {
        if (presentInput != null)
        {
            presentInput.performed -= OnPresent;
            if (enabledPresent) presentInput.Disable();
        }
        if (absentInput != null)
        {
            absentInput.performed -= OnAbsent;
            if (enabledAbsent) absentInput.Disable();
        }
        enabledPresent = enabledAbsent = false;
    }
    private void OnDisable()
    {
        StopExperiment();
        ReleaseInput();
    }
}
