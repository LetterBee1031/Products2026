using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Threading;
//using Unity.VisualScripting;
//using UnityEditor.Purchasing;

//Exp：Explain
//Exprc：Experience

// システム全体のコントローラースクリプト
public class MainController : MonoBehaviour
{
    MonitorController monitorController;
    AudioController audioController;
    DoorOpen doorOpen;
    DoorClose doorClose;
    PointMove pointMove;
    Pause pause;
    DisplayFlashing flashing;
    UIController uiController;

    //日本語テキスト系


    //矢印
    public GameObject[] arrows = new GameObject[6];
    int arrowSt = 0, arrowExEnd = 1, arrowDisaEnd1 = 2, arrowDisaEnd2 = 3, arrowInDisp = 4, arrowOutDisp = 5;

    // 停電時，エレベーターを暗闇にするための箱のレンダラー
    public Renderer blackOutCubeRend;

    public Light evLight; // エレベーター内部の照明
    public Light dlLight; // 全体の照明
    public Renderer flashingInside;
    public Renderer flashingOutside;

    int eventMode = -5; // 火災:0 冠水:1 地震：2 停電：3
    public int langMode = 0;

    float li = 0; // 照明の明るさの数値

    bool isWorking = false; // 体験中か
    bool isExplainEnd = false; // 災害時説明が終了したか
    bool isRode = false; // エレベーターへの乗り込みが終了し，体験が始まったか
    bool isInsideEnd = false; //エレベーター内ディスプレイの説明が終了したか
    bool isStopFloorEnd = false; //停止階に関する説明が終了したか
    bool isOutsideEnd = false; //エレベーター外ディスプレイの説明が終了したか
    float workTime = 0; // 各動作のタイミング管理用タイマー

    int eventProgress = 0; // 体験の進行段階の管理

    int upDownTime = 5; // 階数移動にかかる時間
    //float waitTime = 0.0f;
    int fire = 0, rain = 1, earth = 2, elect = 3; //火災:0 冠水:1 地震：2 停電：3

    int textProgress = 1;

    // Start is called before the first frame update
    void Start()
    {
        // 各スクリプトのインスタンス取得
        monitorController = GetComponent<MonitorController>();
        audioController = GetComponent<AudioController>();
        doorOpen = GetComponent<DoorOpen>();
        doorClose = GetComponent<DoorClose>();
        pointMove = GameObject.Find("Master").GetComponent<PointMove>();
        pause = GetComponent<Pause>();
        flashing = GetComponent<DisplayFlashing>();
        uiController = GetComponent<UIController>();

        // // 照明の取得
        // evLight = GameObject.Find("PointLight").GetComponent<Light>();
        // dlLight = GameObject.Find("Directional Light").GetComponent<Light>();


        // // 進行方向指示用矢印オブジェクトの取得
        // arrows[0] = GameObject.Find("ArrowStart");
        // arrows[1] = GameObject.Find("ArrowExplainEnd");
        // arrows[2] = GameObject.Find("ArrowDisasterEnd1");
        // arrows[3] = GameObject.Find("ArrowDisasterEnd2");
        // arrows[4] = GameObject.Find("ArrowInsideDisplay");
        // arrows[5] = GameObject.Find("ArrowOutsideDisplay");

        // flashingInside = GameObject.Find("FlashingInsideDisplay").GetComponent<Renderer>();
        // flashingOutside = GameObject.Find("FlashingOutsideDisplay").GetComponent<Renderer>();

        // blackOutCubeRend = GameObject.Find("BlackOutCube").GetComponent<Renderer>();

        monitorController.elevatorState = 3;

        // オブジェクトの非表示化
        blackOutCubeRend.enabled = false;

        // ArrowStartは非表示化してほしくないので，i = 1からループ開始してる
        for (int i = 1; i < arrows.Length; i++)
        {
            arrows[i].SetActive(false);
        }
        //エレベータ災害体験システムです
        Debug.Log("MainController: エレベーター災害体験システムです");
        audioController.PlayExplainCommonSound(0,langMode);

        // 各UIの有効化・無効化
        uiController.SetSelectUI(true);
        uiController.SetBeforeExprcExpUI("false",false);
        uiController.SetStartUI(false);
        uiController.SetExpInsideUI(-1,false);
        uiController.SetExpStopFloorUI(-1,false);
        uiController.SetExpOutsideUI(-1,false);
        uiController.SetEndUI(false);
        uiController.SetLanguage(0);
    }

