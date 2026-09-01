using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
//using Oculus.Interaction;
//using Unity.VisualScripting;

public class UIController : MonoBehaviour
{
    public GameObject selectUI;
    public GameObject beforeExprcExpUI;
    public GameObject startUI;
    public GameObject expInsideUI;
    public GameObject expOutsideUI;
    public GameObject endUI;

    //制作・協力のテキストオブジェクト
    // TextMeshProUGUI jpProductionSelectText;
    public TextMeshProUGUI jpProductionExplainText;
    public TextMeshProUGUI jpProductionEndText;

    //災害説明テキストオブジェクト
    public Image BeforeExprcExpImage;
    
    public TextMeshProUGUI[] jpBeforeExprcTitleTextTemp = new TextMeshProUGUI[4];
    public TextMeshProUGUI[] jpBeforeExprcExpTextTemp = new TextMeshProUGUI[8];

    public Image[] beforeExprcExpPngTemp = new Image[8];


    //体験開始ボタンのオブジェクト
    public GameObject explainEndButton;
    public GameObject explainProceedButton;
    public GameObject startButton;

    // エレベーター内ディスプレイの説明に関するオブジェクト
    public Image expInsideDisplayImage;
    public TextMeshProUGUI jpExpInsideDisplayTitle;
    public TextMeshProUGUI[] jpExpInsideDisplayText = new TextMeshProUGUI[4];
    public TextMeshProUGUI jpExpInsideDisplayCaption;
    public Image[] expInsideDisplayPngTemp = new Image[8];
    public GameObject expInsideDisplayButton;

    // エレベーターの停止階の説明に関するオブジェクト
    public TextMeshProUGUI jpExpStopFloorTitle;
    public TextMeshProUGUI[] jpExpStopFloorText = new TextMeshProUGUI[4];
    public Image[] expStopFloorPngTemp = new Image[5];
    public GameObject expStopFloorButton;


    // エレベーター外ディスプレイの説明に関するオブジェクト
    public Image expOutsideDisplayImage;
    public TextMeshProUGUI jpExpOutsideDisplayTitle;
    public TextMeshProUGUI[] jpExpOutsideDisplayText = new TextMeshProUGUI[4];
    public TextMeshProUGUI jpExpOutsideDisplayCaption;
    public Image[] expOutsideDisplayPngTemp = new Image[8];
    public GameObject expOutsideDisplayButton;

    //体験終了テキストのオブジェクト
    public Image endImage;
    public TextMeshProUGUI jpEndTitle;
    public TextMeshProUGUI jpEndText;
    public Image endPngTemp;
    public GameObject endButton;

    //英語テキスト

    //制作・協力のテキストオブジェクト
    //TextMeshProUGUI jpProductionSelectText;
    public TextMeshProUGUI enProductionExplainText;
    public TextMeshProUGUI enProductionEndText;

    //災害説明テキストオブジェクト
    
    public TextMeshProUGUI[] enBeforeExprcTitleTextTemp = new TextMeshProUGUI[4];
    public TextMeshProUGUI[] enBeforeExprcExpTextTemp = new TextMeshProUGUI[8];

    // エレベーター内ディスプレイの説明に関するオブジェクト
    public TextMeshProUGUI enExpInsideDisplayTitle;
    public TextMeshProUGUI[] enExpInsideDisplayText = new TextMeshProUGUI[4];
    public TextMeshProUGUI enExpInsideDisplayCaption;
    

    // エレベーターの停止階の説明に関するオブジェクト
    public TextMeshProUGUI enExpStopFloorTitle;
    public TextMeshProUGUI[] enExpStopFloorText = new TextMeshProUGUI[4];

    // エレベーター外ディスプレイの説明に関するオブジェクト
    public TextMeshProUGUI enExpOutsideDisplayTitle;
    public TextMeshProUGUI[] enExpOutsideDisplayText = new TextMeshProUGUI[4];
    public TextMeshProUGUI enExpOutsideDisplayCaption;

    //体験終了テキストのオブジェクト
    public TextMeshProUGUI enEndTitle;
    public TextMeshProUGUI enEndText;

    // 言語ごとのテキストを一括取得する配列
    public GameObject[] jpTexts;
    public GameObject[] enTexts;


