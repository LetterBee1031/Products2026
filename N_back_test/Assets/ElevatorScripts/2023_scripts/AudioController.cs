using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
// エレベーター音声操作のスクリプト
// 火災:0 冠水:1 地震：2 停電：3 で基本統一
// Cor.cs等によりコルーチンは動くことが分かったが
// audio.isPlayingを用いた音声終了判定がうまくいかないことが分かった
// そのためMainController.csのほうで，強引な手法で音声が被らないようにしている
public class AudioController : MonoBehaviour
{
    public AudioClip[] disasterSounds = new AudioClip[4]; //災害時のエレベータの音源
    public AudioClip[] floorSounds = new AudioClip[5]; //各階の停車音
    public AudioClip[] systemSounds = new AudioClip[6];//上, 下, 扉開, 扉閉, 停止, 扉離
    public AudioClip[] effectSounds = new AudioClip[4]; // 災害時の効果音

    // 日本語音声
    public AudioClip[] jpExplainCommonSounds = new AudioClip[6]; //共通音声, これはエレベータ災害体験システムです等
    public AudioClip[] jpExplainAboutSounds = new AudioClip[4]; //災害時動作の説明音声
    public AudioClip[] jpBeforeExprcExpSounds1 = new AudioClip[4]; //災害時動作の説明音声1 6月13日追加
    public AudioClip[] jpBeforeExprcExpSounds2 = new AudioClip[4]; //災害時動作の説明音声2 6月13日追加
    public AudioClip[] jpExplainInsideDisplay = new AudioClip[5]; //エレベーター内のディスプレイの説明音声
    public AudioClip[] jpExplainStopFloor = new AudioClip[5]; //災害時の停止階の説明の音声
    public AudioClip[] jpExplainOutsideDisplay = new AudioClip[5]; //エレベーター外のディスプレイの説明音声
    public AudioClip[] jpExplainStartExpSounds = new AudioClip[4]; // ～体験を開始します
    public AudioClip[] jpExplainWorkingSounds = new AudioClip[4]; // ～管制運転の体験です
    public AudioClip[] jpExplainHappenSounds = new AudioClip[4]; // 災害が発生しました


    // 英語音声
    public AudioClip[] enExplainCommonSounds = new AudioClip[6]; //共通音声, XR elevator system等
    public AudioClip[] enExplainAboutSounds = new AudioClip[4]; //災害時動作の説明音声
    public AudioClip[] enBeforeExprcExpSounds1 = new AudioClip[4]; //災害時動作の説明音声1 6月13日追加
    public AudioClip[] enBeforeExprcExpSounds2 = new AudioClip[4]; //災害時動作の説明音声2 6月13日追加
    public AudioClip[] enExplainInsideDisplay = new AudioClip[5]; //エレベーター内のディスプレイの説明音声
    public AudioClip[] enExplainStopFloor = new AudioClip[5]; //災害時の停止階の説明の音声
    public AudioClip[] enExplainOutsideDisplay = new AudioClip[5]; //エレベーター外のディスプレイの説明音声
    public AudioClip[] enExplainStartExpSounds = new AudioClip[4]; // start ??? experience
    public AudioClip[] enExplainWorkingSounds = new AudioClip[4]; // This is an ??? experience
    public AudioClip[] enExplainHappenSounds = new AudioClip[4]; // ??? has occured

    public AudioClip[] n_backSounds = new AudioClip[2]; // N-back用　正解・不正解音

    public AudioListener mainCameraListener;
    public AudioSource systemSoundSpeaker;
    public AudioSource effectSoundSpeaker;
    public AudioSource explainSoundSpeaker;
    MonitorController monitorController;

    // Start is called before the first frame update
    void Start()
    {
        // オーディオソースの取得
        systemSoundSpeaker = GameObject.Find("systemSoundController").GetComponent<AudioSource>();
        effectSoundSpeaker = GameObject.Find("effectSoundController").GetComponent<AudioSource>();
        explainSoundSpeaker = GameObject.Find("explainSoundController").GetComponent<AudioSource>();

        monitorController=GetComponent<MonitorController>();
    }

    // 災害時のエレベータ音声再生
    public void PlayDisasterSound(){
        systemSoundSpeaker.PlayOneShot(disasterSounds[monitorController.mode]);
    }

    // 到着階の音声再生
    public void PlayFloorSound(int destination){
        systemSoundSpeaker.PlayOneShot(floorSounds[destination-1]);
    }
    // エレベータの共通音声再生　上へまいります等
    public void PlaySystemSound(int type){
        systemSoundSpeaker.PlayOneShot(systemSounds[type]);
    }

    // 各災害時の効果音再生　火災警報音等
    public void PlayEffectSound(int type){
        effectSoundSpeaker.PlayOneShot(effectSounds[type]);

    }

    // 共通ルートの説明アナウンス再生　これはエレベータ災害体験システムです等
    public void PlayExplainCommonSound(int type, int langMode){
        if(langMode == 0){
            explainSoundSpeaker.PlayOneShot(jpExplainCommonSounds[type]);
            Debug.Log(explainSoundSpeaker.isPlaying);
            Debug.Log("AudioController: エレベーター災害体験システムです(日)");
        } else if(langMode == 1){
            explainSoundSpeaker.PlayOneShot(enExplainCommonSounds[type]);
            Debug.Log(explainSoundSpeaker.isPlaying);
            Debug.Log("AudioController: エレベーター災害体験システムです(英)");
        }
    }

