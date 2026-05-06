using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 地震・停電時のエレベーターの動きのスクリプト
public class PointMove : MonoBehaviour
{
    private int count = 0;
    bool isMoving = false; // エレベーターが（地震等で）動いているか
    public bool isEarth = false; // 地震シナリオか

    Transform myTransform; // エレベーターの現在位置
    Vector3 posOrigin; // エレベーターの初期位置

    float moveSize=0.0f;

    // Start is called before the first frame update
    void Start()
    {
        myTransform = this.transform;
        //posOrigin = myTransform.position;
        //StartCoroutine(ElectStop());
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void FixedUpdate(){
        //
        if(isMoving){
            if((count % 4) == 0){
                moveSize*=-1; // 移動方向の反転
            }
        // 地震停止時，緩やかに揺れがおさまる
        } else if(isEarth){
            if(Mathf.Abs(moveSize)>0.0006f){
                moveSize+=-0.0005f*(moveSize>0 ? 1.0f:-1.0f);
            }else{
                moveSize=0;
                //isEarth = false;
            }
            myTransform.position += (posOrigin-myTransform.position)*(1/(0.012f/0.0005f));
        }

        // 左右の揺れ
        myTransform.position += Vector3.left*moveSize;
        // 上下の揺れ
        if((count % 5) == 1){
            myTransform.position += Vector3.up * moveSize;
        }else if((count % 5) == 3){
            myTransform.position -= Vector3.up * moveSize;
        }
        count += 1;
    }

    // 地震動作の開始
    public void MoveStart()
    {
        count = 0;
        posOrigin = myTransform.position;
        moveSize=0.012f;
        isMoving = true;
        isEarth = true;
    }

    // 地震動作終了
    public void MoveEnd(){
        isMoving = false;
        count = 0;
    }
/*
    public IEnumerator ElectStop()
    {
        Debug.Log("ElectStop START");
        myTransform.position += Vector3.up * 0.01f;
        yield return new WaitForSeconds(0.1f);
        

        Debug.Log("ElectStop END");
    }
*/

    // 停電時の動き
    public void ElectStop(){
        posOrigin = myTransform.position;
        //moveSize = 0.012f;
        isEarth = true;
        myTransform.position += Vector3.up * 0.03f;
    }
}