    // 体験前説明のテキスト・pngを管理するdictionary
    Dictionary<string, Dictionary<int, TextMeshProUGUI>> jpBeforeExprcExpText = new Dictionary<string, Dictionary<int, TextMeshProUGUI>>();
    Dictionary<string, Dictionary<int, TextMeshProUGUI>> enBeforeExprcExpText = new Dictionary<string, Dictionary<int, TextMeshProUGUI>>();
    Dictionary<string, Dictionary<int, Image>> beforeExprcExpPng = new Dictionary<string, Dictionary<int, Image>>();

    // エレベーター内ディスプレイのpngを管理するdictionary
    Dictionary<string, Dictionary<int, Image>> expInsideDisplayPng = new Dictionary<string, Dictionary<int, Image>>();

    // 停止階説明のpngを管理するdictionary
    Dictionary<string, Dictionary<int, Image>> expStopFloorPng = new Dictionary<string, Dictionary<int, Image>>();

    // エレベーター外ディスプレイを管理するdictionary
    Dictionary<string, Dictionary<int, Image>> expOutsideDisplayPng = new Dictionary<string, Dictionary<int, Image>>();


    //Dictionary<string, Dictionary<int, RawImage>> jpBeforeExprcExpGif = new Dictionary<string, Dictionary<int, RawImage>>();

