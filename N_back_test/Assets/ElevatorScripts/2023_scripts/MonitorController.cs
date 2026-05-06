using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// UI処理のクラスを使用する宣言
using UnityEngine.UI;

// エレベーター内外のディスプレイ表示管理のスクリプト
public class MonitorController : MonoBehaviour
{
    public int currentFloor=5;//回数
    public int mode=4;//火災:0, 冠水:1, 地震:2, 停電:3, 消灯:4
    public int elevatorState=0;//0:通常, 1:避難階停止動作中, 2:停止後案内, 3:完全停止, 4:最寄り階停止動作中
    public bool isUp=false; // エレベーターが上昇するか
    public bool isStop=true; // エレベーターが階に停止しているか

    Image[] evfloors=new Image[5]; // エレベーター内部ディスプレイの階数画像の配列
    Image[] disasterType=new Image[4]; // エレベーター内部ディスプレイの災害時運転画像の配列
    Image[] floors=new Image[5]; // エレベーター外部ディスプレイの階数画像の配列
    Image[] outDisasterType=new Image[4]; // エレベーター外部ディスプレイの災害時動作画像の配列

    // エレベーター内部ディスプレイの上昇・下降等，その他表示画像
    Image arrive;
    Image stopFor;
    Image stopFor2;
    Image info;

    Image floorUp;
    Image floorDown;

    Light evLight; // エレベーター内部の照明のオブジェクト

    int evFloor;
    int beforeState;

    // Start is called before the first frame update
    void Start()
    {
        // エレベーター内部ディスプレイの階数画像の取得
        evfloors[0] = GameObject.Find("EVFloor1").GetComponent<Image>();
        evfloors[1] = GameObject.Find("EVFloor2").GetComponent<Image>();
        evfloors[2] = GameObject.Find("EVFloor3").GetComponent<Image>();
        evfloors[3] = GameObject.Find("EVFloor4").GetComponent<Image>();
        evfloors[4] = GameObject.Find("EVFloor5").GetComponent<Image>();

        // エレベーター内部ディスプレイの災害時運転画像の取得
        disasterType[0] = GameObject.Find("Fire").GetComponent<Image>();
        disasterType[3] = GameObject.Find("PowerOutage").GetComponent<Image>();
        disasterType[2] = GameObject.Find("EarthQuake").GetComponent<Image>();
        disasterType[1] = GameObject.Find("Water").GetComponent<Image>();

        // エレベーター外部ディスプレイの階数画像の取得
        floors[0] = GameObject.Find("Floor1").GetComponent<Image>();
        floors[1] = GameObject.Find("Floor2").GetComponent<Image>();
        floors[2] = GameObject.Find("Floor3").GetComponent<Image>();
        floors[3] = GameObject.Find("Floor4").GetComponent<Image>();
        floors[4] = GameObject.Find("Floor5").GetComponent<Image>();

        //エレベーター外部ディスプレイの災害時運転画像の取得
        outDisasterType[0] = GameObject.Find("OutFire").GetComponent<Image>();
        outDisasterType[3] = GameObject.Find("OutPowerOutage").GetComponent<Image>();
        outDisasterType[2] = GameObject.Find("OutEarthQuake").GetComponent<Image>();
        outDisasterType[1] = GameObject.Find("OutWater").GetComponent<Image>();

        // エレベーター内部ディスプレイの上昇・下降等，その他表示画像の取得
        arrive = GameObject.Find("Arrive").GetComponent<Image>();
        stopFor = GameObject.Find("StopFor").GetComponent<Image>();
        stopFor2 = GameObject.Find("StopFor2").GetComponent<Image>();
        info = GameObject.Find("Info").GetComponent<Image>();
        floorUp = GameObject.Find("FloorUp").GetComponent<Image>();
        floorDown = GameObject.Find("FloorDown").GetComponent<Image>();

        // エレベーター内部の照明オブジェクトの取得
        evLight = GameObject.Find("PointLight").GetComponent<Light>();
    }

    // Update is called once per frame
    private void FixedUpdate() {
        // エレベーター階数表示の変更
        for(int i=0;i<5;i++){
            if(i==currentFloor-1){
                //Debug.Log(i);
                floors[i].enabled=true;
                evfloors[i].enabled=true;
            }else{
                floors[i].enabled=false;
                evfloors[i].enabled=false;
            }
        }

        // エレベーター災害時表示の変更
        for(int i=0;i<4;i++){
            if(i==mode){
                //Debug.Log(i);
                disasterType[i].enabled=true;
                outDisasterType[i].enabled=true;
            }else{
                disasterType[i].enabled=false;
                outDisasterType[i].enabled=false;
            }
        }
        //0:通常, 1:避難階停止動作中, 2:停止後案内, 3:完全停止, 4:最寄り階停止動作中
        //elevatorState:0 エレベーター通常動作中
        if(elevatorState==0){
            arrive.enabled=false;
            stopFor.enabled=false;
            stopFor2.enabled=false;
        // elevatorState:1 エレベーター避難階へ移動中　「避難階へ止まります」表示
        }else if(elevatorState==1){
            arrive.enabled=false;
            stopFor.enabled=true;
            stopFor2.enabled=false;
        // elevatorState:2 エレベーター避難階到着後案内　「到着しました，エレベーターから降りてください」表示
        }else if(elevatorState==2){
            arrive.enabled=true;
            stopFor.enabled=false;
            stopFor2.enabled=false;
        // elevatorState:3 エレベーター完全停止
        }else if(elevatorState==3){
            arrive.enabled=false;
            stopFor.enabled=false;
            stopFor2.enabled=false;
        // elevatorState:4 エレベーター最寄り階へ移動中　「最寄りの階へ止まります」表示
        }else if(elevatorState==4){
            arrive.enabled=false;
            stopFor.enabled=false;
            stopFor2.enabled=true;
        }
        // エレベーター完全消灯
        if(elevatorState==3){
            info.enabled=true;
            floorUp.enabled=false;
            floorDown.enabled=false;
            currentFloor=-1;
            mode=4;
            evLight.enabled=false;
        // エレベーター動作中
        }else{
            info.enabled=true;
            // エレベーター特定階停止時
            if(isStop == true){
                floorUp.enabled=false;
                floorDown.enabled=false;
            // エレベーター上昇時
            }else if(isUp == true){
                floorUp.enabled=true;
                floorDown.enabled=false;
            // エレベーター下降時
            } else {
                floorUp.enabled=false;
                floorDown.enabled=true;
            }
            evLight.enabled=true; // エレベーター内照明点灯

        }
        beforeState=elevatorState;
    }
}