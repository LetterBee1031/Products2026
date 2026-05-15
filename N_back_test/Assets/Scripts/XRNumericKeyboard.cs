using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class XRNumericKeyboard : MonoBehaviour
{
    [Header("Keyboard UI")]
    public GameObject keyboardRoot; // 表示/非表示を切り替えるキーボード全体
    public TextMeshProUGUI previewText; // 入力中の文字列を表示する欄
    public bool autoBuildKeyboard = true; // 未設定なら実行時に簡易キーボードを作る

    [Header("Placement")]
    public Transform keyboardAnchor; // キーボードを出す基準位置。未設定ならMain Cameraの前に出す
    public Vector3 localPosition = new Vector3(0f, -0.25f, 1.2f); // anchorから見た表示位置
    public Vector2 canvasSize = new Vector2(400f, 400f); // 自動生成するCanvasの大きさ
    public float canvasScale = 0.0018f; // ワールド空間での表示スケール

    TMP_InputField targetInputField; // 現在入力対象になっているInputField
    Action<string> applyCallback; // OKを押したとき、入力値を呼び出し元へ渡す処理
    bool allowDecimalPoint; // 小数点キーを許可するか

    void Awake()
    {
        // InspectorでキーボードRootを割り当てない場合でも、最低限使えるUIを作る
        if (keyboardRoot == null && autoBuildKeyboard)
        {
            BuildKeyboard();
        }

        Hide();
    }

    public void OpenForInteger(TMP_InputField inputField, Action<string> onApply)
    {
        // n_back_numなど、整数だけ入力したい欄用
        Open(inputField, onApply, false);
    }

    public void OpenForDecimal(TMP_InputField inputField, Action<string> onApply)
    {
        // timeLimitなど、小数入力も許可したい欄用
        Open(inputField, onApply, true);
    }

    public void Open(TMP_InputField inputField, Action<string> onApply, bool decimalPoint)
    {
        // 入力対象と、OK時に実行する反映処理を保持してから表示する
        targetInputField = inputField;
        applyCallback = onApply;
        allowDecimalPoint = decimalPoint;

        if (targetInputField != null)
        {
            SetPreviewText(targetInputField.text);
            targetInputField.ActivateInputField();
        }

        Show();
    }

    public void InputKey(string key)
    {
        // 数字キーや小数点キーを入力対象のInputFieldへ追記する
        if (targetInputField == null || string.IsNullOrEmpty(key))
        {
            return;
        }

        if (key == "." && (!allowDecimalPoint || targetInputField.text.Contains(".")))
        {
            return;
        }

        targetInputField.text += key;
        SetPreviewText(targetInputField.text);
    }

    public void Backspace()
    {
        // 末尾1文字を削除する
        if (targetInputField == null || string.IsNullOrEmpty(targetInputField.text))
        {
            return;
        }

        targetInputField.text = targetInputField.text.Substring(0, targetInputField.text.Length - 1);
        SetPreviewText(targetInputField.text);
    }

    public void Clear()
    {
        // 入力中の文字列をすべて消す
        if (targetInputField == null)
        {
            return;
        }

        targetInputField.text = string.Empty;
        SetPreviewText(string.Empty);
    }

    public void Apply()
    {
        // OKボタン。呼び出し元へ現在の文字列を渡してから閉じる
        if (targetInputField != null)
        {
            applyCallback?.Invoke(targetInputField.text);
            targetInputField.DeactivateInputField();
        }

        Hide();
    }

    public void Cancel()
    {
        // 反映せずに閉じる
        if (targetInputField != null)
        {
            targetInputField.DeactivateInputField();
        }

        Hide();
    }

    public void Show()
    {
        if (keyboardRoot != null)
        {
            keyboardRoot.SetActive(true);
        }
    }

    public void Hide()
    {
        if (keyboardRoot != null)
        {
            keyboardRoot.SetActive(false);
        }
    }

    void SetPreviewText(string text)
    {
        // キーボード上部のプレビュー表示を更新する
        if (previewText != null)
        {
            previewText.text = text;
        }
    }

    void BuildKeyboard()
    {
        // 既存Prefabを用意しなくても動くよう、ワールド空間Canvasとボタン群を実行時に作る
        Transform anchor = keyboardAnchor != null ? keyboardAnchor : Camera.main != null ? Camera.main.transform : transform;

        GameObject canvasObject = new GameObject("XR Numeric Keyboard");
        canvasObject.transform.SetParent(anchor, false);
        canvasObject.transform.localPosition = localPosition;
        canvasObject.transform.localRotation = Quaternion.identity;
        canvasObject.transform.localScale = Vector3.one * canvasScale;

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        // XR Ray Interactorでボタンを押せるようにする
        canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = canvasSize;

        Image background = canvasObject.AddComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.1f, 0.95f);
        background.raycastTarget = false;
        // RectTransform backgroundRect = background.GetComponent<RectTransform>();
        // backgroundRect = new Vector2(400f,)

        keyboardRoot = canvasObject;

        previewText = CreateLabel(canvasObject.transform, "Preview", new Vector2(0f, 115f), new Vector2(320f, 44f), "0", 30);
        previewText.alignment = TextAlignmentOptions.MidlineRight;
        previewText.raycastTarget = false;

        string[] keys =
        {
            "7", "8", "9",
            "4", "5", "6",
            "1", "2", "3",
            ".", "0", "<"
        };

        for (int i = 0; i < keys.Length; i++)
        {
            // 3列 x 4行で数字キーを並べる
            int row = i / 3;
            int col = i % 3;
            string key = keys[i];
            Button button = CreateButton(canvasObject.transform, key, new Vector2(-90f + col * 90f, 55f - row * 52f), new Vector2(72f, 42f));

            if (key == "<")
            {
                button.onClick.AddListener(Backspace);
            }
            else
            {
                button.onClick.AddListener(() => InputKey(key));
            }
        }

        Button clearButton = CreateButton(canvasObject.transform, "Clear", new Vector2(-90f, -165f), new Vector2(72f, 42f));
        clearButton.onClick.AddListener(Clear);

        Button okButton = CreateButton(canvasObject.transform, "OK", new Vector2(0f, -165f), new Vector2(72f, 42f));
        okButton.onClick.AddListener(Apply);

        Button cancelButton = CreateButton(canvasObject.transform, "Cancel", new Vector2(90f, -165f), new Vector2(72f, 42f));
        cancelButton.onClick.AddListener(Cancel);
    }

    Button CreateButton(Transform parent, string text, Vector2 position, Vector2 size)
    {
        // 自動生成キーボード用のボタンを1つ作る
        GameObject buttonObject = new GameObject("Key " + text);
        buttonObject.transform.SetParent(parent, false);

        RectTransform rectTransform = buttonObject.AddComponent<RectTransform>();
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = size;

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.95f, 0.95f, 0.95f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        TextMeshProUGUI label = CreateLabel(buttonObject.transform, "Label", Vector2.zero, size, text, 20);
        label.color = new Color(0.08f, 0.09f, 0.1f, 1f);
        label.raycastTarget = false;

        return button;
    }

    TextMeshProUGUI CreateLabel(Transform parent, string name, Vector2 position, Vector2 size, string text, float fontSize)
    {
        // ボタンラベルやプレビューに使うTextMeshProUGUIを作る
        GameObject labelObject = new GameObject(name);
        labelObject.transform.SetParent(parent, false);

        RectTransform rectTransform = labelObject.AddComponent<RectTransform>();
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = size;

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        return label;
    }
}
