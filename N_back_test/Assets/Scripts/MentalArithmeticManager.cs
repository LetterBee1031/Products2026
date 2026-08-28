using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MentalArithmeticManager : MonoBehaviour
{
    // 問題の難易度。積の範囲によって各問題を分類する。
    private enum Difficulty
    {
        Low,
        Medium,
        High
    }

    // 多重入力やフィードバック表示中の回答を防ぐための進行状態。
    private enum TaskState
    {
        Idle,
        Running,
        Feedback,
        Complete
    }

    // 画面に表示する掛け算問題。
    // a <= b の組み合わせだけを生成し、左右を入れ替えた重複問題を除外する。
    private struct Problem
    {
        public int a;
        public int b;

        public int Answer => a * b;
    }

    // 1回答ごとにCSVへ保存するデータ。
    // 各フィールドは設計書のログ項目に対応している。
    [Serializable]
    private class TrialLog
    {
        public string participantId; // 被験者ID
        public int blockId; // ブロック番号
        public string difficulty; // 難しさ　Low/Medium/High
        public int blockDurationSec; // 設定時間
        public int trialIndex; // 試行番号
        public int a; // 一個目の数値
        public int b; // 二個目の数値
        public int correctAnswer; // 正答
        public string userAnswer; // 入力値
        public bool isCorrect; // 正誤
        public bool isSkipped; // スキップしたか
        public float reactionTimeMs; // 問題表示から解答確定
        public float blockElapsedTimeMs; // ブロック経過時間
        public string timestamp; // 記録時間
    }

    [Header("UI")]
    [SerializeField] private TMP_Text problemText;  // 問題表示用テキスト　例） 7 × 12
    [SerializeField] private TMP_InputField answerInputField;  // 解答入力のフィールド
    [SerializeField] private Button submitButton; // 解答ボタン
    [SerializeField] private Button skipButton; // スキップボタン
    [SerializeField] private TMP_Text timerText; // 経過時間とか
    [SerializeField] private TMP_Text difficultyText; // 難易度のテキスト Low/Medium/High
    [SerializeField] private TMP_Text feedbackText; // 〇×

    [Header("Settings")]
    [SerializeField] private TMP_InputField durationInputField; //制限時間のフィールド
    [SerializeField] private int blockDurationSeconds = 120; // 制限時間
    [SerializeField] private float feedbackDurationSeconds = 0.5f; // 〇×表示時間
    [SerializeField] private string participantId = "test_user";
    [SerializeField] private XRNumericKeyboardInputBinder numericKeyboardInputBinder;

    [Header("Server")]
    [SerializeField] private RequestSender requestSender;
    [SerializeField] private NasaTlxManager nasaTlxManager;

    private const int DefaultDurationSeconds = 120; 
    private const int MinDurationSeconds = 30;
    private const int MaxDurationSeconds = 600;
    private const int MaxAnswerDigits = 3;

    // 難易度ごとに、条件を満たすすべての問題を保持する。
    private readonly Dictionary<Difficulty, List<Problem>> problemPools =
        new Dictionary<Difficulty, List<Problem>>();

    // タスク開始から終了までの回答履歴。
    private readonly List<TrialLog> trialLogs = new List<TrialLog>();

    private TaskState state = TaskState.Idle;  // 状態
    private Coroutine taskCoroutine; // 
    private Problem currentProblem; // 現在の問題
    private Difficulty currentDifficulty; // 課題難易度
    private int currentBlockId; // 今のブロックのID 数値だから何ブロック目かとかそういう感じかと
    private int currentTrialIndex; // ブロック内で何問目かを出力する
    private int currentProblemIndex; // 問題リストから問題を取り出す位置の指定の変数
    private int blockCompletedTrialCount; // 現在のブロックで回答またはスキップした試行数
    private int blockCorrectCount; // 現在のブロックの正答数
    private float blockStartRealtime; // ブロックの開始時刻の記録
    private float problemStartRealtime; // その問題の開始時刻の記録
    private string csvFilePath;
    // 古いフィードバック用コルーチンが次のブロックへ干渉するのを防ぐ識別子。
    private int blockRunToken;
    private bool blockActive;
    private string activeMentalArithmeticStatusName;
    private string activeMentalArithmeticBlockId;
    private bool isMentalArithmeticStatusActive;
    private string pendingNasaTlxBlockId;

    private void Awake()
    {
        // Inspectorで未設定の場合は、既存シーンと同じEventSystemから取得する。
        if (requestSender == null)
        {
            GameObject eventSystem = GameObject.Find("EventSystem");
            if (eventSystem != null)
            {
                requestSender = eventSystem.GetComponent<RequestSender>();
                nasaTlxManager = eventSystem.GetComponent<NasaTlxManager>();
            }
        }

        if (nasaTlxManager == null)
        {
            nasaTlxManager = FindFirstObjectByType<NasaTlxManager>();
        }

        // RequestSenderに設定された被験者IDを暗算ログでも共通利用する。
        if (requestSender != null && !string.IsNullOrWhiteSpace(requestSender.userId))
        {
            participantId = requestSender.userId;
        }

        // 起動時に問題候補を作成し、UIイベントとXR数値キーボードを接続する。
        BuildProblemPools(); // 問題候補集合を作成
        SetupInputFields(); // 入力フィールドに関する設定

        submitButton?.onClick.AddListener(SubmitAnswer); // 恐らく，提出ボタン押下検知用のリスナー
        skipButton?.onClick.AddListener(SkipProblem); // 恐らく，スキップボタン押下検知用のリスナー

        // 初期化とか
        SetControlsInteractable(false); 
        // problemText.text = "Press a start button.";
        timerText.text = string.Empty;
        difficultyText.text = string.Empty;
        feedbackText.text = string.Empty;
    }

    private void Update()
    {
        // フィードバック中もブロック時間は進むため、stateではなく
        // blockActiveを基準に残り時間を更新する。
        if (!blockActive)
        {
            return;
        }

        // 残り時間の出力
        float remaining = Mathf.Max(
            0.0f,
            blockDurationSeconds - (Time.realtimeSinceStartup - blockStartRealtime));
        timerText.text = $"Remaining: {Mathf.CeilToInt(remaining)} sec";
    }

    // Lowブロック開始ボタンのOnClickへ割り当てる。
    public void StartLowBlock()
    {
        StartBlock(Difficulty.Low, 1);
    }

    // Mediumブロック開始ボタンのOnClickへ割り当てる。
    public void StartMediumBlock()
    {
        StartBlock(Difficulty.Medium, 2);
    }

    // Highブロック開始ボタンのOnClickへ割り当てる。
    public void StartHighBlock()
    {
        StartBlock(Difficulty.High, 3);
    }

    public void SubmitAnswer()
    {
        // 回答受付中以外のボタン押下やEnter入力は無視する。
        if (state != TaskState.Running)
        {
            return;
        }

        // 未入力や整数以外の入力では判定せず、同じ問題を表示し続ける。
        string input = answerInputField != null ? answerInputField.text.Trim() : string.Empty;
        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int answer))
        {
            return;
        }

        // 正解値は画面に表示せず、正誤記号だけを500ms表示する。
        bool isCorrect = answer == currentProblem.Answer;
        RecordTrial(answer.ToString(CultureInfo.InvariantCulture), isCorrect, false);
        StartCoroutine(ShowFeedbackAndContinue(
            isCorrect ? "\u25CB" : "\u00D7",
            blockRunToken));
    }

    public void InputAnswerDigit(int digit)
    {
        if (state != TaskState.Running ||
            answerInputField == null ||
            digit < 0 ||
            digit > 9)
        {
            return;
        }

        string currentInput = answerInputField.text;
        if (currentInput.Length >= MaxAnswerDigits)
        {
            return;
        }

        string digitText = digit.ToString(CultureInfo.InvariantCulture);
        string nextInput = currentInput == "0" ? digitText : currentInput + digitText;
        answerInputField.SetTextWithoutNotify(nextInput);
    }

    public void ClearAnswerInput()
    {
        if (state == TaskState.Running)
        {
            answerInputField?.SetTextWithoutNotify(string.Empty);
        }
    }

    public void SkipProblem()
    {
        // スキップは不正解として記録し、user_answerは空欄にする。
        if (state != TaskState.Running)
        {
            return;
        }

        RecordTrial(string.Empty, false, true);
        StartCoroutine(ShowFeedbackAndContinue("SKIP", blockRunToken));
    }

    public void SetBlockDuration(string inputText)
    {
        // 設計書の範囲外、または整数に変換できない値は既定値120秒へ戻す。
        if (!int.TryParse(inputText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) ||
            seconds < MinDurationSeconds ||
            seconds > MaxDurationSeconds)
        {
            blockDurationSeconds = DefaultDurationSeconds;
        }
        else
        {
            blockDurationSeconds = seconds;
        }

        durationInputField?.SetTextWithoutNotify(
            blockDurationSeconds.ToString(CultureInfo.InvariantCulture));
    }

    // StroopManager.StartBlockと同様に、各開始ボタンから共通利用する開始処理。
    private void StartBlock(Difficulty difficulty, int blockId)
    {
        if (!CanStart())
        {
            return;
        }

        pendingNasaTlxBlockId = null;
        StartTaskCoroutine(RunSelectedBlock(difficulty, blockId));
    }

    private void StartTaskCoroutine(IEnumerator routine)
    {
        // 開始時点の入力値を確定し、今回の実験専用CSVファイル名を作る。
        SetBlockDuration(durationInputField != null
            ? durationInputField.text
            : blockDurationSeconds.ToString(CultureInfo.InvariantCulture));

        trialLogs.Clear();
        csvFilePath = CreateCsvFilePath();
        taskCoroutine = StartCoroutine(routine);
    }

    // 選択された1ブロックだけを実行し、終了後は次のボタン入力を待つ。
    private IEnumerator RunSelectedBlock(Difficulty difficulty, int blockId)
    {
        yield return RunBlock(difficulty, blockId);
        CompleteTask();
    }

    private IEnumerator RunBlock(Difficulty difficulty, int blockId)
    {
        // ブロックごとに試行番号と問題リストの読み出し位置をリセットする。
        currentDifficulty = difficulty;
        currentBlockId = blockId;
        currentTrialIndex = 0;
        currentProblemIndex = 0;
        blockCompletedTrialCount = 0;
        blockCorrectCount = 0;
        blockRunToken++;
        blockActive = true;
        SendMentalArithmeticStartStatus(
            difficulty.ToString(),
            "mental_arithmetic_" + blockId.ToString(CultureInfo.InvariantCulture));

        // 同じ難易度でも毎回異なる順番になるよう、開始時にシャッフルする。
        Shuffle(problemPools[difficulty]);

        blockStartRealtime = Time.realtimeSinceStartup;
        difficultyText.text = difficulty.ToString();
        feedbackText.text = string.Empty;
        SetControlsInteractable(true);

        ShowNextProblem();

        // 1問ごとの制限時間は設けず、ブロック全体の経過時間だけを監視する。
        while (Time.realtimeSinceStartup - blockStartRealtime < blockDurationSeconds)
        {
            yield return null;
        }

        // 時間切れ時に表示中だった未回答問題はログへ記録しない。

        state = TaskState.Feedback;
        blockActive = false;
        SetControlsInteractable(false);
        SaveLogsToCsv();
        // problemText.text =
        //     $"Block {blockId} complete\n" +
        //     $"Correct: {blockCorrectCount} / {blockCompletedTrialCount}";
        problemText.text =$"{blockCorrectCount} / {blockCompletedTrialCount}";
        timerText.text = "Remaining: 0 sec";
        feedbackText.text = string.Empty;
    }

    private IEnumerator ShowFeedbackAndContinue(string feedback, int feedbackBlockToken)
    {
        // フィードバック中は回答欄とボタンを無効化し、二重回答を防止する。
        state = TaskState.Feedback;
        SetControlsInteractable(false);
        feedbackText.text = feedback;

        // Time.timeScaleの影響を受けないリアルタイムで500ms待機する。
        float feedbackEnd = Time.realtimeSinceStartup + feedbackDurationSeconds;
        while (Time.realtimeSinceStartup < feedbackEnd &&
               Time.realtimeSinceStartup - blockStartRealtime < blockDurationSeconds)
        {
            yield return null;
        }

        // 待機中にブロックが終了していなければ、入力欄を消して次問へ進む。
        // tokenにより、前ブロックで開始したコルーチンからの遷移も防止する。
        if (feedbackBlockToken == blockRunToken &&
            blockActive &&
            Time.realtimeSinceStartup - blockStartRealtime < blockDurationSeconds)
        {
            feedbackText.text = string.Empty;
            ShowNextProblem();
        }
    }

    private void ShowNextProblem()
    {
        List<Problem> pool = problemPools[currentDifficulty];

        // 全候補を一度ずつ提示した後は、再シャッフルして繰り返し使用する。
        if (currentProblemIndex >= pool.Count)
        {
            Problem previousProblem = currentProblem;
            Shuffle(pool);
            currentProblemIndex = 0;

            // 周回の境界でも直前と同じ問題が連続しないよう先頭を入れ替える。
            if (pool.Count > 1 &&
                pool[0].a == previousProblem.a &&
                pool[0].b == previousProblem.b)
            {
                Problem swap = pool[0];
                pool[0] = pool[1];
                pool[1] = swap;
            }
        }

        currentProblem = pool[currentProblemIndex++];
        currentTrialIndex++;

        // 反応時間は問題を表示した瞬間から回答またはスキップまでを計測する。
        problemStartRealtime = Time.realtimeSinceStartup;
        state = TaskState.Running;

        // 前問の回答を残さず、新しい問題にすぐ入力できる状態へ戻す。
        answerInputField?.SetTextWithoutNotify(string.Empty);
        problemText.text = $"{currentProblem.a} \u00D7 {currentProblem.b} = ";
        SetControlsInteractable(true);
    }

    ///

    private void RecordTrial(string userAnswer, bool isCorrect, bool isSkipped)
    {
        float now = Time.realtimeSinceStartup;

        blockCompletedTrialCount++;
        if (isCorrect)
        {
            blockCorrectCount++;
        }

        // 時間値は設計書に合わせてミリ秒へ変換する。
        // timestampは環境に依存しにくいUTCのISO 8601形式で記録する。
        TrialLog log = new TrialLog
        {
            participantId = participantId,
            blockId = currentBlockId,
            difficulty = currentDifficulty.ToString(),
            blockDurationSec = blockDurationSeconds,
            trialIndex = currentTrialIndex,
            a = currentProblem.a,
            b = currentProblem.b,
            correctAnswer = currentProblem.Answer,
            userAnswer = userAnswer,
            isCorrect = isCorrect,
            isSkipped = isSkipped,
            reactionTimeMs = (now - problemStartRealtime) * 1000.0f,
            blockElapsedTimeMs = (now - blockStartRealtime) * 1000.0f,
            timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };

        trialLogs.Add(log);
        SendLogToServer(log);
    }

    // ローカルCSV保存とは別に、1試行が完了するたびサーバへ送信する。
    private void SendLogToServer(TrialLog log)
    {
        if (requestSender == null)
        {
            Debug.LogWarning(
                "Mental arithmetic log was not sent because RequestSender was not found.");
            return;
        }

        requestSender.SendMentalArithmeticLog(
            log.participantId,
            log.blockId,
            log.difficulty,
            log.blockDurationSec,
            log.trialIndex,
            log.a,
            log.b,
            log.correctAnswer,
            log.userAnswer,
            log.isCorrect,
            log.isSkipped,
            log.reactionTimeMs,
            log.blockElapsedTimeMs,
            log.timestamp);
    }

    private void CompleteTask()
    {
        // 最終ブロック終了時にも保存し、最後の状態を確実にファイルへ反映する。
        SaveLogsToCsv();
        pendingNasaTlxBlockId = activeMentalArithmeticBlockId;
        SendMentalArithmeticEndStatus();
        state = TaskState.Complete;
        taskCoroutine = null;
        SetControlsInteractable(false);
        // problemText.text = "Mental arithmetic task complete";
        timerText.text = string.Empty;
        difficultyText.text = string.Empty;
        feedbackText.text = string.Empty;
        Debug.Log($"Mental arithmetic log saved: {csvFilePath}");
    }

    // 結果画面の遷移ボタンから呼び出し、完了した暗算ブロックのNASA-TLXを開始する
    public void MoveToNasaTlxQuestionnaire()
    {
        if (state != TaskState.Complete || string.IsNullOrWhiteSpace(pendingNasaTlxBlockId))
        {
            Debug.LogWarning(
                "MentalArithmeticManager: completed block for NASA-TLX is not available.");
            return;
        }

        if (nasaTlxManager == null)
        {
            Debug.LogError("MentalArithmeticManager: NasaTlxManager is not assigned.");
            return;
        }

        string blockId = pendingNasaTlxBlockId;
        pendingNasaTlxBlockId = null;
        nasaTlxManager.StartQuestionnaire(blockId);
    }

    // 暗算課題の開始フラグをサーバに送信する。
    private void SendMentalArithmeticStartStatus(string statusName, string blockId)
    {
        activeMentalArithmeticStatusName = statusName;
        activeMentalArithmeticBlockId = blockId;
        isMentalArithmeticStatusActive = true;

        if (requestSender != null)
        {
            requestSender.SendMentalArithmeticStartFlag(
                activeMentalArithmeticStatusName,
                activeMentalArithmeticBlockId);
        }
    }

    // 暗算課題の終了フラグをサーバに送信する。
    private void SendMentalArithmeticEndStatus()
    {
        if (!isMentalArithmeticStatusActive)
        {
            return;
        }

        if (requestSender != null)
        {
            requestSender.SendMentalArithmeticEndFlag(
                activeMentalArithmeticStatusName,
                activeMentalArithmeticBlockId);
        }

        isMentalArithmeticStatusActive = false;
        activeMentalArithmeticStatusName = null;
        activeMentalArithmeticBlockId = null;
    }

    private void BuildProblemPools()
    {
        problemPools.Clear();
        problemPools[Difficulty.Low] = new List<Problem>();
        problemPools[Difficulty.Medium] = new List<Problem>();
        problemPools[Difficulty.High] = new List<Problem>();

        // aとbには6から18までの整数を使用する。
        // bをaから開始することで、6 x 14と14 x 6のような重複を作らない。
        for (int a = 6; a <= 18; a++)
        {
            for (int b = a; b <= 18; b++)
            {
                int product = a * b;

                // 積がどの難易度範囲にも入らない問題は候補へ追加しない。
                if (product >= 80 && product <= 108)
                {
                    problemPools[Difficulty.Low].Add(new Problem { a = a, b = b });
                }
                else if (product >= 126 && product <= 192)
                {
                    problemPools[Difficulty.Medium].Add(new Problem { a = a, b = b });
                }
                else if (product >= 221 && product <= 324)
                {
                    problemPools[Difficulty.High].Add(new Problem { a = a, b = b });
                }
            }
        }
    }


    //入力方法について　修正する
    private void SetupInputFields()
    {
        // Inspector値も有効範囲へ収めてから入力欄へ反映する。
        blockDurationSeconds = Mathf.Clamp(
            blockDurationSeconds,
            MinDurationSeconds,
            MaxDurationSeconds);
        durationInputField?.SetTextWithoutNotify(
            blockDurationSeconds.ToString(CultureInfo.InvariantCulture));

        if (answerInputField != null)
        {
            // 回答欄は暗算課題専用の数字ボタンからのみ更新する。
            answerInputField.contentType = TMP_InputField.ContentType.IntegerNumber;
            answerInputField.readOnly = true;
        }

        if (durationInputField != null)
        {
            durationInputField.contentType = TMP_InputField.ContentType.IntegerNumber;
            durationInputField.onEndEdit.AddListener(SetBlockDuration);
        }

        if (numericKeyboardInputBinder == null)
        {
            // Inspectorで未指定なら、このGameObject上の既存コンポーネントを探す。
            numericKeyboardInputBinder = GetComponent<XRNumericKeyboardInputBinder>();
        }

        if (numericKeyboardInputBinder == null)
        {
            // 見つからない場合だけ動的に追加し、設定漏れでも入力可能にする。
            numericKeyboardInputBinder = gameObject.AddComponent<XRNumericKeyboardInputBinder>();
        }

        numericKeyboardInputBinder.BindInteger(durationInputField, SetBlockDuration);
    }

    private void SaveLogsToCsv()
    {
        // 回答が一件もない場合は空のCSVファイルを作成しない。
        if (trialLogs.Count == 0 || string.IsNullOrWhiteSpace(csvFilePath))
        {
            return;
        }

        // 毎回全ログを書き直すため、ブロック途中まで保存した後も重複行は生じない。
        StringBuilder csv = new StringBuilder();
        csv.AppendLine(
            "user_id,block_id,difficulty,block_duration_sec,trial_index,a,b," +
            "correct_answer,user_answer,is_correct,is_skipped,reaction_time_ms," +
            "block_elapsed_time_ms,timestamp");

        foreach (TrialLog log in trialLogs)
        {
            // 小数値は端末の言語設定に左右されないピリオド形式で保存する。
            csv.Append(Csv(log.participantId)).Append(',')
                .Append(log.blockId).Append(',')
                .Append(log.difficulty).Append(',')
                .Append(log.blockDurationSec).Append(',')
                .Append(log.trialIndex).Append(',')
                .Append(log.a).Append(',')
                .Append(log.b).Append(',')
                .Append(log.correctAnswer).Append(',')
                .Append(log.userAnswer).Append(',')
                .Append(log.isCorrect.ToString().ToLowerInvariant()).Append(',')
                .Append(log.isSkipped.ToString().ToLowerInvariant()).Append(',')
                .Append(log.reactionTimeMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(log.blockElapsedTimeMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(Csv(log.timestamp));
        }

        // Unityが書き込み可能なpersistentDataPathへ、BOMなしUTF-8で保存する。
        File.WriteAllText(csvFilePath, csv.ToString(), new UTF8Encoding(false));
    }

    private string CreateCsvFilePath()
    {
        // participantIdにファイル名として使用できない文字があれば置換する。
        string safeParticipantId = string.IsNullOrWhiteSpace(participantId)
            ? "unknown"
            : participantId;

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            safeParticipantId = safeParticipantId.Replace(invalidChar, '_');
        }

        string fileName = string.Format(
            CultureInfo.InvariantCulture,
            "mental_arithmetic_{0}_{1}.csv",
            safeParticipantId,
            DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        return Path.Combine(Application.persistentDataPath, fileName);
    }

    private bool CanStart()
    {
        // 実行中のタスクがある間は、別の開始ボタンを押しても多重起動しない。
        return taskCoroutine == null &&
               (state == TaskState.Idle || state == TaskState.Complete);
    }

    private void SetControlsInteractable(bool interactable)
    {
        if (answerInputField != null)
        {
            answerInputField.interactable = interactable;
        }

        if (submitButton != null)
        {
            submitButton.interactable = interactable;
        }

        if (skipButton != null)
        {
            skipButton.interactable = interactable;
        }
    }

    private static string Csv(string value)
    {
        // カンマ、引用符、改行を含む文字列はCSV仕様に従って引用する。
        string safeValue = value ?? string.Empty;
        if (!safeValue.Contains(",") &&
            !safeValue.Contains("\"") &&
            !safeValue.Contains("\n") &&
            !safeValue.Contains("\r"))
        {
            return safeValue;
        }

        return "\"" + safeValue.Replace("\"", "\"\"") + "\"";
    }

    private static void Shuffle<T>(IList<T> list)
    {
        // Fisher-Yates法で偏りなく問題順をシャッフルする。
        for (int i = list.Count - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            T value = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = value;
        }
    }

    private void OnDestroy()
    {
        // シーン破棄時にリスナーを解除し、再生成時の重複登録を防ぐ。
        submitButton?.onClick.RemoveListener(SubmitAnswer);
        skipButton?.onClick.RemoveListener(SkipProblem);
        durationInputField?.onEndEdit.RemoveListener(SetBlockDuration);

        if (numericKeyboardInputBinder != null)
        {
            numericKeyboardInputBinder.Unbind(durationInputField);
        }
    }
}
