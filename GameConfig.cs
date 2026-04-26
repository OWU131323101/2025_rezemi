using UnityEngine;

// どのシーンからでもアクセスできる「設定保存場所」
public static class GameConfig
{
    // 1=Easy, 2=Normal, 3=Hard
    public static int SelectedDifficulty = 1; 

    // --- ★追加：リザルト画面へのデータ受け渡し用 ---
    public static bool IsClear = false;      // 起訴できたか
    public static string ResultComment = ""; // 上司/AIからのコメント
}