    private void FixedUpdate()
    {
        //火災体験
        if (eventMode == fire)
        {

            //Debug.Log("火災");//1Fに向かう
            if ((workTime <= 2) && (eventProgress == 0))
            {
                monitorController.isStop = false;
                monitorController.isUp = false;
            }

            if ((workTime > 2) && (workTime < 4) && (eventProgress == 0))
            {

                eventProgress = 1;
            }

            if ((workTime >= 4) && (eventProgress == 1))
            {
                //災害体験の開始
                audioController.PlayEffectSound(fire);  // 火災警報音
                audioController.PlayExplainHappenSound(fire,langMode); // 火災が発生しました

                monitorController.currentFloor = 5;

                eventProgress = 2;
            }
            // エレベーター下降
            if ((workTime >= 4 + upDownTime * 1) && (eventProgress == 2))
            {
                monitorController.mode = eventMode;
                monitorController.elevatorState = 1;
                audioController.PlayDisasterSound(); // 火災です、避難階へとまります
                monitorController.currentFloor = 4;
                eventProgress = 3;

            }
            if ((workTime >= 4 + upDownTime * 2) && (eventProgress == 3))
            {
                monitorController.currentFloor = 3;
                SetExpInside(true);
                eventProgress = 4;
            }
            if ((workTime >= 4 + upDownTime * 3) && (eventProgress == 4))
            {
                monitorController.currentFloor = 2;
                eventProgress = 5;
            }
            // 避難階到着
            if ((workTime >= 4 + upDownTime * 4) && (eventProgress == 5))
            {
                monitorController.isStop = true;
                monitorController.currentFloor = 1;
                audioController.PlayFloorSound(1); // 「1階です」
                eventProgress = 6;
            }

            if ((workTime >= 4 + upDownTime * 4.25) && (eventProgress == 6))
            {
                SetExpStopFloor(true);
                eventProgress = 7;
            }
            if ((workTime >= 4 + upDownTime * 4.5) && (eventProgress == 7))
            {
                audioController.PlaySystemSound(2); // 「扉が開きます」
                monitorController.elevatorState = 2;
                eventProgress = 8;

                SetExpOutside(true);
                arrows[arrowDisaEnd1].SetActive(true);
                arrows[arrowDisaEnd2].SetActive(true);
                //arrowDisasterEnd.SetActive(true);
            }
            // 避難開始
            if ((workTime >= 4 + upDownTime * 5) && (eventProgress == 8))
            {
                // ドア開く
                doorOpen.Open();
                audioController.PlayExplainCommonSound(3,langMode); // 避難階に到着したようです
                //waitTime = audioController.explainCommonSounds[3].length;

                if(langMode == 0){
                    StartCoroutine(AudioCor(audioController.jpExplainCommonSounds[4], 4));
                    StartCoroutine(AudioCor(audioController.jpExplainOutsideDisplay[4], 4));
                    StartCoroutine(AudioCor(audioController.jpExplainOutsideDisplay[eventMode], 4));
                } else if(langMode == 1){
                    StartCoroutine(AudioCor(audioController.enExplainCommonSounds[4], 4));
                    StartCoroutine(AudioCor(audioController.enExplainOutsideDisplay[4], 4));
                    StartCoroutine(AudioCor(audioController.enExplainOutsideDisplay[eventMode], 4));
                }

                eventProgress = 9;
            }
            if ((workTime >= 4 + upDownTime * 6) && (eventProgress == 9)){
                audioController.StopEffectAudio();
                eventProgress = 10;
            }

        }
        // 冠水体験
        else if (eventMode == rain)
        {
            //Debug.Log("冠水");//1Fを避けて止まる

            if ((workTime <= 2) && (eventProgress == 0))
            {
                monitorController.isStop = false;
                monitorController.isUp = true;
            }

            if ((workTime > 2) && (workTime < 4) && (eventProgress == 0))
            {
                eventProgress = 1;
            }

            if ((workTime >= 4) && (eventProgress == 1))
            {
                // 冠水体験開始
                audioController.PlayExplainHappenSound(rain,langMode); // 「冠水が発生しました」
                
                monitorController.currentFloor = 1;


                eventProgress = 2;
            }
            // エレベーター上昇
            if ((workTime >= 4 + upDownTime * 1) && (eventProgress == 2))
            {
                monitorController.mode = eventMode;
                monitorController.elevatorState = 0;
                audioController.PlayDisasterSound(); // 「冠水です．管制運転を行います」
                
                monitorController.currentFloor = 1;
                eventProgress = 3;
            }
            if ((workTime >= 4 + upDownTime * 1.9) && (eventProgress == 3))
            {
                SetExpInside(true);
                eventProgress = 4;
            }
            // 避難階到着
            if ((workTime >= 4 + upDownTime * 2) && (eventProgress == 4))
            {
                monitorController.isStop = true;
                monitorController.currentFloor = 2;
                audioController.PlayFloorSound(2); // 「2階です」
                eventProgress = 5;
            }
            if ((workTime >= 4 + upDownTime * 2.25) && (eventProgress == 5))
            {
                SetExpStopFloor(true);
                eventProgress = 6;
            }
            if ((workTime >= 4 + upDownTime * 2.5) && (eventProgress == 6))
            {
                audioController.PlaySystemSound(2); // 「扉が開きます」
                monitorController.elevatorState = 2;

                SetExpOutside(true);
                arrows[arrowDisaEnd1].SetActive(true);
                arrows[arrowDisaEnd2].SetActive(true);
                //arrowDisasterEnd.SetActive(true);
            eventProgress = 7;
            }
            // 避難開始
            if ((workTime >= 4 + upDownTime * 3) && (eventProgress == 7))
            {
                // ドア開く
                doorOpen.Open();
                audioController.PlayExplainCommonSound(3,langMode); // 避難階に到着したようです

                if(langMode == 0){
                    StartCoroutine(AudioCor(audioController.jpExplainCommonSounds[4], 4));
                    StartCoroutine(AudioCor(audioController.jpExplainOutsideDisplay[4], 4));
                    StartCoroutine(AudioCor(audioController.jpExplainOutsideDisplay[eventMode], 4));
                } else if(langMode == 1){
                    StartCoroutine(AudioCor(audioController.enExplainCommonSounds[4], 4));
                    StartCoroutine(AudioCor(audioController.enExplainOutsideDisplay[4], 4));
                    StartCoroutine(AudioCor(audioController.enExplainOutsideDisplay[eventMode], 4));
                }

                eventProgress = 8;
            }
        }
        // 地震体験
        else if (eventMode == earth)
        {
            //Debug.Log("地震");

            if ((workTime <= 2) && (eventProgress == 0))
            {
                monitorController.isStop = false;
                monitorController.isUp = false;
            }

            if ((workTime > 2) && (workTime < 4) && (eventProgress == 0))
            {
                eventProgress = 1;
            }

            if ((workTime >= 4) && (eventProgress == 1))
            {
                audioController.PlayExplainHappenSound(earth,langMode); // 地震が発生しました
                audioController.PlayEffectSound(earth); // 地震音声

                //地震開始
                pointMove.MoveStart();
                eventProgress = 2;
            }
            if ((workTime > 4) && (eventProgress == 2))
            {
                // 体験開始
                monitorController.mode = 4;
                monitorController.elevatorState = 0;
                monitorController.currentFloor = 5;
                eventProgress = 3;
            }
            // エレベーター下降
            if ((workTime >= 4 + upDownTime * 1) && (eventProgress == 3))
            {
                monitorController.mode = eventMode;
                monitorController.elevatorState = 4;

                monitorController.currentFloor = 4;

                audioController.PlayDisasterSound(); // 「地震です．避難階に止まります」

                eventProgress = 4;
            }
            if ((workTime >= 4 + upDownTime * 2.5) && (eventProgress == 4))
            {
                SetExpInside(true);
                eventProgress = 5;
            }
            // 避難階到着
            if ((workTime >= 4 + upDownTime * 3) && (eventProgress == 5))
            {
                //地震終了
                pointMove.MoveEnd();
                audioController.StopEffectAudio(); // 地震音声の停止
                monitorController.isStop = true;
                monitorController.currentFloor = 3;
                audioController.PlayFloorSound(3); // 「3階です」
                eventProgress = 6;
            }
            if ((workTime >= 4 + upDownTime * 3.25) && (eventProgress == 6))
            {
                SetExpStopFloor(true);
                eventProgress = 7;
            }
            if ((workTime >= 4 + upDownTime * 3.5) && (eventProgress == 7))
            {
                audioController.PlaySystemSound(2); // 「扉が開きます」
                monitorController.elevatorState = 2;
                eventProgress = 8;

                SetExpOutside(true);
                arrows[arrowDisaEnd1].SetActive(true);
                arrows[arrowDisaEnd2].SetActive(true);
                //arrowDisasterEnd.SetActive(true);
            }
            // 避難開始
            if ((workTime >= 4 + upDownTime * 4) && (eventProgress == 8))
            {
                // ドア開く
                doorOpen.Open();
                audioController.PlayExplainCommonSound(3,langMode); // 避難階に到着したようです

                if(langMode == 0){
                    StartCoroutine(AudioCor(audioController.jpExplainCommonSounds[4], 4));
                    StartCoroutine(AudioCor(audioController.jpExplainOutsideDisplay[4], 4));
                    StartCoroutine(AudioCor(audioController.jpExplainOutsideDisplay[eventMode], 4));
                } else if(langMode == 1){
                    StartCoroutine(AudioCor(audioController.enExplainCommonSounds[4], 4));
                    StartCoroutine(AudioCor(audioController.enExplainOutsideDisplay[4], 4));
                    StartCoroutine(AudioCor(audioController.enExplainOutsideDisplay[eventMode], 4));
                }

                eventProgress = 9;
            }
            /*
            if ((workTime >= 4 + upDownTime * 8) && (eventProgress == 7))
            {
                audioController.PlayExplainCommonSound(4); // 脱出が完了しました
                doorClose.Close();
                li -= (10f / (4 * 150));
                monitorController.currentFloor = 2;
                monitorController.elevatorState = 3;
                evLight.range = li;

                eventProgress = 8;
            }
            */
        }
        // 停電体験
        else if (eventMode == elect)
        {
            //Debug.Log("停電");

            if ((workTime <= 2) && (eventProgress == 0))
            {
                monitorController.isStop = false;
                monitorController.isUp = false;
            }

            if ((workTime > 2) && (workTime < 4) && (eventProgress == 0))
            {
                eventProgress = 1;
            }
            if ((workTime >= 4) && (eventProgress == 1))
            {
                // 体験開始
                monitorController.mode = 4;
                monitorController.elevatorState = 3;
                monitorController.currentFloor = 5;

                //消灯
                li = 0;
                evLight.range = li;
                dlLight.enabled = false;
                audioController.PlayEffectSound(elect); // エレベータ急停止音

                pointMove.ElectStop();
                ///blackOutCubeRend.enabled = true;

                audioController.PlayExplainHappenSound(elect,langMode); // 停電が発生しました

                eventProgress = 2;
            }
            if ((workTime >= 5) && (eventProgress == 2))
            {
                // 停電灯点灯
                evLight.color = new Color(255f / 255f, 229f / 255f, 153f / 255f);
                li = 1;
                evLight.range = li;
                dlLight.enabled = true;
                //blackOutCubeRend.enabled = false;

                eventProgress = 3;
            }
            if ((workTime >= 14 + upDownTime * 1) && (eventProgress == 3))
            {
                // エレベーターディスプレイ復旧
                monitorController.mode = eventMode;
                monitorController.elevatorState = 0;
                monitorController.currentFloor = 4;
                monitorController.isUp = true;

                audioController.PlayDisasterSound(); // 「停電です．救出運転中です」


                eventProgress = 4;
            }
            if ((workTime >= 10 + upDownTime * 3) && (eventProgress == 4))
            {
                SetExpInside(true);
                eventProgress = 5;
            }
            // 避難階到着
            if ((workTime >= 10 + upDownTime * 3.5) && (eventProgress == 5))
            {
                monitorController.isStop = true;
                monitorController.currentFloor = 5;
                audioController.PlayFloorSound(5); // 「5階です」
                eventProgress = 6;
            }
            if ((workTime >= 10 + upDownTime * 3.75) && (eventProgress == 6))
            {
                SetExpStopFloor(true);
                eventProgress = 7;
            }
            if ((workTime >= 10 + upDownTime * 4) && (eventProgress == 7))
            {
                audioController.PlaySystemSound(2); // 「扉が開きます」
                monitorController.elevatorState = 2;

                SetExpOutside(true);
                arrows[arrowDisaEnd1].SetActive(true);
                arrows[arrowDisaEnd2].SetActive(true);
                //arrowDisasterEnd.SetActive(true);

                eventProgress = 8;
            }
            // 避難開始
            if ((workTime >= 10 + upDownTime * 4.5) && (eventProgress == 8))
            {
                // ドア開く
                doorOpen.Open();
                audioController.PlayExplainCommonSound(3,langMode); // 避難階に到着したようです

                if(langMode == 0){
                    StartCoroutine(AudioCor(audioController.jpExplainCommonSounds[4], 4));
                    StartCoroutine(AudioCor(audioController.jpExplainOutsideDisplay[4], 4));
                    StartCoroutine(AudioCor(audioController.jpExplainOutsideDisplay[eventMode], 4));
                } else if(langMode == 1){
                    StartCoroutine(AudioCor(audioController.enExplainCommonSounds[4], 4));
                    StartCoroutine(AudioCor(audioController.enExplainOutsideDisplay[4], 4));
                    StartCoroutine(AudioCor(audioController.enExplainOutsideDisplay[eventMode], 4));
                }

                eventProgress = 9;
            }
        }

        //時間の計測
        if (isWorking)
        {
            workTime += Time.deltaTime;
        }
    }