    void Start()
    {
        // 日本語体験前説明テキストのdictionaryへの登録
        jpBeforeExprcExpText["fire"] = new Dictionary<int, TextMeshProUGUI>();
        jpBeforeExprcExpText["rain"] = new Dictionary<int, TextMeshProUGUI>();
        jpBeforeExprcExpText["earth"] = new Dictionary<int, TextMeshProUGUI>();
        jpBeforeExprcExpText["elect"] = new Dictionary<int, TextMeshProUGUI>();

        jpBeforeExprcExpText["fire"][0] = jpBeforeExprcTitleTextTemp[0/*TitleFire*/];
        jpBeforeExprcExpText["fire"][1] = jpBeforeExprcExpTextTemp[0/*ExpFire1*/];
        jpBeforeExprcExpText["fire"][2] = jpBeforeExprcExpTextTemp[1/*ExpFire2*/];
        jpBeforeExprcExpText["rain"][0] = jpBeforeExprcTitleTextTemp[1/*TitleRain*/];
        jpBeforeExprcExpText["rain"][1] = jpBeforeExprcExpTextTemp[2/*ExpRain1*/];
        jpBeforeExprcExpText["rain"][2] = jpBeforeExprcExpTextTemp[3/*ExpRain2*/];
        jpBeforeExprcExpText["earth"][0] = jpBeforeExprcTitleTextTemp[2/*TitleEarth*/];
        jpBeforeExprcExpText["earth"][1] = jpBeforeExprcExpTextTemp[4/*ExpEarth1*/];
        jpBeforeExprcExpText["earth"][2] = jpBeforeExprcExpTextTemp[5/*ExpEarth2*/];
        jpBeforeExprcExpText["elect"][0] = jpBeforeExprcTitleTextTemp[3/*TitleElect*/];
        jpBeforeExprcExpText["elect"][1] = jpBeforeExprcExpTextTemp[6/*ExpElect1*/];
        jpBeforeExprcExpText["elect"][2] = jpBeforeExprcExpTextTemp[7/*ExpElect2*/];

        // 英語体験前説明テキストのdictionaryへの登録
        enBeforeExprcExpText["fire"] = new Dictionary<int, TextMeshProUGUI>();
        enBeforeExprcExpText["rain"] = new Dictionary<int, TextMeshProUGUI>();
        enBeforeExprcExpText["earth"] = new Dictionary<int, TextMeshProUGUI>();
        enBeforeExprcExpText["elect"] = new Dictionary<int, TextMeshProUGUI>();

        enBeforeExprcExpText["fire"][0] = enBeforeExprcTitleTextTemp[0/*TitleFire*/];
        enBeforeExprcExpText["fire"][1] = enBeforeExprcExpTextTemp[0/*ExpFire1*/];
        enBeforeExprcExpText["fire"][2] = enBeforeExprcExpTextTemp[1/*ExpFire2*/];
        enBeforeExprcExpText["rain"][0] = enBeforeExprcTitleTextTemp[1/*TitleRain*/];
        enBeforeExprcExpText["rain"][1] = enBeforeExprcExpTextTemp[2/*ExpRain1*/];
        enBeforeExprcExpText["rain"][2] = enBeforeExprcExpTextTemp[3/*ExpRain2*/];
        enBeforeExprcExpText["earth"][0] = enBeforeExprcTitleTextTemp[2/*TitleEarth*/];
        enBeforeExprcExpText["earth"][1] = enBeforeExprcExpTextTemp[4/*ExpEarth1*/];
        enBeforeExprcExpText["earth"][2] = enBeforeExprcExpTextTemp[5/*ExpEarth2*/];
        enBeforeExprcExpText["elect"][0] = enBeforeExprcTitleTextTemp[3/*TitleElect*/];
        enBeforeExprcExpText["elect"][1] = enBeforeExprcExpTextTemp[6/*ExpElect1*/];
        enBeforeExprcExpText["elect"][2] = enBeforeExprcExpTextTemp[7/*ExpElect2*/];

        // 体験前説明画像のdictionaryへの追加
        beforeExprcExpPng["fire"] = new Dictionary<int, Image>();
        beforeExprcExpPng["rain"] = new Dictionary<int, Image>();
        beforeExprcExpPng["earth"] = new Dictionary<int, Image>();
        beforeExprcExpPng["elect"] = new Dictionary<int, Image>();

        beforeExprcExpPng["fire"][1] = beforeExprcExpPngTemp[0/*PngAnnounceFire1*/];
        beforeExprcExpPng["fire"][2] = beforeExprcExpPngTemp[1/*PngAnnounceFire2*/];
        beforeExprcExpPng["rain"][1] = beforeExprcExpPngTemp[2/*PngAnnounceRain1*/];
        beforeExprcExpPng["rain"][2] = beforeExprcExpPngTemp[3/*PngAnnounceRain2*/];
        beforeExprcExpPng["earth"][1] = beforeExprcExpPngTemp[4/*PngAnnounceEarth1*/];
        beforeExprcExpPng["earth"][2] = beforeExprcExpPngTemp[5/*PngAnnounceEarth2*/];
        beforeExprcExpPng["elect"][1] = beforeExprcExpPngTemp[6/*PngAnnounceElectrocity1*/];
        beforeExprcExpPng["elect"][2] = beforeExprcExpPngTemp[7/*PngAnnounceElectrocity2*/];


        // エレベーター内ディスプレイの表示説明画像のdictionaryへの追加
        expInsideDisplayPng["fire"] = new Dictionary<int, Image>();
        expInsideDisplayPng["rain"] = new Dictionary<int, Image>();
        expInsideDisplayPng["earth"] = new Dictionary<int, Image>();
        expInsideDisplayPng["elect"] = new Dictionary<int, Image>();

        expInsideDisplayPng["fire"][1] = expInsideDisplayPngTemp[0/*PngExpInsideDisplayFire1*/];
        expInsideDisplayPng["fire"][2] = expInsideDisplayPngTemp[1/*PngExpInsideDisplayFire2*/];
        expInsideDisplayPng["rain"][1] = expInsideDisplayPngTemp[2/*PngExpInsideDisplayRain1*/];
        expInsideDisplayPng["rain"][2] = expInsideDisplayPngTemp[3/*PngExpInsideDisplayRain2*/];
        expInsideDisplayPng["earth"][1] = expInsideDisplayPngTemp[4/*PngExpInsideDisplayEarth1*/];
        expInsideDisplayPng["earth"][2] = expInsideDisplayPngTemp[5/*PngExpInsideDisplayEarth2*/];
        expInsideDisplayPng["elect"][1] = expInsideDisplayPngTemp[6/*PngExpInsideDisplayElectrocity1*/];
        expInsideDisplayPng["elect"][2] = expInsideDisplayPngTemp[7/*PngExpInsideDisplayElectrocity2*/];

        // 停止階説明画像のdictionaryへの追加
        expStopFloorPng["fire"] = new Dictionary<int, Image>();
        expStopFloorPng["rain"] = new Dictionary<int, Image>();
        expStopFloorPng["earth"] = new Dictionary<int, Image>();
        expStopFloorPng["elect"] = new Dictionary<int, Image>();

        expStopFloorPng["fire"][1] = expStopFloorPngTemp[0/*JpPngExpStopFloorFire*/];
        expStopFloorPng["rain"][1] = expStopFloorPngTemp[1/*JpPngExpStopFloorRain*/];
        expStopFloorPng["earth"][1] = expStopFloorPngTemp[2/*JpPngExpStopFloorEarth*/];
        expStopFloorPng["elect"][1] = expStopFloorPngTemp[3/*JpPngExpStopFloorElectrocity*/];

        // エレベーター外ディスプレイ説明画像のdictionaryへの追加
        expOutsideDisplayPng["fire"] = new Dictionary<int, Image>();
        expOutsideDisplayPng["rain"] = new Dictionary<int, Image>();
        expOutsideDisplayPng["earth"] = new Dictionary<int, Image>();
        expOutsideDisplayPng["elect"] = new Dictionary<int, Image>();

        expOutsideDisplayPng["fire"][1] = expOutsideDisplayPngTemp[0/*PngOutsideDisplayFire1*/];
        expOutsideDisplayPng["fire"][2] = expOutsideDisplayPngTemp[1/*PngOutsideDisplayFire2*/];
        expOutsideDisplayPng["rain"][1] = expOutsideDisplayPngTemp[2/*PngOutsideDisplayRain1*/];
        expOutsideDisplayPng["rain"][2] = expOutsideDisplayPngTemp[3/*PngOutsideDisplayRain2*/];
        expOutsideDisplayPng["earth"][1] = expOutsideDisplayPngTemp[4/*PngOutsideDisplayEarth1*/];
        expOutsideDisplayPng["earth"][2] = expOutsideDisplayPngTemp[5/*PngOutsideDisplayEarth2*/];
        expOutsideDisplayPng["elect"][1] = expOutsideDisplayPngTemp[6/*PngOutsideDisplayElectrocity1*/];
        expOutsideDisplayPng["elect"][2] = expOutsideDisplayPngTemp[7/*PngOutsideDisplayElectrocity2*/];
    }

