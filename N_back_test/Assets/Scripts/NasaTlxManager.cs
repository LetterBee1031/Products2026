using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NasaTlxManager : MonoBehaviour
{
    [Header("NASA-TLX UI")]
    [SerializeField] private GameObject nasaTlxPanel; // World Space UI panel
    [SerializeField] private TMP_Text progressText; // 進捗表示 1 / 6
    [SerializeField] private TMP_Text questionTitle; // 質問タイトル表示
    [SerializeField] private TMP_Text questionDescription; // 質問説明表示
    [SerializeField] private TMP_Text lowLabel; // スライダー左側ラベル
    [SerializeField] private TMP_Text highLabel; // スライダー右側ラベル
    [SerializeField] private Slider answerSlider; // 回答用スライダー
    [SerializeField] private Button minusButton; // スライダー値を-1
    [SerializeField] private Button plusButton; // スライダー値を+1
    [SerializeField] private Button confirmButton; // 回答確定ボタン

    [Header("Question Data")]
    [SerializeField] private NasaTlxQuestion[] questions = new NasaTlxQuestion[6]; // 6つのNASA-TLX質問

    [Header("Answer Range")]
    [SerializeField] private int minValue = 0; // スライダー最小値
    [SerializeField] private int maxValue = 20; // スライダー最大値

    [Header("Dependencies")]
    [SerializeField] private RequestSender requestSender; // 通信処理は RequestSender に委譲

    private int currentQuestionIndex; // 現在の質問番号
    private bool hasAnswered; // 現在の質問の回答が入力済みか
    private bool isSending; // 送信中フラグ
    private bool suppressSliderEvent; // 初期値設定時にスライダーイベントを無視する

    private readonly Dictionary<string, int> answers = new Dictionary<string, int>(6); // 回答の一時保存
    private string currentBlockId = ""; // ブロックIDを保持
    private string currentUserId = ""; // StartQuestionnaire で受け取る userID

    // デフォルトの質問ID  未入力の場合使用
    private static readonly string[] DefaultQuestionIds =
    {
        "mental_demand",
        "physical_demand",
        "temporal_demand",
        "performance",
        "effort",
        "frustration"
    };

    // デフォルトの質問タイトル  未入力の場合使用
    private static readonly string[] DefaultQuestionTitles =
    {
        "知的・知覚的要求",
        "身体的要求",
        "タイムプレッシャー",
        "作業成績",
        "努力",
        "フラストレーション"
    };

    // デフォルトの質問文  未入力の場合使用
    private static readonly string[] DefaultQuestionDescriptions =
    {
        "この課題では、どの程度の精神的・知覚的活動が必要でしたか？",
        "この課題では、どの程度の身体的努力が必要でしたか？",
        "この課題では、どの程度の時間的・速度的なプレッシャーがありましたか？",
        "この課題では、あなたのパフォーマンスはどの程度でしたか？",
        "この課題では、どの程度の努力が必要でしたか？",
        "この課題では、どの程度のイライラや不満がありましたか？"
    };

    private void Awake()
    {
        // Inspector 必須参照を検証し、初期質問を設定し、UI を非表示にする
        ValidateInspectorReferences();
        InitializeQuestionsIfEmpty();
        HidePanel();
    }

    // それぞれのオブジェクトが割り当てられてなかった時用のエラー文　ここまで丁寧にやる必要あるか...？
    private void ValidateInspectorReferences()
    {
        if (nasaTlxPanel == null) Debug.LogError("NasaTlxManager: nasaTlxPanel is not assigned.");
        if (progressText == null) Debug.LogError("NasaTlxManager: progressText is not assigned.");
        if (questionTitle == null) Debug.LogError("NasaTlxManager: questionTitle is not assigned.");
        if (questionDescription == null) Debug.LogError("NasaTlxManager: questionDescription is not assigned.");
        if (lowLabel == null) Debug.LogError("NasaTlxManager: lowLabel is not assigned.");
        if (highLabel == null) Debug.LogError("NasaTlxManager: highLabel is not assigned.");
        if (answerSlider == null) Debug.LogError("NasaTlxManager: answerSlider is not assigned.");
        if (minusButton == null) Debug.LogError("NasaTlxManager: minusButton is not assigned.");
        if (plusButton == null) Debug.LogError("NasaTlxManager: plusButton is not assigned.");
        if (confirmButton == null) Debug.LogError("NasaTlxManager: confirmButton is not assigned.");
        if (requestSender == null) Debug.LogError("NasaTlxManager: requestSender is not assigned.");
    }

    // 質問文などが割り当てられてなかった時用関数
    private void InitializeQuestionsIfEmpty()
    {
        // 質問が Inspector で設定されていない場合はデフォルト質問を埋める
        if (questions == null || questions.Length != 6)
        {
            questions = new NasaTlxQuestion[6];
        }

        for (int i = 0; i < questions.Length; i++)
        {
            if (questions[i] != null) continue;
            questions[i] = CreateDefaultQuestion(i);
        }
    }

    // 質問文などのデフォルト値を代入する関数
    private static NasaTlxQuestion CreateDefaultQuestion(int index)
    {
        return new NasaTlxQuestion
        {
            id = DefaultQuestionIds[index],
            title = DefaultQuestionTitles[index],
            description = DefaultQuestionDescriptions[index],
            lowLabel = "低い",
            highLabel = "高い"
        };
    }

    // NASA-TLXパネル消すだけ
    private void HidePanel()
    {
        if (nasaTlxPanel != null)
        {
            nasaTlxPanel.SetActive(false);
        }
    }

    // NASA-TLXパネル出すだけ
    private void ShowPanel()
    {
        if (nasaTlxPanel != null)
        {
            nasaTlxPanel.SetActive(true);
        }
    }

    // NASA-TLXアンケート開始用関数
    // 外部（N-backなど）から呼び出す前提
    public void StartQuestionnaire(/*string userId, */string blockId)
    {
        // NASA-TLXの送信中だったら，警告
        if (isSending)
        {
            Debug.LogWarning("NasaTlxManager: questionnaire is already sending.");
            return;
        }

        // NASA-TLXの質問数が6以外になっていたら，エラー
        if (questions == null || questions.Length != 6)
        {
            Debug.LogError("NasaTlxManager: questions array must contain 6 items.");
            return;
        }

        // NASA-TLXの回答可能値の最大値が最小値を下回っていたら
        if (minValue >= maxValue)
        {
            Debug.LogError("NasaTlxManager: minValue must be less than maxValue. Falling back to 0-20.");
            minValue = 0;
            maxValue = 20;
        }

        //currentUserId = string.IsNullOrWhiteSpace(userId) ? "" : userId;
        currentBlockId = string.IsNullOrWhiteSpace(blockId) ? "" : blockId;
        currentQuestionIndex = 0;
        hasAnswered = false;
        answers.Clear();
        ShowPanel();
        SetupSlider();
        ShowQuestion(currentQuestionIndex);
    }

    // NASA-TLX解答用スライダーの初期化関数
    private void SetupSlider()
    {
        if (answerSlider == null) return;

        // 回答は整数のみで、Inspector から範囲を設定可能
        answerSlider.minValue = minValue;
        answerSlider.maxValue = maxValue;
        answerSlider.wholeNumbers = true;
        answerSlider.value = (minValue + maxValue) / 2f; //最初は中心に設定
    }

    // 質問の表示・変更の関数．index番目を出力
    private void ShowQuestion(int index)
    {
        if (index < 0 || index >= questions.Length)
        {
            Debug.LogError($"NasaTlxManager: invalid question index {index}.");
            return;
        }

        // 進捗表示を更新
        if (progressText != null)
        {
            progressText.text = $"{index + 1} / {questions.Length}";
        }

        // index番号に対応したタイトル・質問文・ラベルを表示
        var question = questions[index];
        if (questionTitle != null) questionTitle.text = question.title;
        if (questionDescription != null) questionDescription.text = question.description;
        if (lowLabel != null) lowLabel.text = question.lowLabel;
        if (highLabel != null) highLabel.text = question.highLabel;

        if (answerSlider != null)
        {
            // デフォルト初期値をセットした際のイベントを抑止．中心点にスライダーを移動させ，一時的に動かせなくする
            suppressSliderEvent = true;
            answerSlider.value = (minValue + maxValue) / 2f;
            suppressSliderEvent = false;
        }

        // 回答前状態に戻す
        hasAnswered = false;
        if (confirmButton != null) confirmButton.interactable = false;
        SetControlInteractable(true);
    }

    // スライダーが操作されたら回答可能にする関数
    public void OnSliderValueChanged(float value)
    {
        if (isSending || suppressSliderEvent) return;

        // ユーザーがスライダーを操作したら回答可能状態にする
        hasAnswered = true;
        if (confirmButton != null) confirmButton.interactable = true;
    }

    // +ボタンの動作の関数
    public void IncreaseValue()
    {
        if (isSending || answerSlider == null) return;
        answerSlider.value = Mathf.Clamp(answerSlider.value + 1f, minValue, maxValue);
        hasAnswered = true;
        if (confirmButton != null) confirmButton.interactable = true;
    }

    // -ボタンの動作の関数
    public void DecreaseValue()
    {
        if (isSending || answerSlider == null) return;
        answerSlider.value = Mathf.Clamp(answerSlider.value - 1f, minValue, maxValue);
        hasAnswered = true;
        if (confirmButton != null) confirmButton.interactable = true;
    }

    // 解答提出の関数
    public void ConfirmAnswer()
    {
        // 送信中だったら
        if (isSending)
        {
            Debug.LogWarning("NasaTlxManager: confirm ignored while sending.");
            return;
        }

        // 解答が入力されていなかったら
        if (!hasAnswered)
        {
            Debug.LogWarning("NasaTlxManager: answer not set yet.");
            return;
        }

        // スライダーが割り当てられていなかったら
        if (answerSlider == null)
        {
            Debug.LogError("NasaTlxManager: answerSlider is not assigned.");
            return;
        }

        // 現在のスライダー値を整数として回答に保存
        int answer = Mathf.Clamp(Mathf.RoundToInt(answerSlider.value), minValue, maxValue);
        var question = questions[currentQuestionIndex];
        answers[question.id] = answer;

        // 6問目まで繰り返し，6問目まで終わったら完了
        currentQuestionIndex++;
        if (currentQuestionIndex < questions.Length)
        {
            ShowQuestion(currentQuestionIndex);
        }
        else
        {
            CompleteQuestionnaire();
        }
    }

    // 6問とも解答完了時の関数
    private void CompleteQuestionnaire()
    {
        // 6項目すべての回答を収集したことを確認
        if (answers.Count != questions.Length)
        {
            Debug.LogError("NasaTlxManager: questionnaire completed before all answers were collected.");
            return;
        }

        if (requestSender == null)
        {
            Debug.LogError("NasaTlxManager: requestSender is not assigned.");
            return;
        }

        // 送信中は UI を無効化して二重送信を防ぐ
        isSending = true;
        SetControlInteractable(false);

        string sendUserId = string.IsNullOrWhiteSpace(currentUserId) ? requestSender.userId : currentUserId;
        string blockId = currentBlockId;

        // 送信
        requestSender.SendNASATLX(
            sendUserId,
            blockId,
            answers["mental_demand"],
            answers["physical_demand"],
            answers["temporal_demand"],
            answers["performance"],
            answers["effort"],
            answers["frustration"],
            "raw_tlx",
            OnNASATLXSendComplete
        );
    }

    private void OnNASATLXSendComplete(bool ok)
    {
        isSending = false;

        if (!ok)
        {
            Debug.LogWarning("NasaTlxManager: NASA-TLX送信に失敗しました。再度送信できます。");
            // 送信失敗時は UI を維持して再送可能にする
            SetControlInteractable(true);
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        // 送信成功時はアンケート画面を閉じ、ステータス通知を送る
        HidePanel();
        if (requestSender != null)
        {
            StartCoroutine(requestSender.PostStatusFlag("nasa_tlx_complete"));
        }
    }

    private void SetControlInteractable(bool interactable)
    {
        // 回答中 / 送信中の UI 操作制御
        if (answerSlider != null) answerSlider.interactable = interactable;
        if (minusButton != null) minusButton.interactable = interactable;
        if (plusButton != null) plusButton.interactable = interactable;
        if (confirmButton != null) confirmButton.interactable = interactable && hasAnswered;
    }
}
