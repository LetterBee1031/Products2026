using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
// using UnityEditor.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;


public class N_back : MonoBehaviour
{
    // 刺激パターンJSONをJsonUtilityで読み込むためのデータ構造
    [System.Serializable]
    private class NBackPatternFile
    {
        public string patternId;
        public int nBack;
        public string[] stimuli;
        public bool[] isTarget;
    }

    // ユーザー別割当JSONのルート要素
    [System.Serializable]
    private class NBackAssignmentFile
    {
        public NBackAssignmentParticipant participant;
    }

    // 割当JSON内の参加者情報
    [System.Serializable]
    private class NBackAssignmentParticipant
    {
        public string userId;
        public NBackAssignmentSession[] sessions;
    }

    // 1セッション分の割当情報
    [System.Serializable]
    private class NBackAssignmentSession
    {
        public NBackAssignmentBlock[] blocks;
    }

    // 1回のN-back課題に必要な情報
    [System.Serializable]
    private class NBackAssignmentBlock
    {
        public int sessionId; // このブロックが属するセッション番号
        public int blockId; // 参加者内での通し番号
        public int nBack;   // このブロックで実施するN-back条件（0～3）
    }

    public static class Define
    {
        public static readonly int LIST_MAX_LENGTH = 100;

    }

    public RequestSender requestSender;
    public NasaTlxManager nasaTlxManager;
    public AudioController audioController;
    public GameObject buttonStart;
    public GameObject buttonSame;
    public GameObject buttonForQuestion;
    public GameObject buttonPracticeEnd;
    public GameObject parentNextNback;

    public TextMeshProUGUI textAlphabet;
    public TextMeshProUGUI textResult = new TextMeshProUGUI();
    public TextMeshProUGUI textQuestionNum = new TextMeshProUGUI();
    public TextMeshProUGUI textBlockTitle = new TextMeshProUGUI();
    public TextMeshProUGUI textNextNback = new TextMeshProUGUI();

    // 体験空間内の入力欄からN-back設定を変更するための参照
    [Header("N-back setting input UI")]
    public TMP_InputField nBackNumInputField; // n_back_num入力欄
    public TMP_InputField timeWaitOneTaskInputField; // timeWaitOneTask入力欄
    public TMP_InputField timeLimitInputField; // timeLimit入力欄
    public XRNumericKeyboardInputBinder numericKeyboardInputBinder; // 入力欄と共通数値キーボードを接続する
    public bool applySettingsOnEndEdit = true; // 入力終了時に自動で値を反映する
    public int minNBackNum = 0; // 0-backを許可するため最小値は0
    public int maxNBackNum = 99; // LIST_MAX_LENGTHを超えない範囲で使う
    public float minTimeWaitOneTask = 0.1f; // 1文字あたりの表示時間が短すぎないようにする
    public float minTimeLimit = 1f; // 0秒以下で即終了しないようにする

    public int n_back_num; // n-back の n, 何バックのときを指定するか
    [Header("Stimulus timing")]
    public float stimulusDisplayDuration = 0.5f; // 文字を表示する時間
    public float timeWaitOneTask = 2f; // 十字を表示する刺激間隔
    public float timeLimit = 120f; // 1タスク全体の時間
    public float practiceTimeLimit = 45f; // 練習ブロックの制限時間
    [Header("Session fixation")]
    public float sessionFixationDuration = 120f; // 最初のセッション前とセッション間に「+」を表示する時間
    private float timeHoleTask = 0f; // 経過時間
    private float timeOneTask = 0f; // タスク中の時間
    bool isWorking = false; // n-back課題中か
    bool isButtonSamePressed = false; // 
    bool isJudgeAdded = false; // 
    bool isTextDisplayed = false;

    int outTextCount = 0;
    private const string PatternRootResourcePath = "GeneratedNBackPatterns"; // Resources内の刺激パターン格納先
    private const string AssignmentRootResourcePath = "GeneratedNBackAssignments"; // Resources内のユーザー別割当格納先
    private NBackPatternFile loadedPattern; // 現在実行中の刺激パターン
    private readonly List<NBackAssignmentBlock> assignmentBlocks = new List<NBackAssignmentBlock>(); // blockId順に並べた全ブロック
    private int currentAssignmentIndex; // 次に実行するassignmentBlocksの位置
    private NBackAssignmentBlock currentAssignmentBlock; // 実行中またはアンケート待ちのブロック
    private string loadedAssignmentUserId; // 現在読み込んでいる割当のuserId
    private bool isSessionFixationWorking; // セッション前の「+」表示中か
    private float sessionFixationElapsedTime; // 現在の「+」表示経過時間
    private int fixationCompletedSessionId = -1; // 「+」表示を完了した直近のsessionId
    private bool isPracticeMode; // 割当とは別の1-back練習を実施中か
    private int lastNBackCorrectCount; // 直前に完了した本編N-back課題の正答数