    public void SetSelectUI(bool flag)
    {
        selectUI.SetActive(flag);

    }

    public void SetBeforeExprcExpUI(string disStr, bool flag)
    {
        Debug.Log("SetBeforeExprcExpUI Start");
        beforeExprcExpUI.SetActive(flag);

        
        
        if (flag)
        {
            //各説明文の表示
            if (jpBeforeExprcExpText.ContainsKey(disStr))
            {
                explainProceedButton.SetActive(true);
                explainEndButton.SetActive(false);

                jpBeforeExprcExpText[disStr][0].enabled = true;
                jpBeforeExprcExpText[disStr][1].enabled = true;

                enBeforeExprcExpText[disStr][0].enabled = true;
                enBeforeExprcExpText[disStr][1].enabled = true;

                Debug.Log("BeforeExprcExpText displayed");
            }
            // 各gifの表示
            if (beforeExprcExpPng.ContainsKey(disStr))
            {
                beforeExprcExpPng[disStr][1].enabled = true;
                Debug.Log("BeforeExprcExpGif displayed");
            }
        }
        else
        {
            //日本語テキストの非表示
            foreach (var disaster in jpBeforeExprcExpText)
            {
                foreach (var texts in disaster.Value)
                {
                    texts.Value.enabled = false;
                }
            }

            //英語テキストの非表示
            foreach (var disaster in enBeforeExprcExpText)
            {
                foreach (var texts in disaster.Value)
                {
                    texts.Value.enabled = false;
                }
            }

            //画像の非表示
            foreach (var disaster in beforeExprcExpPng)
            {
                foreach (var image in disaster.Value)
                {
                    image.Value.enabled = false;
                }
            }


            // // 各説明文の非表示
            // for (int i = 0; i < 4; i++)
            // {
            //     //BeforeExprcExpImage.enabled = false;
            //     jpBeforeExprcTitleText[i].enabled = false;
            //     jpBeforeExprcExpText[i].enabled = false;
            //     enBeforeExprcTitleText[i].enabled = false;
            //     enBeforeExprcExpText[i].enabled = false;
            // }
        }
        Debug.Log("BeforeExprcExpUI Set Active End");
    }

    public void SetStartUI(bool flag)
    {
        startUI.SetActive(flag);
    }

