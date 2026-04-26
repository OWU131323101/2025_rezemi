using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    // ボタンに割り当てる関数
    public void OnClickLevel1() { StartGame(1); }
    public void OnClickLevel2() { StartGame(2); }
    public void OnClickLevel3() { StartGame(3); }

    void StartGame(int level)
    {
        // 難易度を保存
        GameConfig.SelectedDifficulty = level;
        // メイン画面へ移動（シーン名はご自身の環境に合わせてください）
        SceneManager.LoadScene("InterrogationRoom"); 
    }

    public void OnClickBackToWelcome()
    {
        SceneManager.LoadScene("WelcomeScene");
    }
}