    List<bool> listJudge = new List<bool>(); // 正解したかどうかのリスト
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //requestSender = GetComponent<RequestSender>();
        Debug.Log("System Start");
        buttonStart.SetActive(true);
        buttonSame.SetActive(false);
        buttonForQuestion.SetActive(false);
        for (int i = 0; i < Define.LIST_MAX_LENGTH; i++)
        {
            listJudge.Add(false);
        }
        // Inspectorで範囲外の値が入っていても、リスト参照が壊れない範囲に丸める
        maxNBackNum = Mathf.Clamp(maxNBackNum, minNBackNum, Define.LIST_MAX_LENGTH - 1);
        n_back_num = Mathf.Clamp(n_back_num, minNBackNum, maxNBackNum);
        timeWaitOneTask = Mathf.Max(minTimeWaitOneTask, timeWaitOneTask);
        timeLimit = Mathf.Max(minTimeLimit, timeLimit);
        practiceTimeLimit = Mathf.Max(minTimeLimit, practiceTimeLimit);

        SetupSettingInputFields();
        UpdateTitleText();

        // 起動時のRequestSender.userIdに対応する実施順を準備する
        LoadAssignment();
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void FixedUpdate()
    {
        // セッション前の注視ブロック中は、N-back本体のタイマーを進めない
        if (isSessionFixationWorking)
        {
            UpdateSessionFixation();
            return;
        }

        Timer(isWorking);
        N_Back_Working();
    }

    // n-back の開始
    public void SetNback(bool flag)
    {
        // falseが渡された場合や、N-back・注視ブロック実行中の二重開始は無視する
        if (!flag || isWorking || isSessionFixationWorking)
        {
            return;
        }

        // userIdが起動後に変更された場合も、開始直前に正しい割当へ読み替える
        if (!EnsureAssignmentForCurrentUser())
        {
            return;
        }

        if (currentAssignmentIndex >= assignmentBlocks.Count)
        {
            Debug.Log("All assigned N-back blocks have been completed.");
            buttonStart.SetActive(false);
            return;
        }

        // blockId順リストから今回の条件を取得し、手動設定よりも割当を優先する
        currentAssignmentBlock = assignmentBlocks[currentAssignmentIndex];
        SetNbackNum(currentAssignmentBlock.nBack);

        // 各セッションの最初のN-back課題の前に、2分間の「+」注視ブロックを挟む
        if (fixationCompletedSessionId != currentAssignmentBlock.sessionId)
        {
            StartSessionFixation();
            return;
        }

        StartCurrentNBackBlock();
    }

    // 本編の割当を進めずに、2-back課題を練習として開始する
    public void StartPracticeNBack()
    {
        if (isWorking || isSessionFixationWorking)
        {
            Debug.LogWarning("N-back or session fixation is already running.");
            return;
        }

        // userIdが変更されていれば、その体験者の本編割当へ切り替えてから練習する
        if (!EnsureAssignmentForCurrentUser())
        {
            return;
        }

        // 練習は常に2-backとし、割当のcurrentAssignmentIndexは変更しない
        SetNbackNum(2);
        if (!LoadPattern())
        {
            RestoreAssignedNBackDisplay();
            return;
        }

        // 前回の表示や判定状態が残っていても、練習は先頭試行から開始する
        outTextCount = 0;
        timeHoleTask = 0f;
        timeOneTask = 0f;
        isButtonSamePressed = false;
        isJudgeAdded = false;
        isTextDisplayed = false;
        buttonPracticeEnd.SetActive(false);
        for (int i = 0; i < listJudge.Count; i++)
        {
            listJudge[i] = false;
        }

        isPracticeMode = true;
        isWorking = true;

        textBlockTitle.text = "N back practice. 2 back mode";
        textResult.text = "";
        textQuestionNum.text = "";
        buttonStart.SetActive(false);
        buttonSame.SetActive(true);
        buttonForQuestion.SetActive(false);
        parentNextNback.SetActive(false);

        // 練習データを本編と混同しないため、RequestSenderへの通知は行わない
        Debug.Log("2-back practice started.");
    }

