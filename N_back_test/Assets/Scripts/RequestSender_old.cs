// // using System;
// // using System.Collections;
// // using System.Collections.Generic;
// // using System.Net.Http;
// // using System.Text;
// // using System.Threading;
// // using System.Threading.Tasks;
// // using Unity.Android.Gradle.Manifest;
// // using UnityEditor.Build.Content;
// // using UnityEngine;



// // public class RequestSender : MonoBehaviour
// // {
// //     private  HttpClient httpClient;

// //     void TestApiClient(string baseUrl)
// //     {
// //         httpClient = new HttpClient
// //         {
// //             BaseAddress = new Uri("http://10.65.29.45:8080/api/hr/")
// //         };
// //     }

// //     public async Task<string> PostTextAsync(string endpoint, string textContent)
// //     {
// //         // postリクエストの作成
// //         var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
// //         {
// //             Content = new StringContent(textContent, Encoding.UTF8)
// //         };

// //         HttpResponseMessage response = await httpClient.SendAsync(request);
// //         response.EnsureSuccessStatusCode();

// //         string result = await response.Content.ReadAsStringAsync();
// //         return result;
// //     }

// //     public void SendPost()
// //     {
// //         var jsonData = "{\"title\": \"foo\", \"body\": \"bar\", \"userId\": 1}";
// //         //var response;
// //         Task.Run(() => PostTextAsync("post",jsonData));
// //         Debug.Log("PostSent");
// //         return;
// //     }
// // }


// using System;
// using System.Collections;
// using System.Collections.Generic;
// using System.Text;
// using UnityEngine;
// using UnityEngine.Networking;
// using Newtonsoft.Json;   // ★Json.NET


// public class RequestSender : MonoBehaviour
// {
//     [Header("FastAPI server base URL (PC IP)")]
//     public string baseUrl = "http://127.0.0.1:8080/";

//     [Header("Send interval (seconds)")]
//     public float sendIntervalSec = 3f;
//     private Coroutine sendLoop;

//     [Serializable]
//     public class TrackedData
//     {
//         public int hr;
//         public List<int> ibi;
//         public long timestamp_ms;
//     }

//     private void Start()
//     {
//         sendLoop = StartCoroutine(SendLoop());
//     }

//     private void OnDisable()
//     {
//         if (sendLoop != null)
//         {
//             StopCoroutine(sendLoop);
//             sendLoop = null;
//         }
//     }

//     private void OnDestroy()
//     {
//         if (sendLoop != null)
//         {
//             StopCoroutine(sendLoop);
//             sendLoop = null;
//         }
//     }


//     private IEnumerator SendLoop()
//     {
//         while (true)
//         {
//             // ★サーバは「配列」を期待するので List<TrackedData> を送る
//             var batch = new List<TrackedData>
//             {
//                 new TrackedData {
//                     hr = UnityEngine.Random.Range(60, 90),
//                     ibi = new List<int>{ 820, 810 } ,
//                     timestamp_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
//                     },
//                 new TrackedData {
//                     hr = UnityEngine.Random.Range(60, 90),
//                     ibi = new List<int>{ 790 },
//                     timestamp_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
//                     }
//             };

//             yield return PostJsonArray("/api/hr", batch);
//             yield return new WaitForSeconds(sendIntervalSec);
//         }
//     }

//     private IEnumerator PostJsonArray(string path, List<TrackedData> batch)
//     {
//         string url = baseUrl.TrimEnd('/') + path;

//         // ★配列JSONにする
//         string json = JsonConvert.SerializeObject(batch);
//         byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

//         using (var req = new UnityWebRequest(url, "POST"))
//         {
//             req.uploadHandler = new UploadHandlerRaw(bodyRaw);
//             req.downloadHandler = new DownloadHandlerBuffer();
//             req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

//             // req.timeout = 30;
//             Debug.Log("POST url=" + url);
//             Debug.Log("JSON=" + json);

//             yield return req.SendWebRequest();

// #if UNITY_2020_2_OR_NEWER
//             bool ok = (req.result == UnityWebRequest.Result.Success);
// #else
//             bool ok = !(req.isNetworkError || req.isHttpError);
// #endif

//             if (!ok)
//             {
//                 Debug.LogWarning($"POST failed: code={req.responseCode}, err={req.error}, body={req.downloadHandler.text}");
//             }
//             else
//             {
//                 Debug.Log($"POST ok: code={req.responseCode}, resp={req.downloadHandler.text}");
//             }
//         }
//     }
// }


