using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LogItem : MonoBehaviour
{
    public TextMeshProUGUI messageText;
    public Image background;
    
    private string fullText;
    private EvidenceManager manager;

    public void Setup(string text, EvidenceManager mgr)
    {
        fullText = text;
        messageText.text = text;
        manager = mgr;

        // ★修正：コードによる強制設定を削除しました。
        // これらはPrefabのInspectorで設定してください。
        // 推奨設定：
        // 1. TextMeshProUGUI: Wrapping=Enabled, Overflow=Truncate or Overflow
        // 2. LogItem(Root): 
        //    - ContentSizeFitter: VerticalFit=PreferredSize
        //    - LayoutElement: FlexibleHeight=0 (勝手に広がらないようにする)
    }

    // ボタンのOnClickに割り当てる
    public void OnClick()
    {
        // 変更：直接選択するのではなく、確認パネルを表示する
        if (manager != null)
        {
            manager.ShowConfirmation(fullText);
        }
    }
}