    // 練習終了後に、本編で次に実施するN-back条件をUIへ戻す
    public void RestoreAssignedNBackDisplay()
    {
        buttonStart.SetActive(true);
        buttonSame.SetActive(false);
        buttonForQuestion.SetActive(false);
        parentNextNback.SetActive(true);
        buttonPracticeEnd.SetActive(false);
        textResult.enabled = false;
        textQuestionNum.text = "";
        
        if (currentAssignmentBlock != null)
        {
            SetNbackNum(currentAssignmentBlock.nBack);
        }
        else if (currentAssignmentIndex >= 0 && currentAssignmentIndex < assignmentBlocks.Count)
        {
            SetNbackNum(assignmentBlocks[currentAssignmentIndex].nBack);
        }
    }

    // 現在選択されている割当ブロックのN-back課題を開始する
    private void StartCurrentNBackBlock()
    {
        if (currentAssignmentBlock == null)
        {
            Debug.LogError("The current N-back assignment block is missing.");
            buttonStart.SetActive(true);
            return;
        }

        // 割当で指定されたnBack条件の刺激パターンをランダムに1件読み込む
        if (!LoadPattern())
        {
            currentAssignmentBlock = null;
            buttonStart.SetActive(true);
            return;
        }

        isWorking = true;
        buttonStart.SetActive(false);
        buttonSame.SetActive(true);
        buttonForQuestion.SetActive(false);
        parentNextNback.SetActive(false);

        // 開始通知には、割当JSONのblockIdをそのまま試験IDとして渡す
        string blockId = currentAssignmentBlock.blockId.ToString(CultureInfo.InvariantCulture);
        Debug.Log($"SetNback: blockId={blockId}, nBack={n_back_num}");
        requestSender.SendNbackStartFlag(n_back_num, blockId);
    }

    // セッション開始前に「+」だけを表示する注視ブロックを開始する
    private void StartSessionFixation()
    {
        if (textAlphabet == null)
        {
            Debug.LogError("TextAlphabet is not assigned.");
            currentAssignmentBlock = null;
            return;
        }

        sessionFixationDuration = Mathf.Max(0f, sessionFixationDuration);
        sessionFixationElapsedTime = 0f;
        isSessionFixationWorking = true;

        textAlphabet.text = "+";
        textAlphabet.enabled = true;
        textResult.text = "";
        textQuestionNum.text = "";
        textBlockTitle.text = $"Session {currentAssignmentBlock.sessionId} Fixation";

        buttonStart.SetActive(false);
        buttonSame.SetActive(false);
        buttonForQuestion.SetActive(false);
        parentNextNback.SetActive(false);

        Debug.Log(
            $"Session fixation started: sessionId={currentAssignmentBlock.sessionId}, " +
            $"duration={sessionFixationDuration} seconds");
    }

    // 「+」を指定時間表示し終えたら、開始ボタンを表示して体験者の操作を待つ
    private void UpdateSessionFixation()
    {
        sessionFixationElapsedTime += Time.deltaTime;
        if (sessionFixationElapsedTime < sessionFixationDuration)
        {
            return;
        }

        isSessionFixationWorking = false;
        sessionFixationElapsedTime = 0f;

        if (currentAssignmentBlock == null)
        {
            Debug.LogError("The assignment block was lost during session fixation.");
            buttonStart.SetActive(true);
            return;
        }

        fixationCompletedSessionId = currentAssignmentBlock.sessionId;
        Debug.Log($"Session fixation completed: sessionId={fixationCompletedSessionId}");

        // 注視画面を閉じ、次に実施するN-back条件と開始ボタンを表示する
        if (textAlphabet != null)
        {
            textAlphabet.enabled = false;
        }

        SetNbackNum(currentAssignmentBlock.nBack);
        buttonStart.SetActive(true);
        buttonSame.SetActive(false);
        buttonForQuestion.SetActive(false);
        parentNextNback.SetActive(true);

        Debug.Log(
            $"Waiting for participant to start N-back: " +
            $"blockId={currentAssignmentBlock.blockId}, nBack={currentAssignmentBlock.nBack}");
    }