    public void SetExpInsideUI(int disNum, bool flag)
    {
        // UIの有効化・無効化
        expInsideUI.SetActive(flag);
        expInsideDisplayButton.SetActive(flag);
        //expInsideDisplayImage.enabled = flag;

        string disStr;
        switch (disNum)
        {
            case 0:
                disStr = "fire";
                break;
            case 1:
                disStr = "rain";
                break;
            case 2:
                disStr = "earth";
                break;
            case 3:
                disStr = "elect";
                break;
            default:
                disStr = "false";
                break;
        }

        if (flag)
        {
            //各説明文の表示
            jpExpInsideDisplayTitle.enabled = flag;
            jpExpInsideDisplayText[disNum].enabled = flag;
            jpExpInsideDisplayCaption.enabled = flag;

            enExpInsideDisplayTitle.enabled = flag;
            enExpInsideDisplayText[disNum].enabled = flag;
            enExpInsideDisplayCaption.enabled = flag;

            // 各画像の表示
            if (expInsideDisplayPng.ContainsKey(disStr))
            {
                expInsideDisplayPng[disStr][1].enabled = true;
                expInsideDisplayPng[disStr][2].enabled = true;
                Debug.Log("expInsideDisplayPng displayed");
            }
        }
        else
        {
            //各説明文の非表示
            jpExpInsideDisplayTitle.enabled = flag;
            enExpInsideDisplayTitle.enabled = flag;

            jpExpInsideDisplayCaption.enabled = flag;
            enExpInsideDisplayCaption.enabled = flag;

            for (int i = 0; i < 4; i++)
            {
                jpExpInsideDisplayText[i].enabled = false;
                enExpInsideDisplayText[i].enabled = false;
            }

            //画像の非表示
            foreach (var disaster in expInsideDisplayPng)
            {
                foreach (var image in disaster.Value)
                {
                    image.Value.enabled = false;
                }
            }
        }
    }
    public void SetExpStopFloorUI(int disNum, bool flag)
    {
        
        // UIの有効化・無効化
        expInsideUI.SetActive(flag);
        expStopFloorButton.SetActive(flag);
        //expInsideDisplayImage.enabled = flag;

        if (flag)
        {
            //各説明文の表示
            jpExpStopFloorTitle.enabled = flag;
            jpExpStopFloorText[disNum].enabled = flag;
            enExpStopFloorTitle.enabled = flag;
            enExpStopFloorText[disNum].enabled = flag;

        string disStr;
        switch (disNum)
        {
            case 0:
                disStr = "fire";
                break;
            case 1:
                disStr = "rain";
                break;
            case 2:
                disStr = "earth";
                break;
            case 3:
                disStr = "elect";
                break;
            default:
                disStr = "false";
                break;
        }

        //     // 各画像の表示
            if (expStopFloorPng.ContainsKey(disStr))
            {
                expStopFloorPng[disStr][1].enabled = true;
                Debug.Log("expStopFloorPng displayed");
            }
        }
        else
        {
            //各説明文の非表示
            for (int i = 0; i < 4; i++)
            {
                jpExpStopFloorTitle.enabled = flag;
                enExpStopFloorTitle.enabled = flag;
                jpExpStopFloorText[i].enabled = false;
                enExpStopFloorText[i].enabled = false;
            }

            //画像の非表示
            foreach (var disaster in expStopFloorPng)
            {
                foreach (var image in disaster.Value)
                {
                    image.Value.enabled = false;
                }
            }
        }
    }

    public void SetExpOutsideUI(int disNum, bool flag)
    {
        //UIの有効化・無効化
        expOutsideUI.SetActive(flag);
        expOutsideDisplayImage.enabled = flag;
        expOutsideDisplayButton.SetActive(flag);

        string disStr;
        switch (disNum)
        {
            case 0:
                disStr = "fire";
                break;
            case 1:
                disStr = "rain";
                break;
            case 2:
                disStr = "earth";
                break;
            case 3:
                disStr = "elect";
                break;
            default:
                disStr = "false";
                break;
        }

        if (flag)
        {
            //各説明文の表示
            jpExpOutsideDisplayTitle.enabled = flag;
            jpExpOutsideDisplayText[disNum].enabled = flag;
            jpExpOutsideDisplayCaption.enabled = flag;

            enExpOutsideDisplayTitle.enabled = flag;
            enExpOutsideDisplayText[disNum].enabled = flag;
            enExpOutsideDisplayCaption.enabled = flag;

            // 各画像の表示
            if (expOutsideDisplayPng.ContainsKey(disStr))
            {
                expOutsideDisplayPng[disStr][1].enabled = true;
                expOutsideDisplayPng[disStr][2].enabled = true;
                Debug.Log("expInsideDisplayPng displayed");
            }
        }
        else
        {
            //各説明文の非表示
            for (int i = 0; i < 4; i++)
            {
                jpExpOutsideDisplayText[i].enabled = false;
                enExpOutsideDisplayText[i].enabled = false;
            }

            //画像の非表示
            foreach (var disaster in expOutsideDisplayPng)
            {
                foreach (var image in disaster.Value)
                {
                    image.Value.enabled = false;
                }
            }
        }
    }

