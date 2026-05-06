using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 停電時の急停止のスクリプト　未使用
// PointMove.csとは別に停電時動作専用のスクリプトを作ろうとした
// PointMove.csから持ってきたコードがなぜかうまく動作しなかった．何故？あっちでは動いてるが？
// 結局PointMove.csに統合した
public class ElectStop : MonoBehaviour
{

    Transform myTransform;
    Vector3 posOrigin;

    bool isElectStop = false;

    float moveSize = 0.0f;
    // Start is called before the first frame update
    void Start()
    {
        myTransform = this.transform;
    }

    // Update is called once per frame
    void Update()
    {

    }

    void FixedUpdate()
    {
        if (isElectStop)
        {
            if (Mathf.Abs(moveSize) > 0.0006f)
            {
                moveSize += -0.0005f * (moveSize > 0 ? 1.0f : -1.0f);
            }
            else
            {
                moveSize = 0;
                isElectStop = false;
            }
            myTransform.position += (posOrigin - myTransform.position) * (1 / (0.012f / 0.0005f));
        }
    }
    // 停電時の動き
    public void StopMove()
    {
        isElectStop = true;
        posOrigin = myTransform.position;
        moveSize = 0.01f;
        myTransform.position += Vector3.up * moveSize;
    }
}