    // RequestSender.userIdと現在保持している割当が一致していることを保証する
    private bool EnsureAssignmentForCurrentUser()
    {
        if (requestSender == null)
        {
            Debug.LogError("RequestSender is not assigned.");
            return false;
        }

        string currentUserId = requestSender.userId == null
            ? string.Empty
            : requestSender.userId.Trim();

        // 未読込またはuserId変更時だけファイルを読み直す
        if (assignmentBlocks.Count == 0 || loadedAssignmentUserId != currentUserId)
        {
            return LoadAssignment();
        }

        return true;
    }

    // {RequestSender.userId}_nback_assignment.jsonを読み込み、blockId順の実行リストを作る
    private bool LoadAssignment()
    {
        // 再読込時に以前のユーザーの進行状態を残さない
        assignmentBlocks.Clear();
        currentAssignmentIndex = 0;
        currentAssignmentBlock = null;
        loadedAssignmentUserId = null;
        isSessionFixationWorking = false;
        sessionFixationElapsedTime = 0f;
        fixationCompletedSessionId = -1;
        isPracticeMode = false;

        if (requestSender == null)
        {
            Debug.LogError("RequestSender is not assigned.");
            return false;
        }

        string userId = requestSender.userId == null
            ? string.Empty
            : requestSender.userId.Trim();

        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogError("RequestSender.userId is empty.");
            return false;
        }

        // 例: Assets/Resources/GeneratedNBackAssignments/01_nback_assignment.json
        string assignmentResourcePath =
            $"{AssignmentRootResourcePath}/{userId}_nback_assignment";
        TextAsset assignmentAsset = Resources.Load<TextAsset>(assignmentResourcePath);

        if (assignmentAsset == null)
        {
            Debug.LogError(
                $"N-back assignment resource was not found: {assignmentResourcePath}");
            return false;
        }

