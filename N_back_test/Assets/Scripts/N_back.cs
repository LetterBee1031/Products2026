using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.IO;


public class N_back : MonoBehaviour
{
    [System.Serializable]
    private class NBackPatternFile
    {
        public string patternId;
        public int nBack;
        public string[] stimuli;
        public bool[] isTarget;
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

    public TextMeshProUGUI TextAlphabet;
    public TextMeshProUGUI textResult = new TextMeshProUGUI();
    public TextMeshProUGUI textQuestionNum = new TextMeshProUGUI();
    public TextMeshProUGUI textTitle = new TextMeshProUGUI();

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
    private float timeHoleTask = 0f; // 経過時間
    private float timeOneTask = 0f; // タスク中の時間
    bool isWorking = false; // n-back課題中か
    bool isButtonSamePressed = false; // 
    bool isJudgeAdded = false; // 
    bool isTextDisplayed = false;

    int outTextCount = 0;
    private const string PatternRootRelativePath = "Scripts/GeneratedNBackPatterns";
    private NBackPatternFile loadedPattern;

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

        SetupSettingInputFields();
        UpdateTitleText();
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void FixedUpdate()
    {
        Timer(isWorking);
        N_Back_Working();
    }

    // n-back の開始
    public void SetNback(bool flag, int n, string block_id)
    {
        // Coroutine coroutine;
        if (flag)
        {
            if (!LoadPattern())
            {
                return;
            }

            //time = 0f;
            isWorking = true;
            n_back_num = n;
            buttonStart.SetActive(false);
            buttonSame.SetActive(true);
            buttonForQuestion.SetActive(false);
            Debug.Log("SetNback");
            requestSender.SendNbackStartFlag(n_back_num, block_id);
            //N_Back_Working();
        }
        // else
        // {
        //     //time = 0f;
        //     isWorking = false;
        //     StopCoroutine(coroutine);
        // }
    }

    private bool LoadPattern()
    {
        string patternFolderPath = Path.Combine(
            Application.dataPath,
            PatternRootRelativePath,
            $"{n_back_num}back");

        if (!Directory.Exists(patternFolderPath))
        {
            Debug.LogError($"N-back pattern folder was not found: {patternFolderPath}");
            return false;
        }

        string[] patternPaths = Directory.GetFiles(
            patternFolderPath,
            "*.json",
            SearchOption.TopDirectoryOnly);

        if (patternPaths.Length == 0)
        {
            Debug.LogError($"No N-back pattern files were found: {patternFolderPath}");
            return false;
        }

        string patternPath = patternPaths[Random.Range(0, patternPaths.Length)];

        try
        {
            loadedPattern = JsonUtility.FromJson<NBackPatternFile>(File.ReadAllText(patternPath));
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"Failed to read N-back pattern: {patternPath}\n{exception}");
            loadedPattern = null;
            return false;
        }

        if (loadedPattern == null ||
            loadedPattern.stimuli == null ||
            loadedPattern.isTarget == null ||
            loadedPattern.stimuli.Length == 0 ||
            loadedPattern.stimuli.Length != loadedPattern.isTarget.Length)
        {
            Debug.LogError($"N-back pattern data is invalid: {patternPath}");
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

        if (loadedPattern.nBack != n_back_num)
        {
            Debug.LogError(
                $"N-back pattern does not match the current setting: " +
                $"expected={n_back_num}, actual={loadedPattern.nBack}, file={patternPath}");
            loadedPattern = null;
            return false;
        }

        Debug.Log(
            $"Loaded N-back pattern: {loadedPattern.patternId} " +
            $"({loadedPattern.stimuli.Length} trials, file: {Path.GetFileName(patternPath)})");
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
        nasaTlxManager.StartQuestionnaire(n_back_num + "_back_start");
        buttonStart.SetActive(true);
        buttonSame.SetActive(false);
        buttonForQuestion.SetActive(false);
        textResult.text = "";
        textQuestionNum.text = "";
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
            if (timeHoleTask < timeLimit && outTextCount < loadedPattern.stimuli.Length)
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

                    if (TextAlphabet == null)
                    {
                        Debug.LogError("TextAlphabet is not assigned.");
                        isWorking = false;
                        return;
                    }

                    TextAlphabet.text = stimulus;
                    TextAlphabet.enabled = true;
                    textQuestionNum.text = outTextCount.ToString();

                    isTextDisplayed = true;
                }

                // 1文字ごとの制限時間中
                // 文字を表示した後、刺激間隔として十字を表示する
                if (timeOneTask >= stimulusDisplayDuration && TextAlphabet.text != "+")
                {
                    TextAlphabet.text = "+";
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

                if (TextAlphabet != null)
                {
                    TextAlphabet.enabled = false;
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
                requestSender.SendNbackEndFlag(n_back_num);
                Debug.Log("N_back End. Out Text Count:" + outTextCount);
                buttonStart.SetActive(false);
                buttonSame.SetActive(false);
                buttonForQuestion.SetActive(true);
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
        textTitle.text = "N back test\n" + n_back_num.ToString() + " back mode";
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
