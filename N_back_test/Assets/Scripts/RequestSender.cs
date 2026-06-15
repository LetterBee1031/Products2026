using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
// using UnityEditor.Compilation;
using TMPro;

public class RequestSender : MonoBehaviour
{
    [Header("FastAPI server base URL")]
    public string baseUrl = "http://127.0.0.1:8080"; // 送信先URL publicになっててunity側で固定され沼った．注意

    [Header("Server URL input UI")]
    public TMP_InputField baseUrlInputField;
    public bool loadSavedBaseUrl = true;
    public bool saveBaseUrlOnChange = true;
    public bool sendStartFlagOnStart = true;

    [Header("Fixed user id")]
    public string userId = "01";

    private const string BaseUrlPrefsKey = "RequestSender.BaseUrl";

    [Serializable]

    // status変更のポストの中身となるクラス
    public class StatusPost
    {
        public string id;
        public string status_flag;
        //public long timestamp_ms;   // ★統一
        public string sent_at;
    }

    [Serializable]
    public class EyeDataPost
    {
        public string userID;

        public float pupilDiaMeanRaw;
        public float pupilDiaMeanSmoothed;

        public float predictedPupilMm;
        public float tepr;
        public float luminanceY;

        public string sentAt;
        public long timestamp;
        public string deviceIp;
    }