        NBackAssignmentFile assignmentFile;
        try
        {
            assignmentFile = JsonUtility.FromJson<NBackAssignmentFile>(assignmentAsset.text);
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                $"Failed to read N-back assignment: {assignmentResourcePath}\n{exception}");
            return false;
        }

        if (assignmentFile == null ||
            assignmentFile.participant == null ||
            assignmentFile.participant.sessions == null)
        {
            Debug.LogError($"N-back assignment data is invalid: {assignmentResourcePath}");
            return false;
        }

        // ファイル名だけでなく、JSON内部のuserIdも一致することを確認する
        if (assignmentFile.participant.userId != userId)
        {
            Debug.LogError(
                $"N-back assignment userId does not match RequestSender.userId: " +
                $"expected={userId}, actual={assignmentFile.participant.userId}");
            return false;
        }

        // セッションごとに分かれたblockを、参加者全体の1つのリストへまとめる
        foreach (NBackAssignmentSession session in assignmentFile.participant.sessions)
        {
            if (session == null || session.blocks == null)
            {
                continue;
            }

            foreach (NBackAssignmentBlock block in session.blocks)
            {
                if (block != null)
                {
                    assignmentBlocks.Add(block);
                }
            }
        }

        // JSON内のセッション配列順に依存せず、通し番号順で必ず実施する
        assignmentBlocks.Sort((left, right) => left.blockId.CompareTo(right.blockId));

        if (assignmentBlocks.Count == 0)
        {
            Debug.LogError(
                $"N-back assignment contains no blocks: {assignmentResourcePath}");
            return false;
        }

        // 実行不能な条件や、同じblockIdの重複を開始前に検出する
        for (int i = 0; i < assignmentBlocks.Count; i++)
        {
            NBackAssignmentBlock block = assignmentBlocks[i];
            if (block.sessionId <= 0 ||
                block.blockId <= 0 ||
                block.nBack < 0 ||
                block.nBack > 3)
            {
                Debug.LogError(
                    $"Invalid N-back assignment block: " +
                    $"sessionId={block.sessionId}, blockId={block.blockId}, nBack={block.nBack}");
                assignmentBlocks.Clear();
                return false;
            }

            if (i > 0 && assignmentBlocks[i - 1].blockId == block.blockId)
            {
                Debug.LogError($"Duplicate N-back blockId: {block.blockId}");
                assignmentBlocks.Clear();
                return false;
            }
        }

        // 最初の開始ボタンを押す前から、UIにはblockId=最小の条件を表示しておく
        loadedAssignmentUserId = userId;
        SetNbackNum(assignmentBlocks[0].nBack);
        Debug.Log(
            $"Loaded N-back assignment: userId={userId}, blocks={assignmentBlocks.Count}, " +
            $"firstBlockId={assignmentBlocks[0].blockId}");
        return true;
    }

    private bool LoadPattern()
    {
        // 現在の割当条件に対応するフォルダー（例: 2back）を選択する
        string patternResourcePath =
            $"{PatternRootResourcePath}/{n_back_num}back";

        // 条件フォルダー直下にある全パターンJSONを候補にする
        TextAsset[] patternAssets = Resources.LoadAll<TextAsset>(patternResourcePath);

        if (patternAssets.Length == 0)
        {
            Debug.LogError(
                $"No N-back pattern resources were found: {patternResourcePath}");
            return false;
        }

        // 同じnBack条件でも提示系列が固定されないよう、実行ごとにランダム選択する
        TextAsset patternAsset = patternAssets[Random.Range(0, patternAssets.Length)];

        try
        {
            loadedPattern = JsonUtility.FromJson<NBackPatternFile>(patternAsset.text);
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                $"Failed to read N-back pattern: {patternAsset.name}\n{exception}");
            loadedPattern = null;
            return false;
        }

        if (loadedPattern == null ||
            loadedPattern.stimuli == null ||
            loadedPattern.isTarget == null ||
            loadedPattern.stimuli.Length == 0 ||
            loadedPattern.stimuli.Length != loadedPattern.isTarget.Length)
        {
            Debug.LogError($"N-back pattern data is invalid: {patternAsset.name}");
            loadedPattern = null;
            return false;
        }

        if (loadedPattern.stimuli.Length > Define.LIST_MAX_LENGTH)
        {
            Debug.LogError(
                $"N-back pattern contains too many trials: {loadedPattern.stimuli.Length} " +
                $"(maximum: {Define.LIST_MAX_LENGTH})");
            loadedPattern = null;
            return false;
        }

        // 誤ったフォルダーやJSONが混入していても別条件として実行しない
        if (loadedPattern.nBack != n_back_num)
        {
            Debug.LogError(
                $"N-back pattern does not match the current setting: " +
                $"expected={n_back_num}, actual={loadedPattern.nBack}, file={patternAsset.name}");
            loadedPattern = null;
            return false;
        }

        Debug.Log(
            $"Loaded N-back pattern: {loadedPattern.patternId} " +
            $"({loadedPattern.stimuli.Length} trials, file: {patternAsset.name})");
        return true;
    }

    public void SetNbackNum(int n)
    {
        // ボタンや入力欄から来た値を許可範囲に丸めてから反映する
        n_back_num = Mathf.Clamp(n, minNBackNum, maxNBackNum);
        if (nBackNumInputField != null)
        {
            nBackNumInputField.SetTextWithoutNotify(n_back_num.ToString());
        }

        UpdateTitleText();
    }

    public void SetNbackNum(string inputText)
    {
        // TMP_InputField.onEndEditから呼べるようにstringを受け取る
        if (!int.TryParse(inputText, out int n))
        {
            Debug.LogWarning($"N-back num input is invalid: {inputText}");
            SyncSettingInputFields();
            return;
        }

        SetNbackNum(n);
    }

    public void SetNbackNumFromInputField()
    {
        // UIボタンのOnClickに登録して、入力欄の値を手動反映するときに使う
        if (nBackNumInputField == null)
        {
            Debug.LogWarning("N-back num input field is not assigned.");
            return;
        }

        SetNbackNum(nBackNumInputField.text);
    }

    public void SetTimeWaitOneTask(float seconds)
    {
        // timeWaitOneTaskは1文字あたりの秒数として扱い、短すぎる値は最小値に丸める
        timeWaitOneTask = Mathf.Max(minTimeWaitOneTask, seconds);
        if (timeWaitOneTaskInputField != null)
        {
            timeWaitOneTaskInputField.SetTextWithoutNotify(FormatFloat(timeWaitOneTask));
        }

        Debug.Log($"N-back time wait one task updated: {timeWaitOneTask}");
    }

    public void SetTimeWaitOneTask(string inputText)
    {
        // TMP_InputField.onEndEditから呼べるようにstringをfloatへ変換する
        if (!TryParseFloat(inputText, out float seconds))
        {
            Debug.LogWarning($"N-back time wait one task input is invalid: {inputText}");
            SyncSettingInputFields();
            return;
        }

        SetTimeWaitOneTask(seconds);
    }

    public void SetTimeWaitOneTaskFromInputField()
    {
        // UIボタンのOnClickに登録して、入力欄の値を手動反映するときに使う
        if (timeWaitOneTaskInputField == null)
        {
            Debug.LogWarning("N-back time wait one task input field is not assigned.");
            return;
        }

        SetTimeWaitOneTask(timeWaitOneTaskInputField.text);
    }

    public void SetTimeLimit(float seconds)
    {
        // timeLimitは秒数として扱い、短すぎる値は最小値に丸める
        timeLimit = Mathf.Max(minTimeLimit, seconds);
        if (timeLimitInputField != null)
        {
            timeLimitInputField.SetTextWithoutNotify(FormatFloat(timeLimit));
        }

        Debug.Log($"N-back time limit updated: {timeLimit}");
    }

    public void SetTimeLimit(string inputText)
    {
        // 小数入力に対応するため、文字列からfloatへ変換して反映する
        if (!TryParseFloat(inputText, out float seconds))
        {
            Debug.LogWarning($"N-back time limit input is invalid: {inputText}");
            SyncSettingInputFields();
            return;
        }

        SetTimeLimit(seconds);
    }

    public void SetTimeLimitFromInputField()
    {
        // UIボタンのOnClickに登録して、入力欄の値を手動反映するときに使う
        if (timeLimitInputField == null)
        {
            Debug.LogWarning("N-back time limit input field is not assigned.");
            return;
        }

        SetTimeLimit(timeLimitInputField.text);
    }

    public void OnSameButton()
    {
        isButtonSamePressed = true;
        Debug.Log("SamePressed");
    }

    public void moveForQuestion()
    {
        // 課題完了前や二重押下によって、割当を飛ばさないようにする
        if (currentAssignmentBlock == null)
        {
            Debug.LogWarning("There is no completed N-back assignment block.");
            return;
        }

        // N-back課題とNASA-TLXを同じblockIdで関連付ける
        string completedBlockId = currentAssignmentBlock.blockId.ToString(
            CultureInfo.InvariantCulture);
        bool isFinalBlock = currentAssignmentIndex == assignmentBlocks.Count - 1;
        nasaTlxManager.StartQuestionnaire(
            completedBlockId,
            lastNBackCorrectCount,
            isFinalBlock);

        // アンケート画面へ遷移した時点で、次のblockIdへ進める
        currentAssignmentIndex++;
        currentAssignmentBlock = null;

        bool hasNextBlock = currentAssignmentIndex < assignmentBlocks.Count;
        if (hasNextBlock)
        {
            // 次回実施条件を開始前にタイトルと入力欄へ反映する
            SetNbackNum(assignmentBlocks[currentAssignmentIndex].nBack);
        }
        else
        {
            textBlockTitle.text = "All blocks completed";
            Debug.Log("All assigned N-back blocks have been completed.");
            parentNextNback.SetActive(false);
        }

        buttonStart.SetActive(hasNextBlock);
        buttonSame.SetActive(false);
        buttonForQuestion.SetActive(false);
        textResult.text = "";
        textQuestionNum.text = "";
        parentNextNback.SetActive(true);
    }

    // 時間計測の研究
    private void Timer(bool flag)
    {
        if (flag)
        {
            timeHoleTask += Time.deltaTime;
            timeOneTask += Time.deltaTime;
            //Debug.Log("全体時間：" + timeHoleTask);
            //Debug.Log("タスク時間" + timeOneTask);
        }
    }

    private void N_Back_Working()
    {
        if (isWorking)
        {

            // int i = 0;
            int resultNum = 0;
            //Random random = new Random();
            //timeHoleTask = 0f;
            //timeOneTask = 0f;
            textResult.enabled = false;

            // N-backタスク全体の制限時間中
            float activeTimeLimit = isPracticeMode ? practiceTimeLimit : timeLimit;
            if (timeHoleTask < activeTimeLimit && outTextCount < loadedPattern.stimuli.Length)
            {
                if (!isTextDisplayed)
                {
                    //Debug.Log("N_back_Working. OutTextCount:" + outTextCount);
                    string stimulus = loadedPattern.stimuli[outTextCount];
                    if (string.IsNullOrEmpty(stimulus))
                    {
                        Debug.LogError($"Stimulus is empty at trial {outTextCount}.");
                        isWorking = false;
                        return;
                    }

                    if (textAlphabet == null)
                    {
                        Debug.LogError("TextAlphabet is not assigned.");
                        isWorking = false;
                        return;
                    }

                    textAlphabet.text = stimulus;
                    textAlphabet.enabled = true;
                    textQuestionNum.text = outTextCount.ToString();

                    isTextDisplayed = true;
                }

                // 1文字ごとの制限時間中
                // 文字を表示した後、刺激間隔として十字を表示する
                if (timeOneTask >= stimulusDisplayDuration && textAlphabet.text != "+")
                {
                    textAlphabet.text = "+";
                }

                float trialDuration = stimulusDisplayDuration + timeWaitOneTask;

                // 文字と十字のどちらを表示している間も回答を受け付ける
                if (timeOneTask < trialDuration)
                {
                    if (outTextCount >= n_back_num)
                    {
                        if (!isJudgeAdded)
                        {
                            // ボタン押下が合ってたら
                            if (loadedPattern.isTarget[outTextCount] && isButtonSamePressed)
                            {
                                listJudge[outTextCount] = true;
                                isJudgeAdded = true;
                                audioController.PlayN_backSound(0);
                                Debug.Log("ButtonPush: true, isTarget: true");
                            }
                            // ボタン押下が合ってなかったら
                            else if (!loadedPattern.isTarget[outTextCount] && isButtonSamePressed)
                            {
                                listJudge[outTextCount] = false;
                                isJudgeAdded = true;
                                audioController.PlayN_backSound(1);
                                Debug.Log("ButtonPush: false, isTarget: false");
                            }
                        }
                    }
                }
                else
                {
                    // 1文字ごとの時間内にボタンが押されなかったら
                    if ((outTextCount >= n_back_num) && (isJudgeAdded == false))
                    {
                        if (loadedPattern.isTarget[outTextCount])
                        {
                            listJudge[outTextCount] = false;
                            isJudgeAdded = true;
                            audioController.PlayN_backSound(1);
                            Debug.Log("NoButtonPush: false, isTarget: true");
                        }
                        else
                        {
                            listJudge[outTextCount] = true;
                            isJudgeAdded = true;
                            audioController.PlayN_backSound(0);
                            Debug.Log("NoButtonPush: true, isTarget: false");
                        }
                    }

                    isJudgeAdded = false;
                    isButtonSamePressed = false;
                    isTextDisplayed = false;
                    timeOneTask = 0f;
                    outTextCount++;
                }


                // yield return new WaitForSeconds(waitTime);
            }
            else
            {
                isWorking = false;
                int completedTrialCount = Mathf.Min(
                    outTextCount + (isTextDisplayed ? 1 : 0),
                    loadedPattern.stimuli.Length);

                if (outTextCount < loadedPattern.isTarget.Length &&
                    outTextCount >= n_back_num &&
                    isJudgeAdded == false)
                {
                    if (loadedPattern.isTarget[outTextCount])
                    {
                        listJudge[outTextCount] = false;
                        isJudgeAdded = true;
                        audioController.PlayN_backSound(1);
                        Debug.Log("NoButtonPush: false");
                    }
                    else
                    {
                        listJudge[outTextCount] = true;
                        isJudgeAdded = true;
                        audioController.PlayN_backSound(0);
                        Debug.Log("NoButtonPush: true");
                    }
                }

                isJudgeAdded = false;
                isButtonSamePressed = false;
                isTextDisplayed = false;
                timeOneTask = 0f;

                if (textAlphabet != null)
                {
                    textAlphabet.enabled = false;
                }

                foreach (var val in listJudge)
                {
                    if (val == true)
                    {
                        resultNum++;
                    }
                }


                textResult.text = resultNum.ToString() + "/" +
                    Mathf.Max(0, completedTrialCount - n_back_num).ToString();
                textResult.enabled = true;

                // 初期化
                outTextCount = 0;
                timeHoleTask = 0f;
                timeOneTask = 0f;

                for (int i = 0; i < Define.LIST_MAX_LENGTH; i++)
                {
                    listJudge[i] = false;
                }
                if (isPracticeMode)
                {
                    // 練習では割当を進めず、終了通知やNASA-TLXも発生させない
                    isPracticeMode = false;
                    buttonPracticeEnd.SetActive(true);
                    //RestoreAssignedNBackDisplay();
                    Debug.Log("1-back practice completed.");
                }
                else
                {
                    // NASA-TLX送信時に同じblockIdのログとして保存する
                    lastNBackCorrectCount = resultNum;

                    // 開始通知と同じblockIdを付けて、この本編ブロックの終了を送信する
                    if (currentAssignmentBlock != null)
                    {
                        string blockId = currentAssignmentBlock.blockId.ToString(
                            CultureInfo.InvariantCulture);
                        requestSender.SendNbackEndFlag(n_back_num, blockId);
                    }
                    else
                    {
                        Debug.LogError("The current N-back assignment block is missing.");
                    }

                    buttonStart.SetActive(false);
                    buttonSame.SetActive(false);
                    buttonForQuestion.SetActive(true);
                }
                Debug.Log("N_back End. Out Text Count:" + outTextCount);
            }
        }
    }

    private void SetupSettingInputFields()
    {
        // 起動時に現在の設定値を入力欄へ表示する
        SyncSettingInputFields();
        SetupNumericKeyboardInputBinder();

        if (!applySettingsOnEndEdit)
        {
            return;
        }

        if (nBackNumInputField != null)
        {
            // 入力欄の編集が終わったタイミングでn_back_numへ反映する
            nBackNumInputField.onEndEdit.AddListener(SetNbackNum);
        }

        if (timeWaitOneTaskInputField != null)
        {
            // 入力欄の編集が終わったタイミングでtimeWaitOneTaskへ反映する
            timeWaitOneTaskInputField.onEndEdit.AddListener(SetTimeWaitOneTask);
        }

        if (timeLimitInputField != null)
        {
            // 入力欄の編集が終わったタイミングでtimeLimitへ反映する
            timeLimitInputField.onEndEdit.AddListener(SetTimeLimit);
        }
    }

    private void SetupNumericKeyboardInputBinder()
    {
        // キーボードの生成・表示と入力欄の選択イベント管理は共通スクリプトへ任せる。
        if (numericKeyboardInputBinder == null)
        {
            numericKeyboardInputBinder = GetComponent<XRNumericKeyboardInputBinder>();
        }

        if (numericKeyboardInputBinder == null)
        {
            numericKeyboardInputBinder = gameObject.AddComponent<XRNumericKeyboardInputBinder>();
        }

        numericKeyboardInputBinder.BindInteger(nBackNumInputField, SetNbackNum);
        numericKeyboardInputBinder.BindDecimal(timeWaitOneTaskInputField, SetTimeWaitOneTask);
        numericKeyboardInputBinder.BindDecimal(timeLimitInputField, SetTimeLimit);
    }

    private void SyncSettingInputFields()
    {
        // SetTextWithoutNotifyで、値同期によるonEndEditの再実行を避ける
        if (nBackNumInputField != null)
        {
            nBackNumInputField.SetTextWithoutNotify(n_back_num.ToString());
        }

        if (timeWaitOneTaskInputField != null)
        {
            timeWaitOneTaskInputField.SetTextWithoutNotify(FormatFloat(timeWaitOneTask));
        }

        if (timeLimitInputField != null)
        {
            timeLimitInputField.SetTextWithoutNotify(FormatFloat(timeLimit));
        }
    }

    private void UpdateTitleText()
    {
        textBlockTitle.text = "block" + currentAssignmentIndex.ToString() +": " + n_back_num.ToString() + " back mode";
        textNextNback.text = n_back_num.ToString();
    }

    private bool TryParseFloat(string inputText, out float value)
    {
        // 日本語環境などの現在カルチャと、小数点固定のInvariantCultureの両方を試す
        if (float.TryParse(inputText, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        return float.TryParse(inputText, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private string FormatFloat(float value)
    {
        // 入力欄へ戻す表示は小数点形式で統一する
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void OnDisable()
    {
        // シーン終了や無効化時に、登録したイベントを外して重複登録を防ぐ
        if (nBackNumInputField != null)
        {
            nBackNumInputField.onEndEdit.RemoveListener(SetNbackNum);
        }

        if (timeWaitOneTaskInputField != null)
        {
            timeWaitOneTaskInputField.onEndEdit.RemoveListener(SetTimeWaitOneTask);
        }

        if (timeLimitInputField != null)
        {
            timeLimitInputField.onEndEdit.RemoveListener(SetTimeLimit);
        }

        // N-backで登録した入力欄だけを共通バインダーから解除する。
        if (numericKeyboardInputBinder != null)
        {
            numericKeyboardInputBinder.Unbind(nBackNumInputField);
            numericKeyboardInputBinder.Unbind(timeWaitOneTaskInputField);
            numericKeyboardInputBinder.Unbind(timeLimitInputField);
        }
    }
}
