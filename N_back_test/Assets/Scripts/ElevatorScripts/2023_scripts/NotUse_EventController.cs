using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class EventController : MonoBehaviour
{
    MonitorController monitorController;
    AudioController audioController;
    DoorOpen doorOpen;
    DoorClose doorClose;
    GameObject selectUI;
    Image expImage;
    TextMeshProUGUI[] titleText = new TextMeshProUGUI[4];
    TextMeshProUGUI[] expText = new TextMeshProUGUI[4];
    Light evLight;

    int eventMode=-5;

    float li=0;

    bool isWorking=false;
    float workTime=0;

    int eventProgress=0;

    // Start is called before the first frame update
    void Start()
    {
        monitorController=GetComponent<MonitorController>();
        audioController=GetComponent<AudioController>();
        doorOpen=GetComponent<DoorOpen>();
        doorClose=GetComponent<DoorClose>();
        evLight=GameObject.Find("PointLight").GetComponent<Light>();
        
        selectUI = GameObject.Find("SelectUI");
        expImage = GameObject.Find("ExpPanel").GetComponent<Image>();

        titleText[0] = GameObject.Find("TitleFire").GetComponent<TextMeshProUGUI>();
        titleText[1] = GameObject.Find("TitleRain").GetComponent<TextMeshProUGUI>();
        titleText[2] = GameObject.Find("TitleEarth").GetComponent<TextMeshProUGUI>();
        titleText[3] = GameObject.Find("TitleElectrocity").GetComponent<TextMeshProUGUI>();

        expText[0] = GameObject.Find("ExpFire").GetComponent<TextMeshProUGUI>();
        expText[1] = GameObject.Find("ExpRain").GetComponent<TextMeshProUGUI>();
        expText[2] = GameObject.Find("ExpEarth").GetComponent<TextMeshProUGUI>();
        expText[3] = GameObject.Find("ExpElectrocity").GetComponent<TextMeshProUGUI>();
    }

    // Update is called once per frame
    void Update()
    {
        if(eventMode==0){
            //Debug.Log("火災");//1Fに向かう
            if((workTime<=2) && (eventProgress==0)){
                monitorController.elevatorState=3;
                li=0;
                evLight.range=li;
            }
            if((workTime<4) && (workTime>2) && (eventProgress==0)){
                li+=(10f/(4*150));
                evLight.range=li;
            }
            if((workTime>=4) && (eventProgress==0)){
                monitorController.mode=eventMode;
                monitorController.elevatorState=1;
                monitorController.currentFloor=4;
                audioController.PlayDisasterSound();
                eventProgress=1;
            }
            if((workTime>=4+3*1) && (eventProgress==1)){
                monitorController.currentFloor=3;
                eventProgress=2;
            }
            if((workTime>=4+3*2) && (eventProgress==2)){
                monitorController.currentFloor=2;
                eventProgress=3;
            }
            if((workTime>=4+3*3) && (eventProgress==3)){
                monitorController.currentFloor=1;
                audioController.PlayFloorSound(1);
                eventProgress=4;
            }
            if((workTime>=4+3*3.5) && (eventProgress==4)){
                audioController.PlaySystemSound(2);
                monitorController.elevatorState=2;
                eventProgress=5;
            }
            if((workTime>=4+3*4) && (eventProgress==5)){
                doorOpen.Open();
                eventProgress=6;
            }
            if((workTime>=4+3*8) && (eventProgress==6)){
                doorClose.Close();
                li-=(10f/(4*150));
                monitorController.currentFloor=4;
                monitorController.elevatorState=3;
                evLight.range=li;
            }
            if((workTime>=4+3*10) && (eventProgress==6)){
                eventMode=-1;
                selectUI.SetActive(true);
                isWorking=false;
                li=10;
                evLight.range=li;
                workTime=0;
                eventProgress=0;
                SceneManager.LoadScene("SampleScene");
            }
            

        }else if(eventMode==1){
            //Debug.Log("冠水");//1Fを避けて止まる
            if((workTime<=2) && (eventProgress==0)){
                monitorController.elevatorState=3;
                li=0;
                evLight.range=li;
            }
            if((workTime<4) && (workTime>2) && (eventProgress==0)){
                li+=(10f/(4*150));
                evLight.range=li;
            }
            if((workTime>=4) && (eventProgress==0)){
                monitorController.mode=eventMode;
                monitorController.elevatorState=1;
                monitorController.currentFloor=4;
                
                audioController.PlayDisasterSound();
                eventProgress=1;
            }
            if((workTime>=4+3*1) && (eventProgress==1)){
                monitorController.currentFloor=3;
                eventProgress=2;
            }
            if((workTime>=4+3*2) && (eventProgress==2)){
                monitorController.currentFloor=2;
                audioController.PlayFloorSound(2);
                eventProgress=4;
            }
            if((workTime>=4+3*3.5) && (eventProgress==4)){
                audioController.PlaySystemSound(2);
                monitorController.elevatorState=2;
                eventProgress=5;
            }
            if((workTime>=4+3*4) && (eventProgress==5)){
                doorOpen.Open();
                eventProgress=6;
            }
            if((workTime>=4+3*8) && (eventProgress==6)){
                doorClose.Close();
                li-=(10f/(4*150));
                monitorController.currentFloor=4;
                monitorController.elevatorState=3;
                evLight.range=li;
            }
            if((workTime>=4+3*10) && (eventProgress==6)){
                eventMode=-1;
                selectUI.SetActive(true);
                isWorking=false;
                li=10;
                evLight.range=li;
                workTime=0;
                eventProgress=0;
                SceneManager.LoadScene("SampleScene");
            }
            

        }else if(eventMode==2){
            //Debug.Log("地震");
            if((workTime<=2) && (eventProgress==0)){
                monitorController.elevatorState=3;
                li=0;
                evLight.range=li;
            }
            if((workTime<4) && (workTime>2) && (eventProgress==0)){
                li+=(10f/(4*150));
                evLight.range=li;
            }
            if((workTime>=4) && (eventProgress==0)){
                monitorController.mode=4;
                monitorController.elevatorState=0;
                monitorController.currentFloor=4;
                eventProgress=1;
            }
            if((workTime>=4+3*1) && (eventProgress==1)){
                monitorController.mode=eventMode;
                monitorController.elevatorState=1;
                
                monitorController.currentFloor=3;
                
                audioController.PlayDisasterSound();
                
                eventProgress=2;
            }
            if((workTime>=4+3*3) && (eventProgress==2)){
                monitorController.currentFloor=2;
                audioController.PlayFloorSound(2);
                eventProgress=4;
            }
            if((workTime>=4+3*3.5) && (eventProgress==4)){
                audioController.PlaySystemSound(2);
                monitorController.elevatorState=2;
                eventProgress=5;
            }
            if((workTime>=4+3*4) && (eventProgress==5)){
                doorOpen.Open();
                eventProgress=6;
            }
            if((workTime>=4+3*8) && (eventProgress==6)){
                doorClose.Close();
                li-=(10f/(4*150));
                monitorController.currentFloor=4;
                monitorController.elevatorState=3;
                evLight.range=li;
            }
            if((workTime>=4+3*10) && (eventProgress==6)){
                eventMode=-1;
                selectUI.SetActive(true);
                isWorking=false;
                li=10;
                evLight.range=li;
                workTime=0;
                eventProgress=0;
                SceneManager.LoadScene("SampleScene");
            }

        }else if(eventMode==3){
            //Debug.Log("停電");
            if((workTime<=2) && (eventProgress==0)){
                monitorController.elevatorState=3;
                li=0;
                evLight.range=li;
            }
            if((workTime<4) && (workTime>2) && (eventProgress==0)){
                li+=(10f/(4*150));
                evLight.range=li;
            }
            if((workTime>=4) && (eventProgress==0)){
                monitorController.mode=4;
                monitorController.elevatorState=0;
                monitorController.currentFloor=4;
                eventProgress=1;
            }
            if((workTime>=4+3*1) && (eventProgress==1)){
                monitorController.mode=eventMode;
                monitorController.elevatorState=1;
                monitorController.currentFloor=3;
                
                audioController.PlayDisasterSound();
                
                
                eventProgress=2;
            }
            if((workTime>=4+3*3) && (eventProgress==2)){
                monitorController.currentFloor=2;
                audioController.PlayFloorSound(2);
                eventProgress=4;
            }
            if((workTime>=4+3*3.5) && (eventProgress==4)){
                audioController.PlaySystemSound(2);
                monitorController.elevatorState=2;
                eventProgress=5;
            }
            if((workTime>=4+3*4) && (eventProgress==5)){
                doorOpen.Open();
                eventProgress=6;
            }
            if((workTime>=4+3*7) && (eventProgress==6)){
                doorClose.Close();
                li-=(10f/(4*150));
                monitorController.currentFloor=4;
                monitorController.elevatorState=3;
                evLight.range=li;
            }
            if((workTime>=4+3*9) && (eventProgress==6)){
                eventMode=-1;
                selectUI.SetActive(true);
                isWorking=false;
                li=10;
                evLight.range=li;
                workTime=0;
                eventProgress=0;
                SceneManager.LoadScene("SampleScene");
            }
        }
        
        if(isWorking){
            workTime+=Time.deltaTime;
        }
    }

    public void selectFire(){
        eventMode=0;
        selectUI.SetActive(false);
        expImage.enabled = true;
        titleText[0].enabled = true;
        expText[0].enabled = true;        
        isWorking=true;
    }
    public void selectWater(){
        eventMode=1;
        selectUI.SetActive(false);
        expImage.enabled = true;
        titleText[1].enabled = true;
        expText[1].enabled = true;    
        isWorking=true;
    }
    public void selectEarth(){
        eventMode=2;
        selectUI.SetActive(false);
        expImage.enabled = true;
        titleText[2].enabled = true;
        expText[2].enabled = true;    
        isWorking=true;
    }
    public void selectElect(){
        eventMode=3;
        selectUI.SetActive(false);
        expImage.enabled = true;
        titleText[3].enabled = true;
        expText[3].enabled = true;    
        isWorking=true;
    }

}
