using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class StroopManager : MonoBehaviour
{
    /// 将来RequestSenderへ渡す1試行分のログデータ。
    /// 現在、サーバー送信処理は保留している。
    [Serializable]
    public class StroopLogData
    {
        public string user_id;
        public string condition;
        public int trial_index;
        public bool is_practice;
        public bool is_correct;
        public float reaction_time_ms;
        public string stimulus_onset_time;
        public string response_time;
        public string result;
    }

    // ブロック開始入力と回答入力を切り替えるための実行状態。
    private enum TrialState
    {
        Idle,
        Fixation,
        Stimulus,
        Response,
        Rest,
        Results
    }

    // 画面に提示する文字と、その表示色の組み合わせ。
    private struct Stimulus
    {
        public string text;
        public StroopColor color;
    }

    [Header("UI")]
    [SerializeField] private TMP_Text stimulusText;
    [SerializeField] private TMP_Text instructionText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text resultText;

    [Header("Participant")]
    [SerializeField] private string userId = "test_user";

    // Stroopの試行ログをサーバへ送信するためのRequestSender。
    // Inspectorで未設定の場合は、Awakeでシーン内から自動取得する。
    [Header("Server")]
    [SerializeField] private RequestSender requestSender;

    // ここシーン上からいじれるように 
    [Header("Timing (seconds)")]
    [SerializeField] public float fixationDuration = 1.0f;
    [SerializeField] public float stimulusDuration = 0.4f;
    [SerializeField] public float responseDuration = 1.1f;
    [FormerlySerializedAs("trialNumOneBlock")]
    [SerializeField] public float blockDurationSeconds = 120.0f;

    // ヒエラルキー上に配置したInputFieldからStroopの時間設定を変更する。
    [Header("Stroop setting input UI")]
    [SerializeField] private TMP_InputField fixationDurationInputField;
    [SerializeField] private TMP_InputField stimulusDurationInputField;
    [SerializeField] private TMP_InputField responseDurationInputField;
    [FormerlySerializedAs("trialNumOneBlockInputField")]
    [SerializeField] private TMP_InputField blockDurationInputField;
    [SerializeField] private XRNumericKeyboardInputBinder numericKeyboardInputBinder;
    [SerializeField] private bool applySettingsOnEndEdit = true;
    [SerializeField] private float minFixationDuration = 0.0f;
    [SerializeField] private float minStimulusDuration = 0.0f;
    [SerializeField] private float minResponseDuration = 0.0f;
    [FormerlySerializedAs("minTrialNumOneBlock")]
    [SerializeField] private float minBlockDurationSeconds = 1.0f;

    // StroopColorの列挙順と各配列の順序を一致させる。
    private static readonly string[] ColorWords = { "あか", "あお", "みどり", "きいろ" };

    private static readonly Color[] DisplayColors =
    {
        new Color32(255, 0, 0, 255),
        new Color32(0, 0, 255, 255),
        new Color32(0, 130, 0, 255),
        new Color32(255, 255, 0, 255)
    };

    private readonly List<StroopTrial> trials = new List<StroopTrial>();

    private TrialState state = TrialState.Idle;
    private StroopColor? pendingResponse;
    private int correctCount;
    private string currentBlockName;
    private string activeStroopStatusName;
    private string activeStroopBlockId;
    private bool isStroopStatusActive;

    [SerializeField] private InputActionAsset inputActions;

    private InputAction buttonA;
    private InputAction buttonB;
    private InputAction buttonX;
    private InputAction buttonY;

    private void Awake()
    {
        // Inspectorで参照が設定されていない場合にも送信できるようにする。
        // RequestSenderは既存シーンではEventSystemに付いているため、GameObject名から取得する。
        if (requestSender == null)
        {
            GameObject eventSystem = GameObject.Find("EventSystem");
            if (eventSystem != null)
            {
                requestSender = eventSystem.GetComponent<RequestSender>();
            }
        }

        // RequestSenderと同じ被験者IDをログに使用する。
        if (requestSender != null && !string.IsNullOrWhiteSpace(requestSender.userId))
        {
            userId = requestSender.userId;
        }

        stimulusText.richText = false;
        instructionText.richText = false;
        statusText.richText = false;
        resultText.richText = false;

        // 回答キーの対応は全条件で共通。
        instructionText.text = "A: あか / B: あお / X: みどり / Y: きいろ";
        ShowIdle();
        SetupSettingInputFields();

        var map = inputActions.FindActionMap("XRControllerInput");

        buttonA = map.FindAction("Button_A");
        buttonB = map.FindAction("Button_B");
        buttonX = map.FindAction("Button_X");
        buttonY = map.FindAction("Button_Y");
    }

    private void OnEnable()
    {
        buttonA?.Enable();
        buttonB?.Enable();
        buttonX?.Enable();
        buttonY?.Enable();
    }

    private void OnDisable()
    {
        buttonA?.Disable();
        buttonB?.Disable();
        buttonX?.Disable();
        buttonY?.Disable();
    }

    private void Update()
    {
        // 回答受付中はブロック開始入力を受け付けない。
        if (state == TrialState.Response)
        {
            StroopColor response;
            if (TryGetColorResponse(out response))
            {
                pendingResponse = response;
            }
            return;
        }

        if (state != TrialState.Idle && state != TrialState.Rest && state != TrialState.Results)
        {
            return;
        }
    }

    // 事前練習パートの開始
    public void StartPractice()
    {
        if (!CanStartBlock())
        {
            return;
        }

        // Practiceは各条件10試行を条件ごとのまとまりとして実施する。
        trials.Clear();
        AddTrials(StroopCondition.Congruent, 10, true);
        AddTrials(StroopCondition.Neutral, 10, true);
        AddTrials(StroopCondition.Incongruent, 10, true);

        currentBlockName = "Practice";
        SendStroopStartStatus("practice", "practice");
        StartCoroutine(RunTrials());
    }

    // 一致試行の開始
    public void StartCongruentBlock()
    {
        StartBlock(StroopCondition.Congruent, 1);
    }

    // 中立試行の開始
    public void StartNeutralBlock()
    {
        StartBlock(StroopCondition.Neutral, 2);
    }

    // 不一致試行の開始
    public void StartIncongruentBlock()
    {
        StartBlock(StroopCondition.Incongruent, 3);
    }

    // 休憩中画面
    public void ShowRest()
    {
        SendStroopEndStatus();
        StopAllCoroutines();

        state = TrialState.Rest;
        stimulusText.text = " ";
        resultText.text = " ";
        statusText.text = "Rest中\n開始キーまたはUIボタンで次のブロックを開始";
    }

    // ブロックの開始　各試行で共通利用する原型
    private void StartBlock(StroopCondition condition, int blockNumber)
    {
        // Idle(待機), Rest(休憩), Result(結果表示)の場合なら，ブロックを開始できる
        if (!CanStartBlock())
        {
            return;
        }

        // 本試行は同一条件を、設定した制限時間が経過するまで繰り返す。
        trials.Clear();

        currentBlockName = "Block " + blockNumber;
        SendStroopStartStatus(condition.ToString(), blockNumber.ToString(CultureInfo.InvariantCulture));
        StartCoroutine(RunTimedBlock(condition));
    }

    // Idle(待機), Rest(休憩), Result(結果表示)のどれかならTrue
    private bool CanStartBlock()
    {
        return state == TrialState.Idle || state == TrialState.Rest || state == TrialState.Results;
    }

    // Stroop課題の開始フラグをサーバに送信する。
    private void SendStroopStartStatus(string statusName, string blockId)
    {
        activeStroopStatusName = statusName;
        activeStroopBlockId = blockId;
        isStroopStatusActive = true;

        if (requestSender != null)
        {
            requestSender.SendStroopStartFlag(activeStroopStatusName, activeStroopBlockId);
        }
    }

    // Stroop課題の終了フラグをサーバに送信する。
    private void SendStroopEndStatus()
    {
        if (!isStroopStatusActive)
        {
            return;
        }

        if (requestSender != null)
        {
            requestSender.SendStroopEndFlag(activeStroopStatusName, activeStroopBlockId);
        }

        isStroopStatusActive = false;
        activeStroopStatusName = null;
        activeStroopBlockId = null;
    }

    // 試行の追加．
    private void AddTrials(StroopCondition condition, int count, bool isPractice)
    {
        for (int i = 0; i < count; i++)
        {
            trials.Add(new StroopTrial(condition, i + 1, isPractice));
        }
    }

    // ブロック内の全試行の実行と結果表示
    private IEnumerator RunTrials()
    {
        correctCount = 0;
        resultText.text = " ";

        // ブロック内の試行を順番に実行する。
        for (int i = 0; i < trials.Count; i++)
        {
            yield return RunTrial(trials[i], i + 1);
        }

        // 結果の表示
        state = TrialState.Results;
        stimulusText.text = " ";
        statusText.text = currentBlockName + " 完了";
        resultText.text = string.Format("出題数: {0}\n正答数: {1}", trials.Count, correctCount);
        SendStroopEndStatus();
    }

    // 本試行用。制限時間内は同じ条件の試行を繰り返す。
    // 制限時間中に開始した試行は最後まで実行し、その終了後にブロックを終了する。
    private IEnumerator RunTimedBlock(StroopCondition condition)
    {
        correctCount = 0;
        resultText.text = " ";

        float blockStartTime = Time.realtimeSinceStartup;
        int completedTrialCount = 0;

        while (Time.realtimeSinceStartup - blockStartTime < blockDurationSeconds)
        {
            int trialIndex = completedTrialCount + 1;
            StroopTrial trial = new StroopTrial(condition, trialIndex, false);
            trials.Add(trial);

            yield return RunTrial(trial, trialIndex, false);
            completedTrialCount++;
        }

        state = TrialState.Results;
        stimulusText.text = " ";
        statusText.text = currentBlockName + " 完了";
        resultText.text = string.Format(
            "経過時間: {0:0.0} 秒\n出題数: {1}\n正答数: {2}",
            Time.realtimeSinceStartup - blockStartTime,
            completedTrialCount,
            correctCount);
        SendStroopEndStatus();
    }

    // 1トライアルの実行
    private IEnumerator RunTrial(
        StroopTrial trial,
        int blockTrialIndex,
        bool showTotalTrialCount = true)
    {
        // 試行開始時に条件に合う刺激を1つ生成する。
        Stimulus stimulus = CreateStimulus(trial.condition);

        statusText.text = showTotalTrialCount
            ? string.Format(
                "{0} / {1}\nTrial {2} / {3}",
                currentBlockName,
                trial.condition,
                blockTrialIndex,
                trials.Count)
            : string.Format(
                "{0} / {1}\nTrial {2}\nTime limit: {3:0.###} sec",
                currentBlockName,
                trial.condition,
                blockTrialIndex,
                blockDurationSeconds);

        // 1. 注視点を1000 ms表示。
        state = TrialState.Fixation;
        pendingResponse = null;
        stimulusText.color = Color.black;
        stimulusText.text = "+";
        yield return new WaitForSecondsRealtime(fixationDuration);

        // 2. Stroop刺激を400 ms表示し、反応時間の計測を開始。
        //    変更点：刺激表示中も回答を受け付ける。
        state = TrialState.Stimulus;
        pendingResponse = null;

        stimulusText.color = DisplayColors[(int)stimulus.color];
        stimulusText.text = stimulus.text;

        string onsetTime = DateTime.UtcNow.ToString("o");
        float onsetRealtime = Time.realtimeSinceStartup;

        while (!pendingResponse.HasValue &&
               Time.realtimeSinceStartup - onsetRealtime < stimulusDuration)
        {
            StroopColor response;

            if (TryGetColorResponse(out response))
            {
                pendingResponse = response;
                break;
            }

            yield return null;
        }

        // 3. 400 ms経過後に刺激を消す。
        stimulusText.text = " ";

        // 4. 刺激表示中に回答されていない場合のみ，刺激を消した状態で最大1100 ms回答を受け付ける。
        if (!pendingResponse.HasValue)
        {
            state = TrialState.Response;

            float responseWindowStart = Time.realtimeSinceStartup;

            while (!pendingResponse.HasValue &&
                   Time.realtimeSinceStartup - responseWindowStart < responseDuration)
            {
                StroopColor response;

                if (TryGetColorResponse(out response))
                {
                    pendingResponse = response;
                    break;
                }

                yield return null;
            }
        }

        // 5. 回答の有無と表示色との一致から結果を判定する。
        bool answered = pendingResponse.HasValue; // 回答したか
        bool isCorrect = answered && pendingResponse.Value == stimulus.color; // 回答して，かつ正解したか

        float reactionTimeMs = answered
            ? (Time.realtimeSinceStartup - onsetRealtime) * 1000.0f  // 回答してたら，回答時間を記録
            : (stimulusDuration + responseDuration) * 1000.0f;       // 回答できてなかったら，刺激提示と受付時間を足した時間を入れる

        string responseTime = answered ? DateTime.UtcNow.ToString("o") : " "; // 回答時刻
        string result = !answered ? "Timeout" : isCorrect ? "Correct" : "Wrong"; // 回答できてなかったら，Timeout，回答してたら，Correct（正解）またはWrong（不正解）

        // 正解してたらカウントアップ
        if (isCorrect)
        {
            correctCount++;
        }

        // ログ送信は保留中。上で算出した値をRequestSender連携時に使用する。
        Debug.Log(string.Format(
            "user_id={0}, condition={1}, trial_index={2}, is_practice={3}, is_correct={4}, reaction_time_ms={5}, stimulus_onset_time={6}, response_time={7}, result={8}",
            userId,
            trial.condition,
            trial.trialIndex,
            trial.isPractice,
            isCorrect,
            reactionTimeMs,
            onsetTime,
            responseTime,
            result));

        // 1試行終了ごとに、設計書で指定されたログ項目をサーバへ送信する。
        if (requestSender != null)
        {
            requestSender.SendStroopLog(
                userId,
                trial.condition.ToString(),
                trial.trialIndex,
                trial.isPractice,
                isCorrect,
                reactionTimeMs,
                onsetTime,
                responseTime,
                result);
        }
        else
        {
            Debug.LogWarning("Stroop log was not sent because RequestSender was not found.");
        }
    }

    // 刺激
    private Stimulus CreateStimulus(StroopCondition condition)
    {
        // 表示色は4色から均等な確率で選ぶ。
        StroopColor displayColor = (StroopColor)UnityEngine.Random.Range(0, DisplayColors.Length);
        string text;

        switch (condition)
        {
            case StroopCondition.Neutral:
                // Neutralでは色名を提示せず、文字列をXXXXに固定する。
                text = "XXXX";
                break;

            case StroopCondition.Incongruent:
                // 表示色と異なる色名だけを候補にして、その中から選ぶ。
                List<int> incongruentWordIndexes = new List<int>();

                for (int i = 0; i < ColorWords.Length; i++)
                {
                    if (i != (int)displayColor)
                    {
                        incongruentWordIndexes.Add(i);
                    }
                }

                int wordIndex = incongruentWordIndexes[
                    UnityEngine.Random.Range(0, incongruentWordIndexes.Count)];

                text = ColorWords[wordIndex];
                break;

            default:
                // Congruentでは表示色と同じ色名を提示する。
                text = ColorWords[(int)displayColor];
                break;
        }

        return new Stimulus
        {
            text = text,
            color = displayColor
        };
    }

    //ここ変更した　かつて無駄に時間をかけて定義したボタン割り当ての遺産をつかう
    private bool TryGetColorResponse(out StroopColor response)
    {
        Keyboard keyboard = Keyboard.current;

        // キーボードのZ/X/C/VとXRコントローラのA/B/X/Yを同じ色へ割り当てる。
        if ((keyboard != null && keyboard.zKey.wasPressedThisFrame) ||
            (buttonA != null && buttonA.WasPressedThisFrame()))
        {
            response = StroopColor.Red;
            return true;
        }

        if ((keyboard != null && keyboard.xKey.wasPressedThisFrame) ||
            (buttonB != null && buttonB.WasPressedThisFrame()))
        {
            response = StroopColor.Blue;
            return true;
        }

        if ((keyboard != null && keyboard.cKey.wasPressedThisFrame) ||
            (buttonX != null && buttonX.WasPressedThisFrame()))
        {
            response = StroopColor.Green;
            return true;
        }

        if ((keyboard != null && keyboard.vKey.wasPressedThisFrame) ||
            (buttonY != null && buttonY.WasPressedThisFrame()))
        {
            response = StroopColor.Yellow;
            return true;
        }

        response = default(StroopColor);
        return false;
    }

    public void SetFixationDuration(string inputText)
    {
        if (!TryParseFloat(inputText, out float seconds))
        {
            Debug.LogWarning($"Fixation duration input is invalid: {inputText}");
            SyncSettingInputFields();
            return;
        }

        fixationDuration = Mathf.Max(minFixationDuration, seconds);
        fixationDurationInputField?.SetTextWithoutNotify(FormatFloat(fixationDuration));
    }

    public void SetStimulusDuration(string inputText)
    {
        if (!TryParseFloat(inputText, out float seconds))
        {
            Debug.LogWarning($"Stimulus duration input is invalid: {inputText}");
            SyncSettingInputFields();
            return;
        }

        stimulusDuration = Mathf.Max(minStimulusDuration, seconds);
        stimulusDurationInputField?.SetTextWithoutNotify(FormatFloat(stimulusDuration));
    }

    public void SetResponseDuration(string inputText)
    {
        if (!TryParseFloat(inputText, out float seconds))
        {
            Debug.LogWarning($"Response duration input is invalid: {inputText}");
            SyncSettingInputFields();
            return;
        }

        responseDuration = Mathf.Max(minResponseDuration, seconds);
        responseDurationInputField?.SetTextWithoutNotify(FormatFloat(responseDuration));
    }

    public void SetBlockDuration(string inputText)
    {
        if (!TryParseFloat(inputText, out float seconds))
        {
            Debug.LogWarning($"Block duration input is invalid: {inputText}");
            SyncSettingInputFields();
            return;
        }

        blockDurationSeconds = Mathf.Max(minBlockDurationSeconds, seconds);
        blockDurationInputField?.SetTextWithoutNotify(FormatFloat(blockDurationSeconds));
    }

    private void SetupSettingInputFields()
    {
        // Inspector値も入力欄と同じ制約に合わせてから表示する。
        fixationDuration = Mathf.Max(minFixationDuration, fixationDuration);
        stimulusDuration = Mathf.Max(minStimulusDuration, stimulusDuration);
        responseDuration = Mathf.Max(minResponseDuration, responseDuration);
        blockDurationSeconds = Mathf.Max(minBlockDurationSeconds, blockDurationSeconds);
        SyncSettingInputFields();

        if (numericKeyboardInputBinder == null)
        {
            numericKeyboardInputBinder = GetComponent<XRNumericKeyboardInputBinder>();
        }

        if (numericKeyboardInputBinder == null)
        {
            numericKeyboardInputBinder = gameObject.AddComponent<XRNumericKeyboardInputBinder>();
        }

        // すべて秒数として小数入力可能な共通キーボードへ登録する。
        numericKeyboardInputBinder.BindDecimal(fixationDurationInputField, SetFixationDuration);
        numericKeyboardInputBinder.BindDecimal(stimulusDurationInputField, SetStimulusDuration);
        numericKeyboardInputBinder.BindDecimal(responseDurationInputField, SetResponseDuration);
        numericKeyboardInputBinder.BindDecimal(blockDurationInputField, SetBlockDuration);

        if (!applySettingsOnEndEdit)
        {
            return;
        }

        fixationDurationInputField?.onEndEdit.AddListener(SetFixationDuration);
        stimulusDurationInputField?.onEndEdit.AddListener(SetStimulusDuration);
        responseDurationInputField?.onEndEdit.AddListener(SetResponseDuration);
        blockDurationInputField?.onEndEdit.AddListener(SetBlockDuration);
    }

    private void SyncSettingInputFields()
    {
        fixationDurationInputField?.SetTextWithoutNotify(FormatFloat(fixationDuration));
        stimulusDurationInputField?.SetTextWithoutNotify(FormatFloat(stimulusDuration));
        responseDurationInputField?.SetTextWithoutNotify(FormatFloat(responseDuration));
        blockDurationInputField?.SetTextWithoutNotify(FormatFloat(blockDurationSeconds));
    }

    private bool TryParseFloat(string inputText, out float value)
    {
        if (float.TryParse(inputText, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        return float.TryParse(inputText, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void OnDestroy()
    {
        fixationDurationInputField?.onEndEdit.RemoveListener(SetFixationDuration);
        stimulusDurationInputField?.onEndEdit.RemoveListener(SetStimulusDuration);
        responseDurationInputField?.onEndEdit.RemoveListener(SetResponseDuration);
        blockDurationInputField?.onEndEdit.RemoveListener(SetBlockDuration);

        if (numericKeyboardInputBinder != null)
        {
            numericKeyboardInputBinder.Unbind(fixationDurationInputField);
            numericKeyboardInputBinder.Unbind(stimulusDurationInputField);
            numericKeyboardInputBinder.Unbind(responseDurationInputField);
            numericKeyboardInputBinder.Unbind(blockDurationInputField);
        }
    }

    private void ShowIdle()
    {
        state = TrialState.Idle;
        stimulusText.text = " ";
        resultText.text = " ";

        statusText.text =
            "P / LB: Practice\n" +
            "1 / RB: Block 1 (Congruent)\n" +
            "2 / Back: Block 2 (Neutral)\n" +
            "3 / Start: Block 3 (Incongruent)";
    }
}