    public void selectFire()
    {
        Debug.Log("Fire Pushed");
        selectDisaster(fire, 5);
    }

    public void selectWater()
    {
        Debug.Log("rain Pushed");
        selectDisaster(rain, 1);
    }

    public void selectEarth()
    {
        Debug.Log("Earth Pushed");
        selectDisaster(earth, 5);
    }

    public void selectElect()
    {
        Debug.Log("Elect Pushed");
        selectDisaster(elect, 5);
    }

    // 災害ボタン押下時の処理
    public void selectDisaster(int disNum, int floor)
    {
        Debug.Log("disaster selected");
        audioController.StopExplainAudio(); // 音声停止

        arrows[arrowSt].SetActive(false); // 開始矢印を非表示化
        //arrowStart.SetActive(false); // 開始矢印を非表示化

        //selectUI.SetActive(false);
        uiController.SetSelectUI(false);
        monitorController.elevatorState = 0;

        eventMode = disNum;
        monitorController.currentFloor = floor;

        // 説明文の表示
        switch (disNum)
        {
            case 0:
                Debug.Log("BeforeExprcExpText_fire displayed");
                uiController.SetBeforeExprcExpUI("fire", true);
                break;
            case 1:
                Debug.Log("BeforeExprcExpText_rain displayed");
                uiController.SetBeforeExprcExpUI("rain", true);
                break;
            case 2:
                Debug.Log("BeforeExprcExpText_earth displayed");
                uiController.SetBeforeExprcExpUI("earth", true);
                break;
            case 3:
                Debug.Log("BeforeExprcExpText_elect displayed");
                uiController.SetBeforeExprcExpUI("elect", true);
                break;
            default:
                Debug.Log("BeforeExprcExpText failed");
                uiController.SetBeforeExprcExpUI("false", false);
                break;
        }
        textProgress = 1;
        Debug.Log("text progressed");

        // 体験開始ボタンの表示
        // startButton.SetActive(true);
        uiController.SetStartUI(true);

        audioController.PlayExplainWorkingSound(disNum, langMode); //火災管制運転の体験です
        
        //火災管制運転の説明，上記の音声が再生し終わってから説明文再生
        // if (langMode == 0)
        // {
        //     StartCoroutine(AudioCor(audioController.jpExplainAboutSounds[disNum], 0));
        // }
        // else if (langMode == 1)
        // {
        //     StartCoroutine(AudioCor(audioController.enExplainAboutSounds[disNum], 0));
        // }

        if (langMode == 0)
        {
            StartCoroutine(AudioCor(audioController.jpBeforeExprcExpSounds1[disNum], 0));
        }
        else if (langMode == 1)
        {
            StartCoroutine(AudioCor(audioController.enBeforeExprcExpSounds1[disNum], 0));
        }
        Debug.Log("ExplainWorkingSound played");
    }

