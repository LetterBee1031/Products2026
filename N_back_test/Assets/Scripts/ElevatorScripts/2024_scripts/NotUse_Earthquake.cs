using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 地震時の揺れのスクリプト，アニメーターバージョン　未使用
// 揺れ方が思いのほか不自然になってしまったので，使用を断念
// 現時点ではPointMove.csを地震の揺れのスクリプトとして使用している
public class Earthquake : MonoBehaviour
{
    [SerializeField]
    [Tooltip("横揺れのアニメータ")]
    private Animator EarthquakeAnimator;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void earthStart(){
        EarthquakeAnimator.SetBool("ContinueEarth", true);
    }

    public void earthEnd(){
        EarthquakeAnimator.SetBool("ContinueEarth", false);
    }
}
