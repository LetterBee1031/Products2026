using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.IO;


public class N_back : MonoBehaviour
{
    public static class Define
    {
        public static readonly int LIST_MAX_LENGTH = 100;

    }

    RequestSender requestSender;

    public TextMeshProUGUI[] TextAlphabet = new TextMeshProUGUI[26];
    public TextMeshProUGUI textResult = new TextMeshProUGUI();
    public TextMeshProUGUI textQuestionNum = new TextMeshProUGUI();
    public TextMeshProUGUI textTitle = new TextMeshProUGUI();
    public int n_back_num; // n-back の n, 何バックのときを指定するか
    public float timeWaitOneTask = 2f;
    public float timeLimit = 120f; // 1タスク全体の時間
    private float timeHoleTask = 0f; // 経過時間
    private float timeOneTask = 0f; // タスク中の時間
    bool isWorking = false; // n-back課題中か
    bool isButtonSamePressed = false; // 
    bool isJudgeAdded = false; // 
    bool isTextDisplayed = false;

    int outTextCount = 0;
    int outTextNum = 50;

    List<int> listOutTextNum = new List<int>(); // ランダムで出力された文字のリスト
    List<bool> listJudge = new List<bool>(); // 正解したかどうかのリスト
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        requestSender = GetComponent<RequestSender>();
        Debug.Log("System Start");
        for (int i = 0; i < Define.LIST_MAX_LENGTH; i++)
        {
            listOutTextNum.Add(-1);
            listJudge.Add(false);
        }
        textTitle.text = "N back test\n" + n_back_num.ToString() + " back mode";
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
    public void SetNback(bool flag)
    {
        // Coroutine coroutine;
        if (flag)
        {
            //time = 0f;
            isWorking = true;
            Debug.Log("SetNback");
            requestSender.SendNbackStartFlag(n_back_num);
            //N_Back_Working();
        }
        // else
        // {
        //     //time = 0f;
        //     isWorking = false;
        //     StopCoroutine(coroutine);
        // }
    }

    public void SetNbackNum(int n)
    {
        n_back_num = n;
        textTitle.text = "N back test\n" + n_back_num.ToString() + " back mode";
    }

    public void OnSameButton()
    {
        isButtonSamePressed = true;
        Debug.Log("SamePressed");
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
            //int outTextNum = 50;

            //timeHoleTask = 0f;
            //timeOneTask = 0f;
            textResult.enabled = false;

            // N-backタスク全体の制限時間中
            if (timeHoleTask < timeLimit)
            {
                if (!isTextDisplayed)
                {
                    //Debug.Log("N_back_Working. OutTextCount:" + outTextCount);
                    // 全文字非表示
                    for (int j = 0; j < TextAlphabet.Length; j++)
                    {
                        TextAlphabet[j].enabled = false;
                    }

                    // ランダムな文字を表示
                    //outTextNum = Random.Range(0, TextAlphabet.Length);
                    outTextNum = Random.Range(0, 5);
                    TextAlphabet[outTextNum].enabled = true;
                    textQuestionNum.text = outTextCount.ToString();

                    listOutTextNum[outTextCount] = outTextNum; //出力した文字列に追加
                    //Debug.Log("OutTextCount：" + outTextCount + "outTextNum：" + outTextNum);
                    isTextDisplayed = true;
                }

                // 1文字ごとの制限時間中
                if (timeOneTask < timeWaitOneTask)
                {
                    if (outTextCount >= n_back_num)
                    {
                        if (!isJudgeAdded)
                        {
                            // ボタン押下が合ってたら
                            if ((outTextNum == listOutTextNum[outTextCount - n_back_num]) && (isButtonSamePressed == true))
                            {
                                listJudge[outTextCount] = true;
                                isJudgeAdded = true;
                                Debug.Log("ButtonPush: true, outTextNum: " + outTextNum + " n個前: " + listOutTextNum[outTextCount - n_back_num]);
                            }
                            // ボタン押下が合ってなかったら
                            else if ((outTextNum != listOutTextNum[outTextCount - n_back_num]) && (isButtonSamePressed == true))
                            {
                                listJudge[outTextCount] = false;
                                isJudgeAdded = true;
                                Debug.Log("ButtonPush: false, outTextNum: " + outTextNum + " n個前: " + listOutTextNum[outTextCount - n_back_num]);
                            }
                        }
                    }
                }
                else
                {
                    // 1文字ごとの時間内にボタンが押されなかったら
                    if ((outTextCount >= n_back_num) && (isJudgeAdded == false))
                    {
                        if (outTextNum == listOutTextNum[outTextCount - n_back_num])
                        {
                            listJudge[outTextCount] = false;
                            isJudgeAdded = true;
                            Debug.Log("NoButtonPush: false, outTextNum: " + outTextNum + " n個前: " + listOutTextNum[outTextCount - n_back_num]);
                        }
                        else
                        {
                            listJudge[outTextCount] = true;
                            isJudgeAdded = true;
                            Debug.Log("NoButtonPush: true, outTextNum: " + outTextNum + " n個前: " + listOutTextNum[outTextCount - n_back_num]);
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

                if ((outTextCount >= n_back_num) && (isJudgeAdded == false))
                    {
                        if (outTextNum == listOutTextNum[outTextCount - n_back_num])
                        {
                            listJudge[outTextCount] = false;
                            isJudgeAdded = true;
                            Debug.Log("NoButtonPush: false");
                        }
                        else
                        {
                            listJudge[outTextCount] = true;
                            isJudgeAdded = true;
                            Debug.Log("NoButtonPush: true");
                        }
                    }

                    isJudgeAdded = false;
                    isButtonSamePressed = false;
                    isTextDisplayed = false;
                    timeOneTask = 0f;

                for (int i = 0; i < TextAlphabet.Length; i++)
                {
                    TextAlphabet[i].enabled = false;
                }

                foreach (var val in listJudge)
                {
                    if (val == true)
                    {
                        resultNum++;
                    }
                }

                

                textResult.text = resultNum.ToString() + "/" + (outTextCount+1-n_back_num).ToString();
                textResult.enabled = true;

                for(int i = 0;i <= outTextCount; i++)
                {
                    Debug.Log(i + "番目：" + listOutTextNum[i]);
                }

                // 初期化
                outTextCount = 0;
                timeHoleTask = 0f;
                timeOneTask = 0f;

                for (int i = 0; i < Define.LIST_MAX_LENGTH; i++)
                {
                    listOutTextNum[i] = -1;
                }
                for (int i = 0; i < Define.LIST_MAX_LENGTH; i++)
                {
                    listJudge[i] = false;
                }
                requestSender.SendNbackEndFlag(n_back_num);
                Debug.Log("N_back End. Out Text Count:" + outTextCount);
            }
        }



        // for (int i = 0;i < TextAlphabet.Length; i++)
        // {
        //     for(int j = 0;j < TextAlphabet.Length; j++)
        //     {
        //         TextAlphabet[j].enabled = false;
        //     }
        //     TextAlphabet[i].enabled = true;
        //     Debug.Log("Text" + i);
        //     yield return new WaitForSeconds(2f);
        // }
    }
}
