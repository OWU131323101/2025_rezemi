using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class ResultManager : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI resultTitleText;   // "起訴成功！" or "不起訴..."
    public TextMeshProUGUI commentText;       // 上司/AIからのコメント
    public GameObject successPanel;           // 成功時演出用（任意）
    public GameObject failPanel;              // 失敗時演出用（任意）

    void Start()
    {
        ShowResult();
    }

    void ShowResult()
    {
        // GameConfigから結果を取得
        bool isClear = GameConfig.IsClear;
        string comment = GameConfig.ResultComment;

        // タイトル表示
        if (resultTitleText != null)
        {
            if (isClear)
            {
                resultTitleText.text = "起訴成功！ (MISSION COMPLETE)";
                resultTitleText.color = Color.cyan;
            }
            else
            {
                resultTitleText.text = "不起訴処分... (GAME OVER)";
                resultTitleText.color = Color.red;
            }
        }

        // コメント表示
        if (commentText != null)
        {
            commentText.text = comment;
        }

        // パネル切り替え（もし設定されていれば）
        if (successPanel != null) successPanel.SetActive(isClear);
        if (failPanel != null) failPanel.SetActive(!isClear);
    }

    // Welcomeシーンに戻る
    public void OnClickBackToWelcome()
    {
        SceneManager.LoadScene("WelcomeScene");
    }

    // タイトル（難易度選択）に戻る
    public void OnClickRetry()
    {
        SceneManager.LoadScene("TitleScene");
    }
}
