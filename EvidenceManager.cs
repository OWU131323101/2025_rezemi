using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; // Button用
using TMPro;

public class EvidenceManager : MonoBehaviour
{
    [Header("Chat Log Settings")]
    public GameObject logPanel;
    public Transform logContentParent;
    public GameObject logItemPrefab; 

    [Header("Evidence List Settings")]
    public GameObject evidenceListPanel; // 証拠リスト表示用パネル
    public Transform evidenceListContentParent; // 証拠リストのContent

    [Header("Confirmation Dialog")]
    public GameObject confirmationPanel; // 確認ダイアログ全体
    public TextMeshProUGUI confirmationText; // 修正：TMPに変更
    public Button confirmYesButton;
    public Button confirmNoButton;

    // 提出用の証言リスト
    public List<string> selectedEvidence = new List<string>();

    private string pendingEvidenceText; // 確認中のテキスト

    void Start()
    {
        // 確認ダイアログのボタンにイベント登録
        if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnConfirmYes);
        if (confirmNoButton != null) confirmNoButton.onClick.AddListener(OnConfirmNo);
        
        // 初期状態ではダイアログとリストは非表示
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
        if (evidenceListPanel != null) evidenceListPanel.SetActive(false);

        // ★修正：ここでの強制レイアウト設定を削除しました。
        // 親オブジェクト（ScrollViewのContent）のInspector設定を使用してください。
        // 推奨設定（Contentオブジェクト）:
        // - Vertical Layout Group: 
        //     Control Child Size: Width=TRUE, Height=TRUE
        //     Child Force Expand: Width=TRUE, Height=FALSE (★重要: Height=TRUEだと無限ループしてフリーズする可能性あり)
        // - Content Size Fitter:
        //     Vertical Fit: Preferred Size
    }

    // チャットログ（全履歴）の表示切り替え
    public void ToggleLogPanel()
    {
        if(logPanel != null)
            logPanel.SetActive(!logPanel.activeSelf);
    }

    // 証拠リスト（保存済み）の表示切り替え
    public void ToggleEvidenceListPanel()
    {
        if(evidenceListPanel != null)
            evidenceListPanel.SetActive(!evidenceListPanel.activeSelf);
    }

    // 会話が発生したらこれを呼ぶ（チャットログに追加）
    public void AddLog(string text)
    {
        if(logContentParent == null || logItemPrefab == null) return;

        // ★追加：最大ログ数の制限（50件）
        if (logContentParent.childCount >= 50)
        {
            Destroy(logContentParent.GetChild(0).gameObject); // 一番古いものを削除
        }

        GameObject obj = Instantiate(logItemPrefab, logContentParent);
        LogItem item = obj.GetComponent<LogItem>();
        if(item != null)
        {
            item.Setup(text, this);
            
            // ボタンイベントを動的に登録
            var btn = obj.GetComponent<UnityEngine.UI.Button>();
            if(btn != null)
                btn.onClick.AddListener(item.OnClick);
        }
    }

    // LogItemから呼ばれる：確認ダイアログを表示
    public void ShowConfirmation(string text)
    {
        // 既に保存済みなら何もしない（あるいは削除確認にする）
        if (selectedEvidence.Contains(text)) return; 

        pendingEvidenceText = text;
        if (confirmationPanel != null) confirmationPanel.SetActive(true);
    }

    void OnConfirmYes()
    {
        if (!string.IsNullOrEmpty(pendingEvidenceText))
        {
            AddEvidence(pendingEvidenceText);
        }
        CloseConfirmation();
    }

    void OnConfirmNo()
    {
        CloseConfirmation();
    }

    void CloseConfirmation()
    {
        pendingEvidenceText = "";
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
    }

    public void AddEvidence(string text)
    {
        if (!selectedEvidence.Contains(text))
        {
            selectedEvidence.Add(text);
            Debug.Log("証拠追加: " + text);

            // 証拠リストUIにも追加表示
            AddToEvidenceListUI(text);
        }
    }

    // UIの証拠リストに追加
    void AddToEvidenceListUI(string text)
    {
        if (evidenceListContentParent == null || logItemPrefab == null) return;

        // 見た目はLogItemと同じPrefabを使い回す（クリック機能はオフにするか、削除機能にする）
        GameObject obj = Instantiate(logItemPrefab, evidenceListContentParent);
        
        // LogItemコンポーネントがあれば設定
        LogItem item = obj.GetComponent<LogItem>();
        if (item != null)
        {
            item.Setup(text, this);
            // 証拠リスト用なのでクリックしても確認ダイアログが出ないようにする等はLogItem側で制御するか、
            // ここでButtonコンポーネントを無効化してもよい
            var btn = obj.GetComponent<UnityEngine.UI.Button>();
            if(btn != null) btn.interactable = false; // 一旦クリック不可にする例
        }
    }

    public void RemoveEvidence(string text)
    {
        if (selectedEvidence.Contains(text))
        {
            selectedEvidence.Remove(text);
            Debug.Log("証拠解除: " + text);
            // 証拠リストUIからの削除はリスト再描画が必要だが、今回は追加のみ実装
        }
    }
}
