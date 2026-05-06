using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 地震の揺れのスクリプト　特定ポイントへの移動でやろうとしたやつ　未使用
// 理由はよくわからないが，MoveTowards関数がうまく動作しなかったので使用を断念
// 現時点ではPointMove.csを地震の揺れのスクリプトとして使用している
public class EarthquakeMove : MonoBehaviour
{
    public GameObject earthPoint;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = Vector3.MoveTowards(transform.position,earthPoint.transform.position,0.1f);
    }
}
