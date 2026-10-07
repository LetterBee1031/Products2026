using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// UIボタンから選択された視覚探索ブロックを1つずつ実行する。
public class VisualSearchExperimentManager : MonoBehaviour
{
    // Practiceは試行数制、Low・Medium・Highは時間制のブロックとして扱う。
    public enum Difficulty { Practice, Low, Medium, High }

    // 練習1試行のSet SizeとTarget有無をまとめて保持する。
    private struct PracticeCondition
    {
        public int setSize;
        public bool targetPresent;

        public PracticeCondition(int setSize, bool targetPresent)
        {
            this.setSize = setSize;
            this.targetPresent = targetPresent;
        }
    }

    [Header("参照")]
    [SerializeField] private RequestSender requestSender; // 状態通知と試行結果の送信に使用する。
    [SerializeField] private VisualSearchStimulusSpawner stimulusSpawner; // 刺激の生成・表示・削除を行う。
    [SerializeField] private VisualSearchUIController uiController; // 説明・正誤・終了表示。未設定でも課題は動作する。
    [SerializeField] private NasaTlxManager nasaTlxManager; // 本番ブロック終了後のNASA-TLXを開始する。
    [SerializeField] private GameObject buttonMoveForQuestion; // NASA-TLX回答画面へ進むUIボタン。
    [SerializeField] private GameObject panels; // 全てのUIパネルの親．視覚探索課題中に非表示にしておくため

    [Header("本番ブロック")]
    [SerializeField, Min(0.01f)] private float blockDurationSeconds = 120f; // 各本番ブロックの制限時間。
    [SerializeField, Min(3)] private int lowSetSize = 5; // Lowの1試行に提示する総刺激数。
    [SerializeField, Min(3)] private int mediumSetSize = 15; // Mediumの1試行に提示する総刺激数。
    [SerializeField, Min(3)] private int highSetSize = 25; // Highの1試行に提示する総刺激数。

    [Header("ブロック時間入力UI")]
    [SerializeField] private TMP_InputField blockDurationInputField; // Unity空間上で制限時間を入力する欄。
    [SerializeField] private XRNumericKeyboardInputBinder numericKeyboardInputBinder; // XRNumericKeyboardとの接続を管理する。
    [SerializeField, Min(0.01f)] private float minBlockDurationSeconds = 1f; // 入力できる最小時間。

    [Header("練習ブロック")]
    // 6条件（3つのSet Size × Target有無）を、それぞれ何回実施するか。
    [SerializeField, Min(1)] private int practiceRepetitionsPerCondition = 1;

    [Header("待機時間（秒）")]
    [SerializeField, Min(0)] private float trialStartDelay = 1f; // 前試行終了から次の刺激提示までの時間。
    [SerializeField, Min(0)] private float blockMessageSeconds = 1f; // ブロック開始・終了表示を見せる時間。
    [SerializeField, Min(0)] private float practiceFeedbackSeconds = 0.75f; // 練習の正誤表示時間。

    [Header("乱数")]
    [SerializeField] private bool useRandomSeed = true; // trueなら下の固定Seedを使用する。
    [SerializeField] private int randomSeed = 12345; // 条件順と刺激配置の再現に使用するSeed。

    [Header("回答入力（Button Action）")]
    [SerializeField] private InputActionReference answerPresentAction; // 「Targetあり」に割り当てるButton Action。
    [SerializeField] private InputActionReference answerAbsentAction; // 「Targetなし」に割り当てるButton Action。

    public bool IsRunning { get; private set; } // いずれかのブロックを実行中ならtrue。
    public int ActualRandomSeed { get; private set; } // 今回のブロックで実際に使用しているSeed。

