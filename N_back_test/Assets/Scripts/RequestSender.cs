using System;
using System.Collections;
using System.Collections.Generic;
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

    [Header("Fixed user id")]
    public string userId = "01";

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
        SendStartFlag();
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

    // ---- status送信 ----
    public IEnumerator PostStatusFlag(string statusFlag)
    {
        string safeBase = baseUrl.Trim().Trim('"').TrimEnd('/');
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


    // ---- status取得（GET）----
    public IEnumerator GetExpStatus()
    {
        string safeBase = baseUrl.Trim().Trim('"').TrimEnd('/');
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
        string safeBase = baseUrl.Trim().Trim('"').TrimEnd('/');
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
        string safeBase = baseUrl.Trim().Trim('"').TrimEnd('/');
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
        if (cl_coroutine != null)
        {
            StopCoroutine(cl_coroutine);
            //SendEndFlag();
            cl_coroutine = null;
        }
    }
}
