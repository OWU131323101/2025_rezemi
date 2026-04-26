using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Google.GenAI;
using Google.GenAI.Types;
using UnityEngine.Networking; // UnityWebRequestに必要

[RequireComponent(typeof(AudioSource))] // 音を出すパーツを自動追加
public class MysteryGM : MonoBehaviour
{
    [SerializeField] private string apiKey = "ここにAPIキーを貼り付ける";

    // VOICEVOX API URL (ローカル)
    private string voicevoxUrl = "http://127.0.0.1:50021";

    // キャラクターの設計図
    [System.Serializable]
    public class CharacterProfile
    {
        public string jobName;      // 役職名
        public string personality;  // 性格
        public string firstPerson;  // 一人称
        public int voiceActorId;    // 声優ID（VOICEVOX用）
        [HideInInspector] public bool isCulprit; // 犯人フラグ
    }

    // 5人のキャラクターリスト
    public List<CharacterProfile> characters = new List<CharacterProfile>();
    
    private AudioSource audioSource; // 音声再生用

    async void Start()
    {
        audioSource = GetComponent<AudioSource>();

        // APIキーの設定
        if (!string.IsNullOrEmpty(apiKey))
        {
            System.Environment.SetEnvironmentVariable("GOOGLE_API_KEY", apiKey);
        }

        // 1. キャラクターの初期データを登録
        characters.Clear();
        SetupCharacters();

        // 2. 犯人をランダムに決定
        AssignRoles();

        // 3. 全員に「アリバイ」を聞いてみる
        await InterviewEveryone();
    }

    void SetupCharacters()
    {
        // voiceActorId: 3=ずんだもん, 2=四国めたん, 4=みこ, 8=春日部つむぎ, 13=青山龍星(男性)
        characters.Add(new CharacterProfile { jobName = "車掌", voiceActorId = 13, firstPerson = "私", personality = "真面目だが神経質。丁寧な言葉遣い。" });
        characters.Add(new CharacterProfile { jobName = "貴族", voiceActorId = 2, firstPerson = "わたくし", personality = "高慢で他人を見下している。お嬢様口調。" });
        characters.Add(new CharacterProfile { jobName = "技師", voiceActorId = 3, firstPerson = "僕", personality = "機械オタク。おどおどしている。専門用語が多い。" });
        characters.Add(new CharacterProfile { jobName = "軍人", voiceActorId = 53, firstPerson = "自分", personality = "威圧的で声が大きい。規律を重んじる。" }); // 53=冥鳴ひまり(低音)
        characters.Add(new CharacterProfile { jobName = "歌姫", voiceActorId = 14, firstPerson = "あたし", personality = "華やかで感情的。気まぐれな口調。" });
    }

    void AssignRoles()
    {
        // 全員一度「シロ」にする
        foreach (var c in characters) c.isCulprit = false;

        // ランダムに1人選んで「クロ（犯人）」にする
        int randomIndex = Random.Range(0, characters.Count);
        characters[randomIndex].isCulprit = true;

        Debug.Log($"<color=red>【システム通知】今回の犯人は「{characters[randomIndex].jobName}」です！</color>");
    }

    async Task InterviewEveryone()
    {
        foreach (var chara in characters)
        {
            string systemPrompt = GeneratePrompt(chara);
            string playerQuestion = "事件のあった21:00頃、どこで何をしていましたか？";
            string fullPrompt = $"{systemPrompt}\n\n【質問】{playerQuestion}\n回答は50文字以内で簡潔にお願いします。"; // 長すぎると生成が遅いので制限

            await SendToGemini(chara, fullPrompt);
            
            // 次の人へ行く前に少し待つ
            await Task.Delay(1000); 
        }
    }

    // AIへの指示書を作る関数
    string GeneratePrompt(CharacterProfile chara)
    {
        string prompt = $"あなたは銀河鉄道の乗客である「{chara.jobName}」役です。\n";
        prompt += $"性格設定: {chara.personality}\n";
        prompt += $"一人称: {chara.firstPerson}\n";
        
        if (chara.isCulprit)
        {
            prompt += "【重要】あなたは犯人です。21:00に被害者を殺害しました。しかし、絶対に自白せず、「自室で寝ていた」等の嘘のアリバイを主張してください。\n";
        }
        else
        {
            prompt += "あなたは無実です。21:00は食堂にいました。見たままの事実を話してください。\n";
        }

        return prompt;
    }

    // --- Gemini通信部分 (Google.GenAI SDK使用) ---
    async Task SendToGemini(CharacterProfile chara, string prompt)
    {
        Debug.Log($"[{chara.jobName}] ({chara.voiceActorId}) に質問中...");

        try
        {
            var client = new Client();
            var response = await client.Models.GenerateContentAsync(
                model: "gemini-2.5-flash", 
                contents: prompt
            );

            if (response.Candidates != null && response.Candidates.Count > 0)
            {
                string replyText = response.Candidates[0].Content.Parts[0].Text;
                // 改行などが含まれると読み上げがおかしくなることがあるので整形しても良いが、今回はそのまま
                Debug.Log($"<color=cyan>【{chara.jobName}】</color>: {replyText}");

                // VOICEVOXで喋らせる
                await SpeakWithVoiceVox(replyText, chara.voiceActorId);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Gemini通信エラー ({chara.jobName}): " + e.Message);
        }
    }

    // --- VOICEVOX通信 (async/await版) ---
    async Task SpeakWithVoiceVox(string text, int speakerId)
    {
        // A. 音声合成用のクエリを作成 (AudioQuery)
        string queryUrl = $"{voicevoxUrl}/audio_query?text={UnityWebRequest.EscapeURL(text)}&speaker={speakerId}";
        string queryJson = "";

        using (UnityWebRequest request = new UnityWebRequest(queryUrl, "POST"))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.uploadHandler = new UploadHandlerRaw(new byte[0]); // 空のBody

            // UnityWebRequestをawaitableにするための簡易的な待機ループ
            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success) 
            { 
                Debug.LogError("VoiceVox Query Error: " + request.error); 
                return; 
            }
            queryJson = request.downloadHandler.text;
        }

        // B. 音声データを生成 (Synthesis)
        string synthesisUrl = $"{voicevoxUrl}/synthesis?speaker={speakerId}";
        
        using (UnityWebRequest request = new UnityWebRequest(synthesisUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(queryJson);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerAudioClip(synthesisUrl, AudioType.WAV);
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success) 
            { 
                Debug.LogError("VoiceVox Synthesis Error: " + request.error); 
                return; 
            }

            // C. 再生
            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (audioSource != null && clip != null)
            {
                audioSource.clip = clip;
                audioSource.Play();
                
                // 再生が終わるまで待つ（クリップの長さ秒数待機）
                await Task.Delay((int)(clip.length * 1000));
            }
        }
    }
}