    private InputAction presentInput; // answerPresentActionから取得した実際のAction。
    private InputAction absentInput; // answerAbsentActionから取得した実際のAction。
    private bool enabledPresent; // このクラスがPresent Actionを有効化したか。
    private bool enabledAbsent; // このクラスがAbsent Actionを有効化したか。
    private bool stopRequested; // 中断要求を受けた場合にtrue。
    private bool awaitingAnswer; // 刺激提示中で回答を受け付けている場合にtrue。
    private bool inputArmed; // 両方の回答ボタンが一度離されたことを確認済みならtrue。
    private bool answered; // 現在の試行で回答が確定した場合にtrue。
    private bool answerPresent; // 参加者の回答。trueがTargetあり、falseがTargetなし。
    private double stimulusOnsetTime; // 全刺激を表示した時刻。
    private double responseTime; // 回答入力を受け付けた時刻。
    // Practiceを除く本番ブロックへ、開始された順に1から割り当てるID。
    private int nextMainBlockId = 1;
    // 同じ条件を再度開始した場合は、最初に割り当てたIDを再利用する。
    private int assignedLowBlockId;
    private int assignedMediumBlockId;
    private int assignedHighBlockId;
    // 完了した本番ブロックのNASA-TLXへ渡すblock_id。Practiceでは設定しない。
    private string pendingNasaTlxBlockId;

    // 各メソッドを対応するUI ButtonのOn Clickへ登録する。
    public void StartPractice() => StartBlock(Difficulty.Practice);
    public void StartLow() => StartBlock(Difficulty.Low);
    public void StartMedium() => StartBlock(Difficulty.Medium);
    public void StartHigh() => StartBlock(Difficulty.High);

    private void Awake()
    {
        // StroopManager、MentalArithmeticManagerと同じく、未設定ならシーン内から取得する。
        if (nasaTlxManager == null)
        {
            GameObject eventSystem = GameObject.Find("EventSystem");
            if (eventSystem != null)
                nasaTlxManager = eventSystem.GetComponent<NasaTlxManager>();
        }

        if (nasaTlxManager == null)
            nasaTlxManager = FindFirstObjectByType<NasaTlxManager>();

        buttonMoveForQuestion?.SetActive(false);
        SetupDurationInputField();
    }

    private void StartBlock(Difficulty difficulty)
    {
        // 実行中に別のボタンが押されても、新しいブロックは開始しない。
        if (!Application.isPlaying || IsRunning || !isActiveAndEnabled) return;

        // 新しいブロックを開始した場合、前ブロックの未回答NASA-TLXへの遷移情報は破棄する。
        pendingNasaTlxBlockId = null;
        buttonMoveForQuestion?.SetActive(false);

        // InputFieldの現在値を確定してからブロックを開始する。
        SetBlockDuration(blockDurationInputField != null
            ? blockDurationInputField.text
            : blockDurationSeconds.ToString(CultureInfo.InvariantCulture));

        // Input Actionのperformedイベントで回答時刻を取得する。
        presentInput = answerPresentAction.action;
        absentInput = answerAbsentAction.action;
        presentInput.performed += OnPresent;
        absentInput.performed += OnAbsent;

        // すでに別のComponentが有効化しているActionは、終了時に無効化しない。
        enabledPresent = !presentInput.enabled;
        enabledAbsent = !absentInput.enabled;
        if (enabledPresent) presentInput.Enable();
        if (enabledAbsent) absentInput.Enable();

        panels.SetActive(false); // パネル非表示

        stopRequested = false;
        IsRunning = true;
        // 選択した難易度だけを実行し、終了後は次のUIボタン入力を待つ。
        // Practiceは本番の実施順に含めない。本番3条件は最初に開始された順で採番する。
        int blockId = GetOrAssignBlockId(difficulty);
        requestSender.StartCoroutine(RunBlock(difficulty, blockId));
    }