    public void ProcBeforeExprcExp()
    {
        audioController.StopAllAudio();
        audioController.PlayBeforeExprcExpSound2(eventMode, langMode);
        if (textProgress == 1)
        {
            switch (eventMode)
            {
                case 0:
                    uiController.ProcBeforeExprcExpUI("fire", textProgress);
                    break;
                case 1:
                    uiController.ProcBeforeExprcExpUI("rain", textProgress);
                    break;
                case 2:
                    uiController.ProcBeforeExprcExpUI("earth", textProgress);
                    break;
                case 3:
                    uiController.ProcBeforeExprcExpUI("elect", textProgress);
                    break;
                default:
                    break;
            }

            textProgress++;
        }
    }

    public void BackBeforeExprcExp()
    {
        if (textProgress == 2)
        {
            switch (eventMode)
            {
                case 0:
                    uiController.BackBeforeExprcExpUI("fire", textProgress);
                    break;
                case 1:
                    uiController.BackBeforeExprcExpUI("rain", textProgress);
                    break;
                case 2:
                    uiController.BackBeforeExprcExpUI("earth", textProgress);
                    break;
                case 3:
                    uiController.BackBeforeExprcExpUI("elect", textProgress);
                    break;
                default:
                    break;
            }
        
            textProgress -= 1;
        }
    }


