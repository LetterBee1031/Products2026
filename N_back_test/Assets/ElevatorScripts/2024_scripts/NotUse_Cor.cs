using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// 未使用　コルーチンのテスト用スクリプト
// コルーチン自体は動作することが分かった
// しかし，audio.isPlayingを用いた音声終了判定がうまくいかないことが分かった
// そのためMainController.csのほうで，強引な手法で音声が被らないようにしている
public class Cor : MonoBehaviour
{
    Material mat;
    // Start is called before the first frame update
    void Start()
    {
        mat = this.GetComponent<Renderer>().material;
        StartCoroutine(TestCor());
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    IEnumerator cor()
    {
        IEnumerator enumerator = TestCor();
        // TestCorが終わるまで待つ
        yield return enumerator;

        Debug.Log("Main END");

    }

    // 2f待つ
    IEnumerator TestCor()
    {
        
        Debug.Log("TestCor START");
        mat.color = Color.red;
        yield return new WaitForSeconds(4f);
        mat.color = Color.blue;
        Debug.Log("TestCor END");

    }
}