    // 選択された1ブロックの開始通知、試行、終了通知を順番に実行する。
    private IEnumerator RunBlock(Difficulty difficulty, int blockId)
    {
        var generator = new VisualSearchTrialGenerator(useRandomSeed, randomSeed);
        ActualRandomSeed = generator.Seed;
        bool practice = difficulty == Difficulty.Practice;
        string levelName = difficulty.ToString().ToLowerInvariant();
        string visualBlockId = "visual_" + blockId.ToString(CultureInfo.InvariantCulture);

        // 開始案内の表示後、選択した難易度をStatus Flagとして送信する。
        if (uiController != null) uiController.ShowBlockStart(difficulty.ToString(), blockId);
        yield return Delay(blockMessageSeconds);

        if (!stopRequested)
        {
            // 例: visual_low_start / block_id: visual_1
            yield return requestSender.PostStatusFlag(
                "visual_" + levelName + "_start",
                visualBlockId);

            // Practiceだけは指定試行数、本番3レベルは制限時間で終了する。
            if (practice)
                yield return RunPracticeTrials(generator, visualBlockId);
            else
                yield return RunTimedTrials(generator, difficulty, visualBlockId);

            // 最後の試行結果を送信してからブロック終了を通知する。
            // 例: visual_low_end / block_id: visual_1
            yield return requestSender.PostStatusFlag(
                "visual_" + levelName + "_end",
                visualBlockId);
        }

        // 次のUIボタンから別ブロックを開始できる状態へ戻す。
        stimulusSpawner.ClearStimuli();
        ReleaseInput();

        if (!stopRequested && uiController != null)
        {
            panels.SetActive(true); // パネル表示
            uiController.ShowBlockEnd();
            yield return Delay(blockMessageSeconds);
        }

        // 本番ブロックを正常完了した場合だけ、NASA-TLXへ進める状態にする。
        if (!practice && !stopRequested)
        {
            pendingNasaTlxBlockId = visualBlockId;
            buttonMoveForQuestion?.SetActive(true);
        }

        // 終了表示とNASA-TLX遷移準備が終わってから、次の開始ボタンを受け付ける。
        IsRunning = false;
    }

    // Set Size 5・15・25 × Target Present・Absentを、それぞれ指定回数実施する。
    private IEnumerator RunPracticeTrials(VisualSearchTrialGenerator generator, string blockId)
    {
        List<PracticeCondition> conditions = CreatePracticeConditions(generator.Random);

        for (int i = 0; i < conditions.Count && !stopRequested; i++)
        {
            PracticeCondition condition = conditions[i];
            // 練習のtrial_indexも、ランダム化後の実施順で1から採番する。
            yield return RunTrial(generator, blockId, Difficulty.Practice, i + 1,
                condition.setSize, condition.targetPresent, true);
        }
    }

    // Low・Medium・Highは、選択した難易度だけを制限時間まで反復する。
    private IEnumerator RunTimedTrials(
        VisualSearchTrialGenerator generator,
        Difficulty difficulty,
        string blockId)
    {
        int trialIndex = 0;
        int setSize = GetSetSize(difficulty);
        // 本番はこの時点から計時し、試行間で制限時間を確認する。
        double blockStart = Time.realtimeSinceStartupAsDouble;
        generator.BeginBlock();

        while (!stopRequested &&
               Time.realtimeSinceStartupAsDouble - blockStart < blockDurationSeconds)
        {
            trialIndex++;
            // 2試行ごとにPresentとAbsentが1回ずつになるようGeneratorが決定する。
            bool targetPresent = generator.NextTargetPresent();
            yield return RunTrial(generator, blockId, difficulty, trialIndex,
                setSize, targetPresent, false);
        }
    }