    // 各災害時動作の説明文の音声再生
    public void PlayExplainAboutSound(int type, int langMode){
        if(langMode == 0){
            explainSoundSpeaker.PlayOneShot(jpExplainAboutSounds[type]);
        } else if(langMode == 1){
            explainSoundSpeaker.PlayOneShot(enExplainAboutSounds[type]);
        }
    }
    
    public void PlayBeforeExprcExpSound1(int type, int langMode){
        if(langMode == 0){
            explainSoundSpeaker.PlayOneShot(jpBeforeExprcExpSounds1[type]);
        } else if(langMode == 1){
            explainSoundSpeaker.PlayOneShot(enBeforeExprcExpSounds1[type]);
        }
    }

    public void PlayBeforeExprcExpSound2(int type, int langMode){
        if(langMode == 0){
            explainSoundSpeaker.PlayOneShot(jpBeforeExprcExpSounds2[type]);
        } else if(langMode == 1){
            explainSoundSpeaker.PlayOneShot(enBeforeExprcExpSounds2[type]);
        }
    }

    //エレベーター内のディスプレイの説明音声
    public void PlayExplainInsideSound(int type, int langMode)
    {
        if (langMode == 0)
        {
            explainSoundSpeaker.PlayOneShot(jpExplainInsideDisplay[type]);
        }
        else if (langMode == 1)
        {
            explainSoundSpeaker.PlayOneShot(enExplainInsideDisplay[type]);
        }
    }

    //災害時の停止階の説明の音声
    public void PlayExplainStopFloorSound(int type, int langMode){
        if(langMode == 0){
            explainSoundSpeaker.PlayOneShot(jpExplainStopFloor[type]);
        } else if(langMode == 1){
            explainSoundSpeaker.PlayOneShot(enExplainStopFloor[type]);
        }
    }

    //エレベーター外のディスプレイの説明音声
    public void PlayExplainOutsideSound(int type, int langMode){
        if(langMode == 0){
            explainSoundSpeaker.PlayOneShot(jpExplainOutsideDisplay[type]);
        } else if(langMode == 1){
            explainSoundSpeaker.PlayOneShot(enExplainOutsideDisplay[type]);
        }
    }

    // 各災害体験開始のアナウンス再生
    public void PlayExplainStartExpSound(int type, int langMode){
        if(langMode == 0){
            explainSoundSpeaker.PlayOneShot(jpExplainStartExpSounds[type]);
        } else if(langMode == 1){
            explainSoundSpeaker.PlayOneShot(enExplainStartExpSounds[type]);
        }
    }

    // 各管制運転体験開始のアナウンス
    public void PlayExplainWorkingSound(int type, int langMode){
        if(langMode == 0){
            explainSoundSpeaker.PlayOneShot(jpExplainWorkingSounds[type]);
        }else if(langMode == 1){
            explainSoundSpeaker.PlayOneShot(enExplainWorkingSounds[type]);
        }

    }

    // 各災害発生のアナウンス
    public void PlayExplainHappenSound(int type, int langMode){
        if(langMode == 0){
            explainSoundSpeaker.PlayOneShot(jpExplainHappenSounds[type]);
        }else if(langMode == 1){
            explainSoundSpeaker.PlayOneShot(enExplainHappenSounds[type]);
        }
    }

    // 災害時のエレベータ音声再生
    public void PlayN_backSound(int type){
        systemSoundSpeaker.PlayOneShot(n_backSounds[type]);
    }

    //音が鳴り終わるまで待機する関数
    IEnumerator Cor(AudioSource audioSource,AudioClip sound)
    {
        //鳴り始めたことを表示
        //Debug.Log("音声開始");

        //終了まで待機
        yield return new WaitWhile(() => audioSource.isPlaying);
        audioSource.PlayOneShot(sound);

        //終了したことを表示
        //Debug.Log("音声終了");
    }
    public void StopAllAudio(){
        systemSoundSpeaker.Stop();
        effectSoundSpeaker.Stop();
        explainSoundSpeaker.Stop();
    }

    public void PauseAllAudio(){
        systemSoundSpeaker.Pause();
        effectSoundSpeaker.Pause();
        explainSoundSpeaker.Pause();
    }

    public void UnPauseAllAudio(){
        systemSoundSpeaker.UnPause();
        effectSoundSpeaker.UnPause();
        explainSoundSpeaker.UnPause();
    }

    public void StopSystemAudio(){
        systemSoundSpeaker.Stop();
    }
    public void StopEffectAudio(){
        effectSoundSpeaker.Stop();
    }
    public void StopExplainAudio(){
        explainSoundSpeaker.Stop();
    }

/*
    public void PlayDisasterSound(){
        // 音声が鳴り終わるまで待つ
        //StartCoroutine(Cor(systemSoundSpeaker,disasterSounds[monitorController.mode]));

        systemSoundSpeaker.PlayOneShot(disasterSounds[monitorController.mode]);
    }
*/
}
