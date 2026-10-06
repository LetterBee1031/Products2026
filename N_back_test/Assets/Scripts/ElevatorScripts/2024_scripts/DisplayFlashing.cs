using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 強調したいものの周りでオブジェクトを点滅させるためのスクリプト
// エレベーター内のディスプレイの強調などに使用予定
public class DisplayFlashing : MonoBehaviour
{
    // 点滅させる対象
    private Renderer target;
    private GameObject flashTest;

    //private GameObject[] flashingObject;
    // 点滅周期[s]
    [SerializeField] private float flashcycle = 1;

    private double time;

    private bool isFlashing = false;

    void Start()
    {
        /*
        var tag = "flashingObject";
        flashingObject = GameObject.FindGameObjectsWithTag(tag);
        */
    }

    private void Update()
    {
        // 内部時間の計測
        time += Time.unscaledDeltaTime;

        // 周期cycleで繰り返す値の取得
        // 0～cycleの範囲の値が得られる
        float repeatValue = Mathf.Repeat((float)time, flashcycle);

        if (isFlashing)
        {
            if (repeatValue >= (flashcycle * 0.5f))
            {
                target.enabled = true;
            }
            else
            {
                target.enabled = false;
            }
        }



        /*
        // 点滅するオブジェクトのレンダラーを取得し，時間で点滅処理
        for (int i = 0; i < flashingObject.Length; i++)
        {
            target = flashingObject[i].GetComponent<Renderer>();

            if (isFlashing)
            {
                if (repeatValue >= (flashcycle * 0.5f))
                {
                    target.enabled = true;
                }
                else
                {
                    target.enabled = false;
                }
            }
        }
        */

        /*
        if (OVRInput.GetDown(OVRInput.RawButton.A))
        {
            Debug.Log("Aボタンを押した");
            //flashStart();
        }
        if (OVRInput.GetDown(OVRInput.RawButton.B))
        {
            Debug.Log("Bボタンを押した");
            flashEnd();
        }
        */
    }

    public void flashStart(Renderer flashObject)
    {
        isFlashing = true;
        target = flashObject;
        Debug.Log("flashObjectReceived");
    }

    public void flashEnd()
    {
        isFlashing = false;
        target.enabled = false;
        target = null;
        /*
        for (int i = 0; i < flashingObject.Length; i++)
        {
            target = flashingObject[i].GetComponent<Renderer>();
            target.enabled = false;
        }
        */
    }
}