    public void exprainEnd()
    {
        //ドアを開けてユーザがエレベータ内に乗車可能に
        doorOpen.Open();
        audioController.StopExplainAudio(); // 音声停止
        isExplainEnd = true; //説明文終了フラグ

        audioController.PlayExplainCommonSound(1, langMode); // それではエレベータに乗ってください
        // 「体験開始ボタンを押すと体験が開始します」，上記の音声が再生し終わってから説明文再生
        if (langMode == 0)
        {
            StartCoroutine(AudioCor(audioController.jpExplainCommonSounds[2], 1));
        }
        else if (langMode == 1)
        {
            StartCoroutine(AudioCor(audioController.enExplainCommonSounds[2], 1));
        }

        arrows[arrowExEnd].SetActive(true);
        //arrowExplainEnd.SetActive(true);

        uiController.SetBeforeExprcExpUI("false", false);
    }

    // 体験開始ボタン押下時処理
    public void exeDisaster()
    {
        audioController.StopExplainAudio(); // 音声停止
        isRode = true; // エレベータ乗車フラグ

        doorClose.Close();
        isWorking = true;
        // startButton.SetActive(false);
        uiController.SetStartUI(false);
        arrows[arrowExEnd].SetActive(false);
        //arrowExplainEnd.SetActive(false);

        audioController.PlayExplainStartExpSound(eventMode,langMode); // 「～体験を開始します」
    }

