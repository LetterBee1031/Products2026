using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// TMP_InputFieldの選択イベントとXRNumericKeyboardを接続する共通コンポーネント。
/// N-backやStroopなど、複数の課題から同じ数値キーボードを再利用できる。
/// </summary>
public class XRNumericKeyboardInputBinder : MonoBehaviour
{
    [Header("Numeric keyboard")]
    [SerializeField] private XRNumericKeyboard numericKeyboard;

    private readonly Dictionary<TMP_InputField, UnityAction<string>> selectListeners =
        new Dictionary<TMP_InputField, UnityAction<string>>();

    /// <summary>
    /// 整数入力欄をキーボードへ接続する。
    /// </summary>
    public void BindInteger(TMP_InputField inputField, Action<string> onApply)
    {
        Bind(inputField, onApply, false);
    }

    /// <summary>
    /// 小数入力欄をキーボードへ接続する。
    /// </summary>
    public void BindDecimal(TMP_InputField inputField, Action<string> onApply)
    {
        Bind(inputField, onApply, true);
    }

    /// <summary>
    /// 指定した入力欄とキーボードの接続を解除する。
    /// </summary>
    public void Unbind(TMP_InputField inputField)
    {
        if (inputField == null ||
            !selectListeners.TryGetValue(inputField, out UnityAction<string> listener))
        {
            return;
        }

        inputField.onSelect.RemoveListener(listener);
        selectListeners.Remove(inputField);
    }

    private void Bind(TMP_InputField inputField, Action<string> onApply, bool allowDecimalPoint)
    {
        if (inputField == null)
        {
            return;
        }

        EnsureKeyboard();
        if (numericKeyboard == null)
        {
            Debug.LogWarning("XRNumericKeyboardInputBinder: XRNumericKeyboard could not be created.");
            return;
        }

        // 同じ入力欄が再登録された場合は、古いリスナーを先に外す。
        Unbind(inputField);

        UnityAction<string> listener = _ =>
            numericKeyboard.Open(inputField, onApply, allowDecimalPoint);

        inputField.onSelect.AddListener(listener);
        selectListeners[inputField] = listener;
    }

    private void EnsureKeyboard()
    {
        if (numericKeyboard != null)
        {
            return;
        }

        numericKeyboard = GetComponent<XRNumericKeyboard>();
        if (numericKeyboard == null)
        {
            numericKeyboard = gameObject.AddComponent<XRNumericKeyboard>();
        }
    }

    private void OnDisable()
    {
        // 課題画面が非表示になったとき、キーボードだけが残らないようにする。
        if (numericKeyboard != null)
        {
            numericKeyboard.Hide();
        }
    }

    private void OnDestroy()
    {
        foreach (KeyValuePair<TMP_InputField, UnityAction<string>> binding in selectListeners)
        {
            if (binding.Key != null)
            {
                binding.Key.onSelect.RemoveListener(binding.Value);
            }
        }

        selectListeners.Clear();
    }
}
