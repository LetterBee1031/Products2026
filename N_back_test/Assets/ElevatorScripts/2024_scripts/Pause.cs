using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ポーズ機能のスクリプト
// Time.timeScaleをいじる単純なポーズ機能の実装を試みた
// 実装自体はできたが，timeScaleいじってポーズしたらポーズとハイライト機能が両立できないことに
// 気が付いてしまった．現時点(2024/10/4)ではハイライト表示の点滅をなくしている
public class Pause : MonoBehaviour
{
    AudioController audioController;
    // Start is called before the first frame update
    void Start()
    {
        audioController = GetComponent<AudioController>();
    }

    // Update is called once per frame
    void Update()
    {
        /*
        if(OVRInput.GetDown(OVRInput.RawButton.X))
        {
            Debug.Log("Xボタンを押した");
            PauseGame();
        }
        if(OVRInput.GetDown(OVRInput.RawButton.Y)){
            Debug.Log("Yボタンを押した");
            ResumeGame();
        }
        */
    }

    public void PauseGame(){
        Time.timeScale = 0;
        audioController.PauseAllAudio();
    }

    public void ResumeGame(){
        Time.timeScale = 1;
        audioController.UnPauseAllAudio();
    }
}