    public void SetEndUI(bool flag)
    {
        endUI.SetActive(flag);
    }

    // 文章を「次へ」するための関数
    public void ProceedText(Dictionary<string, Dictionary<int, TextMeshProUGUI>> procText, Dictionary<string, Dictionary<int, Image>> procGif, string disStr, int textNum)
    {
        // string disStr = "false";
        // int textNum = -1;

        // // 現在表示されているテキストを取得
        // foreach (var disaster in procText)
        // {
        //     foreach (var text in disaster.Value)
        //     {
        //         if ((text.Value.enabled == true) && (text.Key > 0))
        //         {
        //             disStr = disaster.Key;
        //             textNum = text.Key;
        //         }
        //     }
        // }

        if (procText.ContainsKey(disStr) && procText[disStr].ContainsKey(textNum + 1))
        {
            //表示中のテキストを非表示
            procText[disStr][textNum].enabled = false;
            //次のテキストを表示
            procText[disStr][textNum + 1].enabled = true;
        }
        if (procGif.ContainsKey(disStr) && procGif[disStr].ContainsKey(textNum + 1))
        {
            //表示中Gifを非表示
            procGif[disStr][textNum].enabled = false;
            //次のGifを表示
            procGif[disStr][textNum + 1].enabled = true;
        }

    }

    // 文章を「戻る」するための関数
    public void BackText(Dictionary<string, Dictionary<int, TextMeshProUGUI>> backText, Dictionary<string, Dictionary<int, Image>> backGif, string disStr, int textNum)
    {
        // string disStr = "false";
        // int textNum = -1;

        // // 現在表示されているテキストを取得
        // foreach (var disaster in backText)
        // {
        //     foreach (var text in disaster.Value)
        //     {
        //         if ((text.Value.enabled == true) && (text.Key > 0))
        //         {
        //             disStr = disaster.Key;
        //             textNum = text.Key;
        //         }
        //     }
        // }

        if (backText.ContainsKey(disStr) && (textNum > 1))
        {
            //表示中のテキストを非表示
            backText[disStr][textNum].enabled = false;
            //次のテキストを表示
            backText[disStr][textNum - 1].enabled = true;
        }
        if (backGif.ContainsKey(disStr) && (textNum > 1))
        {
            //表示中のテキストを非表示
            backGif[disStr][textNum].enabled = false;
            //次のテキストを表示
            backGif[disStr][textNum - 1].enabled = true;
        }
    }

    public void ProcBeforeExprcExpUI(string disStr, int textNum)
    {
        explainProceedButton.SetActive(false);
        explainEndButton.SetActive(true);
        ProceedText(jpBeforeExprcExpText, beforeExprcExpPng, disStr, textNum);
        ProceedText(enBeforeExprcExpText, beforeExprcExpPng, disStr, textNum);
        //ProceedText(enBeforeExprcExpText, disStr, textNum);
    }

    public void BackBeforeExprcExpUI(string disStr, int textNum)
    {
        BackText(jpBeforeExprcExpText, beforeExprcExpPng, disStr, textNum);
        BackText(enBeforeExprcExpText, beforeExprcExpPng, disStr, textNum);
        //BackText(enBeforeExprcExpText, disStr, textNum);
    }






    public void SetLanguage(int langMode)
    {
        Debug.Log("SetLanguage Start");
        if (langMode == 0)
        {
            // 日本語テキスト有効化
            for (int i = 0; i < jpTexts.Length; i++)
            {
                jpTexts[i].SetActive(true);
            }
            Debug.Log("Japanese active");

            // 英語テキスト無効化
            for (int i = 0; i < enTexts.Length; i++)
            {
                enTexts[i].SetActive(false);
            }
            Debug.Log("English inactive");
        }
        else if (langMode == 1)
        {
            // 日本語テキスト無効化
            for (int i = 0; i < jpTexts.Length; i++)
            {
                jpTexts[i].SetActive(false);
            }
            // 英語テキスト有効化
            for (int i = 0; i < enTexts.Length; i++)
            {
                enTexts[i].SetActive(true);
            }
        }
    }
}