    // 名前変えた
    public void SetExpInside(bool flag){
        uiController.SetExpInsideUI(eventMode,flag);
        arrows[arrowInDisp].SetActive(flag);

        if(flag){
            pause.PauseGame();
            flashing.flashStart(flashingInside);
            audioController.PlayExplainInsideSound(4,langMode);
            if(langMode == 0){
                StartCoroutine(AudioCor(audioController.jpExplainInsideDisplay[eventMode], 2));
            } else if(langMode == 1){
                StartCoroutine(AudioCor(audioController.enExplainInsideDisplay[eventMode], 2));
            }
        } else {
            isInsideEnd = true;
            isOutsideEnd = false; // 「他の災害を体験する」選択時に初期化すると「エレベーター外の表示」の音声が変なタイミングで再生されるようになったのでこちらで初期化
            audioController.StopExplainAudio();
            pause.ResumeGame();
            flashing.flashEnd();
        }
    }

    // 名前変えた
    public void SetExpStopFloor(bool flag){
        uiController.SetExpStopFloorUI(eventMode,flag);
        arrows[arrowInDisp].SetActive(flag);

        if(flag){
            pause.PauseGame();
            flashing.flashStart(flashingInside);

            // 変更しまあああああああああす！！！
            audioController.PlayExplainStopFloorSound(4,langMode);
            if(langMode == 0){
                StartCoroutine(AudioCor(audioController.jpExplainStopFloor[eventMode], 3));
            } else if(langMode == 1){
                StartCoroutine(AudioCor(audioController.enExplainStopFloor[eventMode], 3));
            }
        } else {
            isStopFloorEnd = true;
            audioController.StopExplainAudio();
            pause.ResumeGame();
            flashing.flashEnd();
        }
    }

