using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Threading;
//using Unity.VisualScripting;


// システム全体のコントローラースクリプト 未使用
// 念のため残している2024_08_16時点でのMainController.csのコード
// 困ったらここを確認
public class NotUse_MainController_2024_08_16 : MonoBehaviour
{
    /*
    // 各スクリプトのインスタンス化
    MonitorController monitorController;
    AudioController audioController;
    DoorOpen doorOpen;
    DoorClose doorClose;
    GameObject selectUI;
    Image expImage;
    PointMove pointMove;

    //制作・協力のテキストオブジェクト
    TextMeshProUGUI productionSelectText;
    TextMeshProUGUI productionExplainText;
    TextMeshProUGUI productionEndText;

    //災害説明テキストオブジェクト
    TextMeshProUGUI[] titleText = new TextMeshProUGUI[4];
    TextMeshProUGUI[] expText = new TextMeshProUGUI[4];

    //体験開始ボタンのオブジェクト
    //GameObject[] startButton = new GameObject[4];
    GameObject explainEndButton;
    GameObject startButton;

    //体験終了テキストのオブジェクト
    Image endImage;
    TextMeshProUGUI endText;
    GameObject endButton;

    //矢印
    GameObject arrowExplainEnd;
    GameObject arrowDisasterEnd;

    Light evLight; // エレベーター内部の照明
    Light dlLight; // 全体の照明

    int eventMode = -5; // 火災:0 冠水:1 地震：2 停電：3

    float li = 0; // 照明の明るさの数値

    bool isWorking = false; // 体験中か
    bool isExplainEnd = false; // 災害時説明が終了したか
    bool isRode = false; // エレベーターへの乗り込みが終了し，体験が始まったか
    float workTime = 0; // 各動作のタイミング管理用タイマー

    int eventProgress = 0; // 体験の進行段階の管理

    int upDownTime = 5; // 階数移動にかかる時間
    

    // Start is called before the first frame update
    void Start()
    {
        // 各スクリプトのインスタンス取得
        monitorController = GetComponent<MonitorController>();
        audioController = GetComponent<AudioController>();
        doorOpen = GetComponent<DoorOpen>();
        doorClose = GetComponent<DoorClose>();
        pointMove = GameObject.Find("Master").GetComponent<PointMove>();

        // 照明の取得
        evLight = GameObject.Find("PointLight").GetComponent<Light>();
        dlLight = GameObject.Find("Directional Light").GetComponent<Light>();

        selectUI = GameObject.Find("SelectUI"); // 災害選択UIの取得

        // 制作・協力のテキストの取得
        productionSelectText = GameObject.Find("productionSelect").GetComponent<TextMeshProUGUI>();
        productionExplainText = GameObject.Find("productionExplain").GetComponent<TextMeshProUGUI>();
        productionEndText = GameObject.Find("productionEnd").GetComponent<TextMeshProUGUI>();

        // 災害時動作説明UIに関する各オブジェクトの取得
        expImage = GameObject.Find("ExpPanel").GetComponent<Image>();
        titleText[0] = GameObject.Find("TitleFire").GetComponent<TextMeshProUGUI>();
        titleText[1] = GameObject.Find("TitleRain").GetComponent<TextMeshProUGUI>();
        titleText[2] = GameObject.Find("TitleEarth").GetComponent<TextMeshProUGUI>();
        titleText[3] = GameObject.Find("TitleElectrocity").GetComponent<TextMeshProUGUI>();
        expText[0] = GameObject.Find("ExpFire").GetComponent<TextMeshProUGUI>();
        expText[1] = GameObject.Find("ExpRain").GetComponent<TextMeshProUGUI>();
        expText[2] = GameObject.Find("ExpEarth").GetComponent<TextMeshProUGUI>();
        expText[3] = GameObject.Find("ExpElectrocity").GetComponent<TextMeshProUGUI>();

        explainEndButton = GameObject.Find("ButtonExplainEnd"); // 「体験へ」ボタンの取得
        startButton = GameObject.Find("ButtonStart"); // 「体験開始」ボタンの取得

        // 体験終了UIに関する各オブジェクトの取得
        endImage = GameObject.Find("EndPanel").GetComponent<Image>();
        endText = GameObject.Find("EndText").GetComponent<TextMeshProUGUI>();
        endButton = GameObject.Find("ButtonEnd");

        // 進行方向指示用矢印オブジェクトの取得
        arrowExplainEnd = GameObject.Find("ArrowExplainEnd");
        arrowDisasterEnd = GameObject.Find("ArrowDisasterEnd");

        monitorController.elevatorState = 3;

        // 各オブジェクトの非表示化
        endButton.SetActive(false);
        explainEndButton.SetActive(false);
        startButton.SetActive(false);
        arrowExplainEnd.SetActive(false);
        arrowDisasterEnd.SetActive(false);

        //エレベータ災害体験システムです
        audioController.PlayExplainCommonSound(0);

    }

    // Update is called once per frame
    void Update()
    {
        //火災体験
        if (eventMode == 0)
        {

            //Debug.Log("火災");//1Fに向かう
            if ((workTime <= 2) && (eventProgress == 0))
            {
                monitorController.isStop = false;
                monitorController.isUp = false;
            }

            if((workTime > 2) && (workTime < 4) && (eventProgress == 0)){

                eventProgress = 1;
            }

            if ((workTime >= 4) && (eventProgress == 1))
            {
                //災害体験の開始
                audioController.PlayEffectSound(0);  // 火災警報音
                audioController.PlayExplainHappenSound(0); // 火災が発生しました

                monitorController.mode = eventMode;
                monitorController.elevatorState = 1;
                monitorController.currentFloor = 5;
                audioController.PlayDisasterSound(); // 火災です、避難階へとまります
                eventProgress = 2;
            }
            // エレベーター下降
            if ((workTime >= 4 + upDownTime * 1) && (eventProgress == 2))
            {
                monitorController.currentFloor = 4;
                eventProgress = 3;
            }
            if ((workTime >= 4 + upDownTime * 2) && (eventProgress == 3))
            {
                monitorController.currentFloor = 3;
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
            if ((workTime >= 4 + upDownTime * 4.5) && (eventProgress == 6))
            {
                audioController.PlaySystemSound(2); // 「扉が開きます」
                monitorController.elevatorState = 2;
                eventProgress = 7;

                // 体験終了UIの表示
                endImage.enabled = true;
                endText.enabled = true;
                endButton.SetActive(true);
                productionEndText.enabled = true;
            }
            // 避難開始
            if ((workTime >= 4 + upDownTime * 5) && (eventProgress == 7))
            {
                // ドア開く
                doorOpen.Open();
                arrowDisasterEnd.SetActive(true);
                audioController.PlayExplainCommonSound(3); // 避難階に到着したようです
                eventProgress = 8;
            }
            if ((workTime >= 4 + upDownTime * 9) && (eventProgress == 8))
            {
                audioController.PlayExplainCommonSound(4); // 脱出が完了しました
                doorClose.Close();
                li -= (10f / (4 * 150));
                monitorController.currentFloor = 4;
                monitorController.elevatorState = 3;
                evLight.range = li;
                eventProgress = 9;
            }
            /*
            if ((workTime >= 4 + upDownTime * 10) && (eventProgress == 9))
            {
                eventMode = -1;
                isWorking = false;
                li = 10;
                evLight.range = li;
                workTime = 0;
                eventProgress = 0;
                //SceneManager.LoadScene("mainScene");
            }
            
        }
        // 冠水体験
        else if (eventMode == 1)
        {
            //Debug.Log("冠水");//1Fを避けて止まる

            if ((workTime <= 2) && (eventProgress == 0))
            {
                monitorController.isStop = false;
                monitorController.isUp = true;
            }

            if((workTime > 2) && (workTime < 4) && (eventProgress == 0)){
                //audioController.PlayExplainHappenSound(1); // 冠水が発生しました
                eventProgress = 1;
            }

            if ((workTime >= 4) && (eventProgress == 1))
            {
                // 冠水体験開始
                audioController.PlayExplainHappenSound(1); // 「冠水が発生しました」
                monitorController.mode = eventMode;
                monitorController.elevatorState = 1;
                monitorController.currentFloor = 1;

                audioController.PlayDisasterSound(); // 「冠水です．管制運転を行います」
                eventProgress = 2;
            }
            // エレベーター上昇
            if ((workTime >= 4 + upDownTime * 1) && (eventProgress == 2))
            {
                monitorController.currentFloor = 1;
                eventProgress = 3;
            }
            // 避難階到着
            if ((workTime >= 4 + upDownTime * 2) && (eventProgress == 3))
            {
                monitorController.isStop = true;
                monitorController.currentFloor = 2;
                audioController.PlayFloorSound(2); // 「2階です」
                eventProgress = 4;
            }
            if ((workTime >= 4 + upDownTime * 2.5) && (eventProgress == 4))
            {
                audioController.PlaySystemSound(2); // 「扉が開きます」
                monitorController.elevatorState = 2;
                eventProgress = 5;

                // 体験終了UIの表示
                endImage.enabled = true;
                endText.enabled = true;
                endButton.SetActive(true);
                productionEndText.enabled = true;

            }
            // 避難開始
            if ((workTime >= 4 + upDownTime * 3) && (eventProgress == 5))
            {
                // ドア開く
                doorOpen.Open();
                arrowDisasterEnd.SetActive(true);
                audioController.PlayExplainCommonSound(3); // 避難階に到着したようです
                eventProgress = 6;
            }
            if ((workTime >= 4 + upDownTime * 7) && (eventProgress == 6))
            {
                audioController.PlayExplainCommonSound(4); // 脱出が完了しました
                doorClose.Close();
                li -= (10f / (4 * 150));
                monitorController.currentFloor = 2;
                monitorController.elevatorState = 3;
                evLight.range = li;

                eventProgress = 7;
            }
            /*
            if ((workTime >= 4 + upDownTime * 10) && (eventProgress == 7))
            {
                eventMode = -1;
                //selectUI.SetActive(true);
                isWorking = false;
                li = 10;
                evLight.range = li;
                workTime = 0;
                eventProgress = 0;
                //SceneManager.LoadScene("mainScene");
            }
            

        }
        // 地震体験
        else if (eventMode == 2)
        {
            //Debug.Log("地震");

            if ((workTime <= 2) && (eventProgress == 0))
            {
                monitorController.isStop = false;
                monitorController.isUp = false;
            }

            if((workTime > 2) && (workTime < 4) && (eventProgress == 0)){
                //audioController.PlayExplainHappenSound(2); // 地震が発生しました
                eventProgress = 1;
            }

            if ((workTime >= 4) && (eventProgress == 1))
            {
                audioController.PlayExplainHappenSound(2); // 地震が発生しました
                audioController.PlayEffectSound(2); // 地震音声

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
                monitorController.elevatorState = 1;

                monitorController.currentFloor = 4;

                audioController.PlayDisasterSound(); // 「地震です．避難階に止まります」

                eventProgress = 4;
            }
            // 避難階到着
            if ((workTime >= 4 + upDownTime * 3) && (eventProgress == 4))
            {
                //地震終了
                pointMove.MoveEnd();
                audioController.audioStopEffect(); // 地震音声の停止
                monitorController.isStop = true;
                monitorController.currentFloor = 3;
                audioController.PlayFloorSound(3); // 「3階です」
                eventProgress = 5;
            }
            if ((workTime >= 4 + upDownTime * 3.5) && (eventProgress == 5))
            {
                audioController.PlaySystemSound(2); // 「扉が開きます」
                monitorController.elevatorState = 2;
                eventProgress = 6;

                // 体験終了UIの表示
                endImage.enabled = true;
                endText.enabled = true;
                endButton.SetActive(true);
                productionEndText.enabled = true;
            }
            // 避難開始
            if ((workTime >= 4 + upDownTime * 4) && (eventProgress == 6))
            {
                // ドア開く
                doorOpen.Open();
                arrowDisasterEnd.SetActive(true);
                audioController.PlayExplainCommonSound(3); // 避難階に到着したようです
                eventProgress = 7;
            }
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
            /*
            if ((workTime >= 4 + upDownTime * 10) && (eventProgress == 8))
            {
                eventMode = -1;
                //selectUI.SetActive(true);
                isWorking = false;
                li = 10;
                evLight.range = li;
                workTime = 0;
                eventProgress = 0;
                //SceneManager.LoadScene("mainScene");
            }
            
        }
        // 停電体験
        else if (eventMode == 3)
        {
            //Debug.Log("停電");

            if ((workTime <= 2) && (eventProgress == 0))
            {
                monitorController.isStop = false;
                monitorController.isUp = false;
            }

            if((workTime > 2) && (workTime < 4) && (eventProgress == 0)){
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
                audioController.PlayEffectSound(3); // エレベータ急停止音

                pointMove.ElectStop();
                //electStop.StopMove(); // エレベータ急停止の動き

                audioController.PlayExplainHappenSound(3); // 停電が発生しました

                eventProgress = 2;
            }
            if ((workTime >= 14 + upDownTime * 1) && (eventProgress == 2))
            {
                //audioController.PlayExplainHappenSound(3); // 停電が発生しました

                // 停電灯点灯
                evLight.color = new Color(255f/255f ,229f/255f ,153f/255f);
                li = 1;
                evLight.range = li;
                dlLight.enabled = true;

                // エレベーターディスプレイ復旧
                monitorController.mode = eventMode;
                monitorController.elevatorState = 1;
                monitorController.currentFloor = 4;
                monitorController.isUp = true;

                audioController.PlayDisasterSound(); // 「停電です．救出運転中です」


                eventProgress = 3;
            }
            // 避難階到着
            if ((workTime >= 10 + upDownTime * 3) && (eventProgress == 3))
            {
                monitorController.isStop = true;
                monitorController.currentFloor = 5;
                audioController.PlayFloorSound(5); // 「5階です」
                eventProgress = 4;
            }
            if ((workTime >= 10 + upDownTime * 3.5) && (eventProgress == 4))
            {
                audioController.PlaySystemSound(2); // 「扉が開きます」
                monitorController.elevatorState = 2;
                eventProgress = 5;

                // 体験終了UIの表示
                endImage.enabled = true;
                endText.enabled = true;
                endButton.SetActive(true);
                productionEndText.enabled = true;
            }
            // 避難開始
            if ((workTime >= 10 + upDownTime * 4) && (eventProgress == 5))
            {
                // ドア開く
                doorOpen.Open();
                arrowDisasterEnd.SetActive(true);
                audioController.PlayExplainCommonSound(3); // 避難階に到着したようです
                eventProgress = 6;
            }
            if ((workTime >= 10 + upDownTime  * 7) && (eventProgress == 6))
            {
                audioController.PlayExplainCommonSound(4); // 脱出が完了しました
                doorClose.Close();
                li -= (10f / (4 * 150));
                monitorController.currentFloor = 4;
                monitorController.elevatorState = 3;
                evLight.range = li;

                eventProgress = 7;
            }
            /*
            if ((workTime >= 10 + upDownTime * 9) && (eventProgress == 7))
            {
                eventMode = -1;
                //selectUI.SetActive(true);
                isWorking = false;
                li = 10;
                evLight.range = li;
                workTime = 0;
                eventProgress = 0;
                //SceneManager.LoadScene("mainScene");
            }
            
        }

        //時間の計測
        if (isWorking)
        {
            workTime += Time.deltaTime;
        }
    }

    //火災ボタンが押された場合の処理
    public void selectFire()
    {
        audioController.audioStopExplain(); // 音声停止

        selectUI.SetActive(false);
        monitorController.elevatorState = 0;

        eventMode = 0;
        monitorController.currentFloor = 5;

        // 説明文の表示
        expImage.enabled = true;
        titleText[0].enabled = true;
        expText[0].enabled = true;
        explainEndButton.SetActive(true);
        productionExplainText.enabled = true;

        Debug.Log("selectFire");

        // 体験開始ボタンの表示
        startButton.SetActive(true);

        audioController.PlayExplainWorkingSound(0); //火災管制運転の体験です
        //火災管制運転の説明，上記の音声が再生し終わってから説明文再生
        StartCoroutine(AudioCor(audioController.explainWorkingSounds[0].length, audioController.explainAboutSounds[0], 0));
    }

    //冠水ボタンが押された時の処理
    public void selectWater()
    {
        audioController.audioStopExplain(); // 音声停止

        selectUI.SetActive(false);
        monitorController.elevatorState = 0;

        eventMode = 1;
        monitorController.currentFloor = 1;
        // 説明文の表示
        expImage.enabled = true;
        titleText[1].enabled = true;
        expText[1].enabled = true;
        explainEndButton.SetActive(true);
        productionExplainText.enabled = true;

        // 体験開始ボタンの表示
        startButton.SetActive(true);

        audioController.PlayExplainWorkingSound(1); //冠水管制運転の体験です
        //冠水管制運転の説明，上記の音声が再生し終わってから説明文再生
        StartCoroutine(AudioCor(audioController.explainWorkingSounds[1].length, audioController.explainAboutSounds[1], 0));
    }

    //地震ボタンが押された時の処理
    public void selectEarth()
    {
        audioController.audioStopExplain(); // 音声停止

        selectUI.SetActive(false);
        monitorController.elevatorState = 0;

        eventMode = 2;
        monitorController.currentFloor = 5;

        // 説明文の表示
        expImage.enabled = true;
        titleText[2].enabled = true;
        expText[2].enabled = true;
        explainEndButton.SetActive(true);
        productionExplainText.enabled = true;

        // 体験開始ボタンの表示
        startButton.SetActive(true);

        audioController.PlayExplainWorkingSound(2); //地震管制運転の体験です
        //地震管制運転の説明，上記の音声が再生し終わってから説明文再生
        StartCoroutine(AudioCor(audioController.explainWorkingSounds[2].length, audioController.explainAboutSounds[2], 0));
    }

    //停電ボタンが押された時の処理
    public void selectElect()
    {
        audioController.audioStopExplain(); // 音声停止

        selectUI.SetActive(false);
        monitorController.elevatorState = 0;

        eventMode = 3;
        monitorController.currentFloor = 5;

        // 説明文の表示
        expImage.enabled = true;
        titleText[3].enabled = true;
        expText[3].enabled = true;
        explainEndButton.SetActive(true);
        productionExplainText.enabled = true;

        // 体験開始ボタンの表示
        startButton.SetActive(true);
        audioController.PlayExplainWorkingSound(3); //停電救出運転の体験です
        //停電救出運転の説明，上記の音声が再生し終わってから説明文再生
        StartCoroutine(AudioCor(audioController.explainWorkingSounds[3].length, audioController.explainAboutSounds[3], 0));
    }

    public void exprainEnd()
    {
        //ドアを開けてユーザがエレベータ内に乗車可能に
        doorOpen.Open();
        audioController.audioStopExplain(); // 音声停止
        isExplainEnd = true; //説明文終了フラグ

        audioController.PlayExplainCommonSound(1); // それではエレベータに乗ってください
        // 「体験開始ボタンを押すと体験が開始します」，上記の音声が再生し終わってから説明文再生
        StartCoroutine(AudioCor(audioController.explainCommonSounds[1].length, audioController.explainCommonSounds[2], 1));

        arrowExplainEnd.SetActive(true);
        explainEndButton.SetActive(false);
        for (int i = 0; i < 4; i++)
        {
            // 説明文の非表示
            expImage.enabled = false;
            titleText[i].enabled = false;
            expText[i].enabled = false;
        }
        productionExplainText.enabled = false;
    }

    // 体験開始ボタン押下時処理
    public void exeDisaster(){

        audioController.audioStopExplain(); // 音声停止
        isRode = true; // エレベータ乗車フラグ

        doorClose.Close();
        isWorking = true;
        startButton.SetActive(false);
        arrowExplainEnd.SetActive(false);

        audioController.PlayExplainStartExpSound(eventMode); // 「～体験を開始します」
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
        workTime = 0;
        eventProgress = 0;
        monitorController.elevatorState = 3;
        pointMove.isEarth = false;

        selectUI.SetActive(true);

        // 終了UIの非表示
        arrowDisasterEnd.SetActive(false);
        endButton.SetActive(false);
        endImage.enabled = false;
        endText.enabled = false;
        productionEndText.enabled = false;
        doorClose.Close();

        //音声の停止
        audioController.audioStopEffect();
        audioController.audioStopExplain();
        audioController.audioStopSystem();

        //エレベータ災害体験システムです
        audioController.PlayExplainCommonSound(0);
    }

    public void setEndUI(bool flag)
    {
        endImage.enabled = flag;
        endText.enabled = flag;
        endButton.SetActive(flag);
    }

    // コルーチン無理やり使うためのやつ、あとで治す
    IEnumerator AudioCor(float num, AudioClip sounds, int flagNum)
    {
        Debug.Log("TestCor START");

        yield return new WaitForSeconds(num);
        if((flagNum == 0) && (isExplainEnd == false)){
            audioController.explainSoundSpeaker.PlayOneShot(sounds);
        } else if((flagNum == 1) && (isRode == false)){
            audioController.explainSoundSpeaker.PlayOneShot(sounds);
        }
        Debug.Log("TestCor END");
    }
    */
}