    [Serializable]
    // Stroop課題の1試行分のログをサーバへ送るためのJSON形式。
    public class StroopLogPost
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
        public string sent_at;
        public long timestamp;
        public string deviceIp;
    }

    [Serializable]
    // 暗算課題の1試行分のログをサーバへ送るためのJSON形式。
    public class MentalArithmeticLogPost
    {
        public string participant_id;
        public int block_id;
        public string difficulty;
        public int block_duration_sec;
        public int trial_index;
        public int a;
        public int b;
        public int correct_answer;
        public string user_answer;
        public bool is_correct;
        public bool is_skipped;
        public float reaction_time_ms;
        public float block_elapsed_time_ms;
        public string trial_timestamp;
        public string sent_at;
        public long timestamp;
        public string deviceIp;
    }



    // ---- PLRキャリブレーション送信用 ----
    // Unity側で集めた「輝度Y」と「瞳孔径mm」の1サンプル
    [Serializable]
    public class PLRCalibrationSample
    {
        public float luminanceY_panel;
        public float luminanceY_cam;
        public float luminanceGap;
        public float pupilMm;
    }

    // /api/plr/fit に送るJSON全体
    [Serializable]
    public class PLRCalibrationRequest
    {
        public string userID;
        public string sentAt;
        public long timestamp;
        public string deviceIp;
        public List<PLRCalibrationSample> samples = new List<PLRCalibrationSample>();
    }

    // /api/plr/fit から返ってくる推定結果
    [Serializable]
    public class PLRFitResponse
    {
        public bool ok;
        public string userID;
        public float a;
        public float b;
        public float c;
        public float mse;
        public int sampleCount;
        public string error;
    }

    [Serializable]
    public class StatusGetResponse
    {
        public bool ok;
        public string id;
        public string status;
    }

    [Serializable]
    public class CL_ConditionGetResponse
    {
        public bool ok;
        public string id;
        public string cl_condition;
        public string error;
        public MLResult ml_result;
        public Dictionary<string, string> issue_settings = new Dictionary<string, string>();
    }

    [Serializable]
    public class MLResult
    {
        public string label;
        public int training_rows;
        public List<string> classes;
        public Dictionary<string, float> features;
        public Dictionary<string, float> probabilities;
    }

    bool isGetCLCondition = false;
    bool isCLRequestRunning = false;

    public float CLIntervalSec = 1.0f;
    private float CLLastRequestStartTime = -999f;


    private Coroutine cl_coroutine;
    public CL_ConditionGetResponse resp_cl_temp;
    public TextMeshProUGUI textCL_Condition = new TextMeshProUGUI();
    public TextMeshProUGUI[] text_issues = new TextMeshProUGUI[7];
    // public Dictionary<string, TextMeshProUGUI> dict_text_issues = new Dictionary<string, TextMeshProUGUI>();

    void Start()
    {
        if (loadSavedBaseUrl && PlayerPrefs.HasKey(BaseUrlPrefsKey))
        {
            baseUrl = NormalizeBaseUrl(PlayerPrefs.GetString(BaseUrlPrefsKey));
        }

        if (baseUrlInputField != null)
        {
            baseUrlInputField.SetTextWithoutNotify(baseUrl);
            baseUrlInputField.onEndEdit.AddListener(SetBaseUrl);
        }

        if (sendStartFlagOnStart)
        {
            SendStartFlag();
        }

        cl_coroutine = StartCoroutine(RequestLoop());
        resp_cl_temp = new CL_ConditionGetResponse();

        // dict_text_issues.Add("tempo", text_issues[0]);
        // dict_text_issues.Add("guidance", text_issues[1]);
        // dict_text_issues.Add("complexity", text_issues[2]);
        // dict_text_issues.Add("stimulus", text_issues[3]);
        // dict_text_issues.Add("break_policy", text_issues[4]);
        // dict_text_issues.Add("feedback", text_issues[5]);
        // dict_text_issues.Add("taste", text_issues[6]);
    }

    void FixedUpdate()
    {
        //FetchCLCondition();
    }

    // 例：どこかのタイミングで呼ぶ（UIボタンやイベント等）
    public void SendStartFlag()
    {
        StartCoroutine(PostStatusFlag("experience_start"));
    }

    public void SendEndFlag()
    {
        StartCoroutine(PostStatusFlag("experience_end"));
    }
    public void SendNbackStartFlag(int n)
    {
        StartCoroutine(PostStatusFlag(n + "_back_start"));
    }

    public void SendNbackEndFlag(int n)
    {
        StartCoroutine(PostStatusFlag(n + "_back_end"));
    }
    
    public void SendSeeingColorFlag(string color)
    {
        StartCoroutine(PostStatusFlag(color));
    }

    public void FetchStatus()
    {
        StartCoroutine(GetExpStatus());
    }
    public void FetchCLCondition()
    {
        if (isGetCLCondition)
        {
            StartCoroutine(GetCLCondition());
        }
    }

    public void SetBaseUrl(string inputUrl)
    {
        if (string.IsNullOrWhiteSpace(inputUrl))
        {
            Debug.LogWarning("BASE_URL input is empty.");
            return;
        }

        baseUrl = NormalizeBaseUrl(inputUrl);

        if (baseUrlInputField != null)
        {
            baseUrlInputField.SetTextWithoutNotify(baseUrl);
        }

        if (saveBaseUrlOnChange)
        {
            PlayerPrefs.SetString(BaseUrlPrefsKey, baseUrl);
            PlayerPrefs.Save();
        }

        Debug.Log($"BASE_URL updated: {baseUrl}");
    }

    public void SetBaseUrlFromInputField()
    {
        if (baseUrlInputField == null)
        {
            Debug.LogWarning("BASE_URL input field is not assigned.");
            return;
        }

        SetBaseUrl(baseUrlInputField.text);
    }

    public void PostEyeData(
    float pupilDiaMeanRaw,
    float pupilDiaMeanSmoothed,
    float predictedPupilMm,
    float tepr,
    float luminanceY
)
    {
        StartCoroutine(PostEyeDataCoroutine(
            pupilDiaMeanRaw,
            pupilDiaMeanSmoothed,
            predictedPupilMm,
            tepr,
            luminanceY
        ));
    }

    public void SendStroopLog(
        string logUserId,
        string condition,
        int trialIndex,
        bool isPractice,
        bool isCorrect,
        float reactionTimeMs,
        string stimulusOnsetTime,
        string responseTime,
        string result
    )
    {
        // StroopManager側のuserIdが空の場合はRequestSenderの固定IDを使う。
        string safeUserId = string.IsNullOrWhiteSpace(logUserId) ? userId : logUserId;
        StartCoroutine(PostStroopLogCoroutine(
            safeUserId,
            condition,
            trialIndex,
            isPractice,
            isCorrect,
            reactionTimeMs,
            stimulusOnsetTime,
            responseTime,
            result
        ));
    }

    public void SendMentalArithmeticLog(
        string participantId,
        int blockId,
        string difficulty,
        int blockDurationSec,
        int trialIndex,
        int a,
        int b,
        int correctAnswer,
        string userAnswer,
        bool isCorrect,
        bool isSkipped,
        float reactionTimeMs,
        float blockElapsedTimeMs,
        string trialTimestamp
    )
    {
        string safeParticipantId =
            string.IsNullOrWhiteSpace(participantId) ? userId : participantId;

        StartCoroutine(PostMentalArithmeticLogCoroutine(
            safeParticipantId,
            blockId,
            difficulty,
            blockDurationSec,
            trialIndex,
            a,
            b,
            correctAnswer,
            userAnswer,
            isCorrect,
            isSkipped,
            reactionTimeMs,
            blockElapsedTimeMs,
            trialTimestamp
        ));
    }

    public void StartAlnalyzeCLCondition(bool flag)
    {
        if (flag)
        {
            StartCoroutine(GetAnalyzeHrSave());
        }
        isGetCLCondition = flag;
    }



    public void DisplayCLCondition(string condition)
    {
        textCL_Condition.text = condition;
    }

    public void DisplayIssueCondition(Dictionary<string, string> issue_settings)
    {
        text_issues[0].text = GetIssueSettingValue(issue_settings, "tempo");
        text_issues[1].text = GetIssueSettingValue(issue_settings, "guidance");
        text_issues[2].text = GetIssueSettingValue(issue_settings, "complexity");
        text_issues[3].text = GetIssueSettingValue(issue_settings, "stimulus");
        text_issues[4].text = GetIssueSettingValue(issue_settings, "break_policy");
        text_issues[5].text = GetIssueSettingValue(issue_settings, "feedback");
        text_issues[6].text = GetIssueSettingValue(issue_settings, "taste");
    }



    public string GetIssueSettingValue(Dictionary<string, string> issue_settings, string key)
    {
        if (issue_settings == null)
        {
            Debug.LogWarning("issue_settings is null");
            return null;
        }

        if (issue_settings.TryGetValue(key, out string value))
        {
            return key + ":" + value;
        }

        Debug.LogWarning($"issue_settings does not contain key: {key}");
        return null;
    }

    public CL_ConditionGetResponse GetCL_ConditionFromMain()
    {
        return resp_cl_temp;
    }

    private string NormalizeBaseUrl(string inputUrl)
    {
        string normalized = inputUrl.Trim().Trim('"').TrimEnd('/');

        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "http://" + normalized;
        }

        return normalized;
    }

    private string GetSafeBaseUrl()
    {
        baseUrl = NormalizeBaseUrl(baseUrl);
        return baseUrl;
    }

    // ---- status送信 ----
    public IEnumerator PostStatusFlag(string statusFlag)
    {
        string safeBase = GetSafeBaseUrl();
        string url = safeBase + "/api/status_post";

        var payload = new StatusPost
        {
            id = userId,
            status_flag = statusFlag,
            //timestamp_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            sent_at = DateTime.Now.ToString()
        };

        string json = JsonConvert.SerializeObject(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            Debug.Log($"STATUS_POST url=[{url}] payload={json}");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = (req.result == UnityWebRequest.Result.Success);
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif

            if (!ok)
                Debug.LogWarning($"STATUS_POST failed: code={req.responseCode}, err={req.error}, body={req.downloadHandler.text}");
            else
                Debug.Log($"STATUS_POST ok: {req.downloadHandler.text}");
        }
    }


    // ---- eye data送信 ----
    public IEnumerator PostEyeDataCoroutine(
        float pupilDiaMeanRaw,
        float pupilDiaMeanSmoothed,
        float predictedPupilMm,
        float tepr,
        float luminanceY
    )
    {
        string safeBase = GetSafeBaseUrl();
        string url = safeBase + "/api/EyeData";

        var payload = new List<EyeDataPost>
    {
        new EyeDataPost
        {
            userID = userId,
            pupilDiaMeanRaw = pupilDiaMeanRaw,
            pupilDiaMeanSmoothed = pupilDiaMeanSmoothed,
            predictedPupilMm = predictedPupilMm,
            tepr = tepr,
            luminanceY = luminanceY,
            sentAt = DateTime.Now.ToString(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            deviceIp = GetDeviceIpAddress()
        }
    };

        string json = JsonConvert.SerializeObject(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"EYEDATA_POST failed: {req.error}, body={req.downloadHandler.text}");
            }
            else
            {
                Debug.Log($"EYEDATA_POST ok: {req.downloadHandler.text}");
            }
        }
    }




    // ---- stroop log送信 ----
    // StroopManagerから受け取った1試行分の結果を/api/stroop_logへ送る。
    // サーバ側ではuser_idごとにJSON Lines形式で追記保存する。
    public IEnumerator PostStroopLogCoroutine(
        string logUserId,
        string condition,
        int trialIndex,
        bool isPractice,
        bool isCorrect,
        float reactionTimeMs,
        string stimulusOnsetTime,
        string responseTime,
        string result
    )
    {
        string safeBase = GetSafeBaseUrl();
        string url = safeBase + "/api/stroop_log";

        var payload = new StroopLogPost
        {
            user_id = string.IsNullOrWhiteSpace(logUserId) ? userId : logUserId,
            condition = condition,
            trial_index = trialIndex,
            is_practice = isPractice,
            is_correct = isCorrect,
            reaction_time_ms = reactionTimeMs,
            stimulus_onset_time = stimulusOnsetTime,
            response_time = responseTime,
            result = result,
            sent_at = DateTime.Now.ToString(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            deviceIp = GetDeviceIpAddress()
        };

        string json = JsonConvert.SerializeObject(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            Debug.Log($"STROOP_LOG_POST url=[{url}] payload={json}");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = (req.result == UnityWebRequest.Result.Success);
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif

            if (!ok)
            {
                Debug.LogWarning($"STROOP_LOG_POST failed: code={req.responseCode}, err={req.error}, body={req.downloadHandler.text}");
            }
            else
            {
                Debug.Log($"STROOP_LOG_POST ok: {req.downloadHandler.text}");
            }
        }
    }

    // ---- mental arithmetic log送信 ----
    // MentalArithmeticManagerから受け取った1試行分をサーバへ送信する。
    public IEnumerator PostMentalArithmeticLogCoroutine(
        string participantId,
        int blockId,
        string difficulty,
        int blockDurationSec,
        int trialIndex,
        int a,
        int b,
        int correctAnswer,
        string userAnswer,
        bool isCorrect,
        bool isSkipped,
        float reactionTimeMs,
        float blockElapsedTimeMs,
        string trialTimestamp
    )
    {
        string safeBase = GetSafeBaseUrl();
        string url = safeBase + "/api/mental_arithmetic_log";

        var payload = new MentalArithmeticLogPost
        {
            participant_id = string.IsNullOrWhiteSpace(participantId) ? userId : participantId,
            block_id = blockId,
            difficulty = difficulty,
            block_duration_sec = blockDurationSec,
            trial_index = trialIndex,
            a = a,
            b = b,
            correct_answer = correctAnswer,
            user_answer = userAnswer,
            is_correct = isCorrect,
            is_skipped = isSkipped,
            reaction_time_ms = reactionTimeMs,
            block_elapsed_time_ms = blockElapsedTimeMs,
            trial_timestamp = trialTimestamp,
            sent_at = DateTime.Now.ToString(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            deviceIp = GetDeviceIpAddress()
        };

        string json = JsonConvert.SerializeObject(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            Debug.Log($"MENTAL_ARITHMETIC_LOG_POST url=[{url}] payload={json}");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = (req.result == UnityWebRequest.Result.Success);
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif

            if (!ok)
            {
                Debug.LogWarning(
                    $"MENTAL_ARITHMETIC_LOG_POST failed: code={req.responseCode}, " +
                    $"err={req.error}, body={req.downloadHandler.text}");
            }
            else
            {
                Debug.Log($"MENTAL_ARITHMETIC_LOG_POST ok: {req.downloadHandler.text}");
            }
        }
    }


    // ---- PLRモデル推定リクエスト送信 ----
    // calibrationSamplesには、キャリブレーション中に取得した輝度Yと瞳孔径mmのペアを入れる
    // onSuccessには、FastAPIからa,b,cが返ってきた後に実行したい処理を渡す
    public void PostPLRFitRequest(
        List<PLRCalibrationSample> calibrationSamples,
        Action<PLRFitResponse> onSuccess = null
    )
    {
        StartCoroutine(PostPLRFitRequestCoroutine(calibrationSamples, onSuccess));
    }

    public IEnumerator PostPLRFitRequestCoroutine(
        List<PLRCalibrationSample> calibrationSamples,
        Action<PLRFitResponse> onSuccess = null
    )
    {
        if (calibrationSamples == null || calibrationSamples.Count == 0)
        {
            Debug.LogWarning("PLR_FIT_POST skipped: calibrationSamples is empty.");
            yield break;
        }

        string safeBase = GetSafeBaseUrl();
        string url = safeBase + "/api/plr/fit";

        var payload = new PLRCalibrationRequest
        {
            userID = userId,
            sentAt = DateTime.Now.ToString(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            deviceIp = GetDeviceIpAddress(),
            samples = calibrationSamples
        };

        string json = JsonConvert.SerializeObject(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            Debug.Log($"PLR_FIT_POST url=[{url}] sampleCount={calibrationSamples.Count}");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = (req.result == UnityWebRequest.Result.Success);
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif

            if (!ok)
            {
                Debug.LogWarning($"PLR_FIT_POST failed: code={req.responseCode}, err={req.error}, body={req.downloadHandler.text}");
                yield break;
            }

            var resp = JsonConvert.DeserializeObject<PLRFitResponse>(req.downloadHandler.text);
            if (resp == null)
            {
                Debug.LogWarning($"PLR_FIT_POST parse failed: body={req.downloadHandler.text}");
                yield break;
            }

            if (!resp.ok)
            {
                Debug.LogWarning($"PLR_FIT_POST server returned ok=false: error={resp.error}, body={req.downloadHandler.text}");
                yield break;
            }

            Debug.Log($"PLR_FIT_POST ok: userID={resp.userID}, a={resp.a}, b={resp.b}, c={resp.c}, mse={resp.mse}, n={resp.sampleCount}");
            onSuccess?.Invoke(resp);
        }
    }

    private string GetDeviceIpAddress()
    {
        try
        {
            foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return address.ToString();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"GetDeviceIpAddress failed: {e.Message}");
        }

        return "unknown";
    }

    // ---- status取得（GET）----
    public IEnumerator GetExpStatus()
    {
        string safeBase = GetSafeBaseUrl();
        string url = safeBase + "/api/status_get?id=" + UnityWebRequest.EscapeURL(userId);

        using (var req = UnityWebRequest.Get(url))
        {
            Debug.Log($"STATUS_GET url=[{url}]");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = (req.result == UnityWebRequest.Result.Success);
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif

            if (!ok)
            {
                Debug.LogWarning($"STATUS_GET failed: code={req.responseCode}, err={req.error}, body={req.downloadHandler.text}");
                yield break;
            }

            var resp = JsonConvert.DeserializeObject<StatusGetResponse>(req.downloadHandler.text);
            Debug.Log($"STATUS_GET ok: id={resp.id}, status={resp.status}");
        }
    }

    // 心拍の解析・閾値設定指示　unityから
    public IEnumerator GetAnalyzeHrSave()
    {
        string safeBase = GetSafeBaseUrl();
        string url = safeBase + "/api/analyze_hr/set_threshold?id=" + UnityWebRequest.EscapeURL(userId);

        using (var req = UnityWebRequest.Get(url))
        {
            Debug.Log($"ANALYZE_HR_SAVE_GET url=[{url}]");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = (req.result == UnityWebRequest.Result.Success);
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif

            if (!ok)
            {
                Debug.LogWarning($"ANALYZE_HR_SAVE_GET failed: code={req.responseCode}, err={req.error}, body={req.downloadHandler.text}");
                yield break;
            }

            var resp = JsonConvert.DeserializeObject<StatusGetResponse>(req.downloadHandler.text);
            Debug.Log($"ANALYZE_HR_SAVE_GET ok: code={req.responseCode}, body={req.downloadHandler.text}");
        }
    }

    IEnumerator RequestLoop()
    {
        while (true)
        {
            if (isGetCLCondition)
            {
                bool intervalPassed = (Time.time - CLLastRequestStartTime) >= CLIntervalSec;

                if (!isCLRequestRunning && intervalPassed)
                {
                    CLLastRequestStartTime = Time.time;
                    StartCoroutine(GetCLCondition());
                }
            }
            yield return null; // 毎フレーム確認
        }
    }

    // 認知負荷状態の取得（get）
    public IEnumerator GetCLCondition()
    {
        isCLRequestRunning = true;
        string safeBase = GetSafeBaseUrl();
        string url = safeBase + "/api/cl_condition_get?id=" + UnityWebRequest.EscapeURL(userId);

        using (var req = UnityWebRequest.Get(url))
        {
            Debug.Log($"CL_CONDITION_GET url=[{url}]");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = (req.result == UnityWebRequest.Result.Success);
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif

            if (!ok)
            {
                Debug.LogWarning($"CL_CONDITION_GET failed: code={req.responseCode}, err={req.error}, body={req.downloadHandler.text}");
                isCLRequestRunning = false;
                yield break;
            }

            var resp = JsonConvert.DeserializeObject<CL_ConditionGetResponse>(req.downloadHandler.text);
            if (resp == null)
            {
                Debug.LogWarning($"CL_CONDITION_GET parse failed: body={req.downloadHandler.text}");
                isCLRequestRunning = false;
                yield break;
            }

            if (!resp.ok)
            {
                Debug.LogWarning($"CL_CONDITION_GET server returned ok=false: id={resp.id}, error={resp.error}, body={req.downloadHandler.text}");
                resp_cl_temp = resp;
                isCLRequestRunning = false;
                yield break;
            }

            string issueSettingsJson = JsonConvert.SerializeObject(resp.issue_settings);
            string mlResultJson = JsonConvert.SerializeObject(resp.ml_result);
            Debug.Log($"CL_CONDITION_GET ok: id={resp.id}, cl_condition={resp.cl_condition}, ml_result={mlResultJson}, issue_settings={issueSettingsJson}");
            resp_cl_temp = resp;
            DisplayCLCondition(resp.cl_condition);
            if (resp.issue_settings != null)
            {
                DisplayIssueCondition(resp.issue_settings);
            }


        }
        isCLRequestRunning = false;
        // yield return new WaitForSeconds(1.0f);
    }


    void OnDisable()
    {
        if (baseUrlInputField != null)
        {
            baseUrlInputField.onEndEdit.RemoveListener(SetBaseUrl);
        }

        if (cl_coroutine != null)
        {
            StopCoroutine(cl_coroutine);
            //SendEndFlag();
            cl_coroutine = null;
        }
    }
}