    private IEnumerator RunTrial(
        VisualSearchTrialGenerator generator,
        string blockId,
        Difficulty difficulty,
        int trialIndex,
        int setSize,
        bool targetPresent,
        bool practice)
    {
        // 前試行の刺激とメッセージを消してから、指定時間待機する。
        stimulusSpawner.ClearStimuli();
        if (uiController != null) uiController.Clear();
        yield return Delay(trialStartDelay);
        if (stopRequested) yield break;

        // 指定されたSet SizeとTarget条件に従って、全刺激を非表示状態で準備する。
        if (!stimulusSpawner.TryPrepare(setSize, targetPresent, generator.Random))
        {
            Debug.LogWarning("Visual Search: stimulus placement failed.");
            stopRequested = true;
            yield break;
        }

        answered = false;
        inputArmed = false;
        // 全刺激を同時に表示し、その直後を反応時間の計測開始点とする。
        stimulusSpawner.ShowStimuli();
        stimulusOnsetTime = Time.realtimeSinceStartupAsDouble;
        awaitingAnswer = true;

        // 回答入力または中断要求が来るまで、試行を継続する。
        while (!answered && !stopRequested) yield return null;

        awaitingAnswer = false;
        stimulusSpawner.ClearStimuli();
        if (!answered) yield break;

        // 実際のTarget条件と参加者のPresent/Absent回答を比較する。
        bool correct = answerPresent == targetPresent;
        float reactionTimeMs = (float)((responseTime - stimulusOnsetTime) * 1000.0);

        // 1試行分の結果を送信し、送信処理の完了後に次の試行へ進む。
        yield return requestSender.SendVisualSearchResult(
            blockId,
            difficulty.ToString(),
            trialIndex,
            practice,
            targetPresent,
            correct,
            reactionTimeMs,
            generator.Seed);

        // 正誤フィードバックはPracticeだけで表示する。
        if (practice && !stopRequested)
        {
            if (uiController != null) uiController.ShowPracticeFeedback(correct);
            yield return Delay(practiceFeedbackSeconds);
        }
    }

    // 練習で必要な全条件を指定回数分生成し、実施回数を変えずに順序だけを混ぜる。
    private List<PracticeCondition> CreatePracticeConditions(System.Random random)
    {
        var conditions = new List<PracticeCondition>();
        // 練習条件は本番用Inspector値に依存せず、指定された3種類に固定する。
        int[] setSizes = { 5, 15, 25 };

        // 各Set SizeについてTargetあり・なしを同じ回数追加する。
        foreach (int setSize in setSizes)
        {
            for (int i = 0; i < practiceRepetitionsPerCondition; i++)
            {
                conditions.Add(new PracticeCondition(setSize, true));
                conditions.Add(new PracticeCondition(setSize, false));
            }
        }

        // 条件数は変えず、練習内の提示順だけをランダム化する。
        for (int i = conditions.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            PracticeCondition temp = conditions[i];
            conditions[i] = conditions[j];
            conditions[j] = temp;
        }

        return conditions;
    }

    // UIボタンで選ばれた本番難易度に対応するSet Sizeを返す。
    private int GetSetSize(Difficulty difficulty)
    {
        switch (difficulty)
        {
            case Difficulty.Low: return lowSetSize;
            case Difficulty.Medium: return mediumSetSize;
            default: return highSetSize;
        }
    }

    // Low・Medium・Highの各条件に、最初に開始された順で1、2、3を割り当てる。
    private int GetOrAssignBlockId(Difficulty difficulty)
    {
        if (difficulty == Difficulty.Practice) return 0;

        if (difficulty == Difficulty.Low)
        {
            if (assignedLowBlockId == 0) assignedLowBlockId = nextMainBlockId++;
            return assignedLowBlockId;
        }

        if (difficulty == Difficulty.Medium)
        {
            if (assignedMediumBlockId == 0) assignedMediumBlockId = nextMainBlockId++;
            return assignedMediumBlockId;
        }

        if (assignedHighBlockId == 0) assignedHighBlockId = nextMainBlockId++;
        return assignedHighBlockId;
    }

    // XRNumericKeyboardのOKまたはInputFieldの編集終了時に、入力した秒数を反映する。
    public void SetBlockDuration(string inputText)
    {
        if (!TryParseFloat(inputText, out float seconds))
        {
            // 数値に変換できない場合は現在の設定値をInputFieldへ戻す。
            SyncDurationInputField();
            return;
        }

        blockDurationSeconds = Mathf.Max(minBlockDurationSeconds, seconds);
        SyncDurationInputField();
    }