    public void SetExpOutside(bool flag){
        uiController.SetExpOutsideUI(eventMode,flag);
        arrows[arrowOutDisp].SetActive(flag);

        if(flag){
            flashing.flashStart(flashingOutside);
        } else {
            audioController.StopExplainAudio();

            audioController.PlayExplainCommonSound(5,langMode); //これで体験は終了です

            arrows[arrowDisaEnd1].SetActive(false);
            arrows[arrowDisaEnd2].SetActive(false);

            flashing.flashEnd();
            isOutsideEnd = true;
        }
    }

    public void SetEnd(bool flag)
    {
        uiController.SetEndUI(flag);
    }

    // コルーチン無理やり使うためのやつ、あとで治す
    IEnumerator AudioCor(AudioClip sounds,int flagNum)
    {
        Debug.Log("AudioCor START");

        //yield return new WaitForSeconds(num);
        yield return new WaitWhile(() => audioController.explainSoundSpeaker.isPlaying);
        //audioController.explainSoundSpeaker.PlayOneShot(sounds);

        if ((flagNum == 0) && (isExplainEnd == false)){
            audioController.explainSoundSpeaker.PlayOneShot(sounds);
        }
        else if ((flagNum == 1) && (isRode == false)){
            audioController.explainSoundSpeaker.PlayOneShot(sounds);
        }
        else if ((flagNum == 2) && (isInsideEnd == false)){
            audioController.explainSoundSpeaker.PlayOneShot(sounds);
        }
        else if ((flagNum == 3) && (isStopFloorEnd == false)){
            audioController.explainSoundSpeaker.PlayOneShot(sounds);
        }
        else if ((flagNum == 4) && (isOutsideEnd == false)){
            audioController.explainSoundSpeaker.PlayOneShot(sounds);
        }

        Debug.Log("AudioCor END");

    }

    // 体験終了ボタンが押された際のイベント
    public void endScene()
    {
        // 初期化
        evLight.color = Color.white;
        eventMode = -5;
        isWorking = false;
        isExplainEnd = false;
        isRode = false;
        isInsideEnd = false;
        isStopFloorEnd = false;
        // isOutsideEnd = false; 爆速で体験を終わらせるとエレベーター外表示の音声が他の音声と被るので，このフラグだけ別のとこで初期化します
        workTime = 0;
        //waitTime = 0.0f;
        eventProgress = 0;
        monitorController.elevatorState = 3;
        pointMove.isEarth = false;

        arrows[arrowSt].SetActive(true);
        //arrowStart.SetActive(true);
        uiController.SetSelectUI(true);

        // 終了UIの非表示
        uiController.SetEndUI(false);

        doorClose.Close();

        //音声の停止
        audioController.StopAllAudio();

        //エレベータ災害体験システムです
        audioController.PlayExplainCommonSound(0,langMode);
    }

    // 言語モードを変更する
    public void ChangeLangMode(int mode){
        langMode = mode;
        uiController.SetLanguage(mode);
    }
}