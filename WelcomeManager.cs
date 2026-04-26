using UnityEngine;
using UnityEngine.SceneManagement;

public class WelcomeManager : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject tutorialPanel; // チュートリアル表示用パネル

    // ゲーム開始ボタン（難易度選択へ）
    public void OnClickStart()
    {
        // TitleSceneへ移動
        SceneManager.LoadScene("TitleScene");
    }

    // チュートリアルボタン（表示切り替え）
    public void OnClickTutorial()
    {
        if (tutorialPanel != null)
        {
            // アクティブ状態を反転（オンならオフ、オフならオン）
            bool isActive = tutorialPanel.activeSelf;
            tutorialPanel.SetActive(!isActive);
        }
    }
}
