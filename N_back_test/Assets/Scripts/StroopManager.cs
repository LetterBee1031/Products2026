using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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

    // ここシーン上からいじれるように 
    [Header("Timing (seconds)")]
    [SerializeField] public float fixationDuration = 1.0f;
    [SerializeField] public float stimulusDuration = 0.4f;
    [SerializeField] public float responseDuration = 1.1f;
    [SerializeField] public int trialNumOneBlock = 30;

    // [Header("Block start keys")]
    // ここシーン上のボタンに変更
    // [SerializeField] private KeyCode practiceKey = KeyCode.P;
    // [SerializeField] private KeyCode congruentBlockKey = KeyCode.Alpha1;
    // [SerializeField] private KeyCode neutralBlockKey = KeyCode.Alpha2;
    // [SerializeField] private KeyCode incongruentBlockKey = KeyCode.Alpha3;

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


    [SerializeField] private InputActionAsset inputActions;

    private InputAction buttonA;
    private InputAction buttonB;
    private InputAction buttonX;
    private InputAction buttonY;

    private void Awake()
    {
        // 回答キーの対応は全条件で共通。
        instructionText.text = "A: 赤 / B: 青 / X: 緑 / Y: 黄";
        ShowIdle();

        var map = inputActions.FindActionMap("XRControllerInput");

        buttonA = map.FindAction("Button_A");
        buttonB = map.FindAction("Button_B");
        buttonX = map.FindAction("Button_X");
        buttonY = map.FindAction("Button_Y");
    }

    private void OnEnable()
    {
        buttonA.Enable();
        buttonB.Enable();
        buttonX.Enable();
        buttonY.Enable();
    }

    private void OnDisable()
    {
        buttonA.Disable();
        buttonB.Disable();
        buttonX.Disable();
        buttonY.Disable();
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


        // // JoystickButton 4～7は一般的なゲームパッドの肩・メニューボタン。
        // if (Input.GetKeyDown(practiceKey) || Input.GetKeyDown(KeyCode.JoystickButton4))
        // {
        //     StartPractice();
        // }
        // else if (Input.GetKeyDown(congruentBlockKey) || Input.GetKeyDown(KeyCode.JoystickButton5))
        // {
        //     StartBlock(StroopCondition.Congruent, 1);
        // }
        // else if (Input.GetKeyDown(neutralBlockKey) || Input.GetKeyDown(KeyCode.JoystickButton6))
        // {
        //     StartBlock(StroopCondition.Neutral, 2);
        // }
        // else if (Input.GetKeyDown(incongruentBlockKey) || Input.GetKeyDown(KeyCode.JoystickButton7))
        // {
        //     StartBlock(StroopCondition.Incongruent, 3);
        // }
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
        StopAllCoroutines();
        state = TrialState.Rest;
        stimulusText.text = string.Empty;
        resultText.text = string.Empty;
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

        // 本試行は1ブロックにつき同一条件を30(trialNumOneBlock)試行実施する。
        trials.Clear();
        AddTrials(condition, trialNumOneBlock, false);
        currentBlockName = "Block " + blockNumber;
        StartCoroutine(RunTrials());
    }

    // Idle(待機), Rest(休憩), Result(結果表示)のどれかならTrue
    private bool CanStartBlock()
    {
        return state == TrialState.Idle || state == TrialState.Rest || state == TrialState.Results;
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
        resultText.text = string.Empty;

        // ブロック内の試行を順番に実行する。
        for (int i = 0; i < trials.Count; i++)
        {
            yield return RunTrial(trials[i], i + 1);
        }

        // 結果の表示
        state = TrialState.Results;
        stimulusText.text = string.Empty;
        statusText.text = currentBlockName + " 完了";
        resultText.text = string.Format("出題数: {0}\n正答数: {1}", trials.Count, correctCount);
    }

    // 1トライアルの実行
    private IEnumerator RunTrial(StroopTrial trial, int blockTrialIndex)
    {
        // 試行開始時に条件に合う刺激を1つ生成する。
        Stimulus stimulus = CreateStimulus(trial.condition);
        statusText.text = string.Format(
            "{0} / {1}\nTrial {2} / {3}",
            currentBlockName,
            trial.condition,
            blockTrialIndex,
            trials.Count);

        // 1. 注視点を1000 ms表示。
        state = TrialState.Fixation;
        stimulusText.color = Color.black;
        stimulusText.text = "+";
        yield return new WaitForSecondsRealtime(fixationDuration);

        // 2. Stroop刺激を400 ms表示し、反応時間の計測を開始。
        state = TrialState.Stimulus;
        pendingResponse = null;
        stimulusText.color = DisplayColors[(int)stimulus.color];
        stimulusText.text = stimulus.text;
        string onsetTime = DateTime.UtcNow.ToString("o");
        float onsetRealtime = Time.realtimeSinceStartup;
        yield return new WaitForSecondsRealtime(stimulusDuration);

        // 3. 刺激を消した状態で最大1100 ms回答を受け付ける。
        state = TrialState.Response;
        stimulusText.text = string.Empty;
        // pendingResponse = null;

        float responseWindowStart = Time.realtimeSinceStartup;
        while (!pendingResponse.HasValue &&
               Time.realtimeSinceStartup - responseWindowStart < responseDuration)
        {
            yield return null;
        }

        // 4. 回答の有無と表示色との一致から結果を判定する。
        bool answered = pendingResponse.HasValue; // 回答したか
        bool isCorrect = answered && pendingResponse.Value == stimulus.color; // 回答して，かつ正解したか
        float reactionTimeMs = answered
            ? (Time.realtimeSinceStartup - onsetRealtime) * 1000.0f  // 回答してたら，回答時間を記録
            : (stimulusDuration + responseDuration) * 1000.0f;  // 回答できてなかったら，刺激提示と受付時間を足した時間を入れる
        string responseTime = answered ? DateTime.UtcNow.ToString("o") : string.Empty; // 回答時刻
        string result = !answered ? "Timeout" : isCorrect ? "Correct" : "Wrong"; // 回答できてなかったら，Timeout，回答してたら，Correct（正解）またはWrong（不正解）
        
        // 正解してたらカウントアップ
        if (isCorrect)
        {
            correctCount++;
        }

        // ログ送信は保留中。上で算出した値をRequestSender連携時に使用する。
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

        return new Stimulus { text = text, color = displayColor };
    }

    //ここ変更した　かつて無駄に時間をかけて定義したボタン割り当ての遺産をつかう
    private bool TryGetColorResponse(out StroopColor response)
    {
        // キーボードのA/B/X/Yとゲームパッドの同名ボタンを同じ色へ割り当てる。
        if (Input.GetKeyDown(KeyCode.Z) || buttonA.WasPressedThisFrame())
        {
            response = StroopColor.Red;
            return true;
        }
        if (Input.GetKeyDown(KeyCode.X) || buttonB.WasPressedThisFrame())
        {
            response = StroopColor.Blue;
            return true;
        }
        if (Input.GetKeyDown(KeyCode.C) || buttonX.WasPressedThisFrame())
        {
            response = StroopColor.Green;
            return true;
        }
        if (Input.GetKeyDown(KeyCode.V) || buttonY.WasPressedThisFrame())
        {
            response = StroopColor.Yellow;
            return true;
        }

        response = default(StroopColor);
        return false;
    }

    private void ShowIdle()
    {
        state = TrialState.Idle;
        stimulusText.text = string.Empty;
        resultText.text = string.Empty;
        statusText.text =
            "P / LB: Practice\n" +
            "1 / RB: Block 1 (Congruent)\n" +
            "2 / Back: Block 2 (Neutral)\n" +
            "3 / Start: Block 3 (Incongruent)";
    }
}