    // StroopManagerと同じ形式で、TMP_InputFieldをXR数値キーボードへ接続する。
    private void SetupDurationInputField()
    {
        blockDurationSeconds = Mathf.Max(minBlockDurationSeconds, blockDurationSeconds);
        SyncDurationInputField();

        if (blockDurationInputField != null)
        {
            blockDurationInputField.contentType = TMP_InputField.ContentType.DecimalNumber;
            blockDurationInputField.onEndEdit.AddListener(SetBlockDuration);
        }

        if (numericKeyboardInputBinder == null)
            numericKeyboardInputBinder = GetComponent<XRNumericKeyboardInputBinder>();

        if (numericKeyboardInputBinder == null)
            numericKeyboardInputBinder = gameObject.AddComponent<XRNumericKeyboardInputBinder>();

        // blockDurationSecondsはfloatなので、小数点を使用できるキーボードとして登録する。
        numericKeyboardInputBinder.BindDecimal(blockDurationInputField, SetBlockDuration);
    }

    private void SyncDurationInputField()
    {
        blockDurationInputField?.SetTextWithoutNotify(
            blockDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture));
    }

    // 端末の言語設定とピリオド小数の両方を受け付ける。
    private static bool TryParseFloat(string inputText, out float value)
    {
        if (float.TryParse(inputText, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return true;

        return float.TryParse(inputText, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // 本番ブロック終了画面の遷移ボタンから呼び出し、対応するNASA-TLXを開始する。
    public void MoveToNasaTlxQuestionnaire()
    {
        if (string.IsNullOrWhiteSpace(pendingNasaTlxBlockId))
        {
            Debug.LogWarning(
                "VisualSearchExperimentManager: completed block for NASA-TLX is not available.");
            return;
        }

        if (nasaTlxManager == null)
        {
            Debug.LogError("VisualSearchExperimentManager: NasaTlxManager is not assigned.");
            return;
        }

        string blockId = pendingNasaTlxBlockId;
        pendingNasaTlxBlockId = null;
        buttonMoveForQuestion?.SetActive(false);
        if (uiController != null) uiController.Clear();
        nasaTlxManager.StartQuestionnaire(blockId);
    }

    // Time.timeScaleに依存しない時計で待機する。中断時は待機を打ち切る。
    private IEnumerator Delay(float seconds)
    {
        double endTime = Time.realtimeSinceStartupAsDouble + seconds;
        while (!stopRequested && Time.realtimeSinceStartupAsDouble < endTime)
            yield return null;
    }

    private void Update()
    {
        // 提示前から押し続けている入力は回答として扱わない。
        if (awaitingAnswer && !inputArmed &&
            !presentInput.IsPressed() && !absentInput.IsPressed())
            inputArmed = true;
    }

    // 2種類の入力を共通の回答確定処理へ渡す。
    private void OnPresent(InputAction.CallbackContext context) => AcceptAnswer(true);
    private void OnAbsent(InputAction.CallbackContext context) => AcceptAnswer(false);

    // 刺激提示中に受け取った最初の回答だけを記録する。
    private void AcceptAnswer(bool present)
    {
        if (!awaitingAnswer || !inputArmed || answered || stopRequested) return;

        answerPresent = present;
        responseTime = Time.realtimeSinceStartupAsDouble;
        answered = true;
        awaitingAnswer = false;
    }

    // 未回答試行を記録せずに現在のブロックを中断する。
    [ContextMenu("Stop Visual Search")]
    public void StopExperiment()
    {
        stopRequested = true;
        awaitingAnswer = false;
        if (stimulusSpawner != null) stimulusSpawner.ClearStimuli();
    }

    // イベントの重複登録を防ぐため、ブロック終了時に購読を解除する。
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

        enabledPresent = false;
        enabledAbsent = false;
    }

    private void OnDisable()
    {
        StopExperiment();
        ReleaseInput();
    }

    private void OnDestroy()
    {
        // シーン破棄時にイベントを解除し、再生成時の重複登録を防ぐ。
        blockDurationInputField?.onEndEdit.RemoveListener(SetBlockDuration);

        if (numericKeyboardInputBinder != null)
            numericKeyboardInputBinder.Unbind(blockDurationInputField);
    }
}
