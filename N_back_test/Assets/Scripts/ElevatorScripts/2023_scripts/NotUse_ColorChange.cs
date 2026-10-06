using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//ボタンの点灯・消灯処理のプログラム
public class ColorChange : MonoBehaviour
{
    //ボタンの背景のオブジェクト
    public GameObject QuadOn;
    public GameObject QuadOff;

    //ボタン背景のレンダラー
    private Renderer RendererOn;
    private Renderer RendererOff;
    
    

    // Start is called before the first frame update
    void Start()
    {
        //ボタン背景のレンダラーの取得
        RendererOn = QuadOn.GetComponent<Renderer>();
        RendererOff = QuadOff.GetComponent<Renderer>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void hogehoge(){
        if(RendererOn.enabled == false){
            SetOn();
        } else {
            SetOff();
        }

    }

    //ボタンを点灯状態に
    public void SetOn(){
        RendererOn.enabled = true;
        RendererOff.enabled = false;
    }

    //ボタンを消灯状態に
    public void SetOff(){
        RendererOn.enabled = false;
        RendererOff.enabled = true;
    }
}
