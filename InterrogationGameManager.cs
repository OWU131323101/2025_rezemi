using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement; // ★追加
using TMPro;

public class InterrogationGameManager : MonoBehaviour
{
    [Header("参照")]
    public OpenAIManager openAI;
    public EvidenceManager evidenceManager;
    public LidarDistanceSensor lidarSensor; // ★変更：新スクリプト参照
    
    public Transform playerProxy;
    public Transform suspectCapsule;
    
    [System.Serializable]
    public struct CharacterProfile
    {
        public GameObject prefab;
        public AnimationClip idleAnimation; // キャラクター固有の待機モーション
        public Vector3 spawnOffset;         // 位置の微調整
        public Vector3 rotationOffset;      // 回転の微調整
    }

    [Header("キャラクター設定")]
    public CharacterProfile[] characterProfiles; // Prefab配列の代わりにこちらを使用
    
    public RuntimeAnimatorController commonAnimatorController; // Nodアニメーション含む
    private Animator currentAnimator;

    public TMP_InputField inputField;
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI chargeText;

    private List<OpenAIManager.Message> chatHistory = new List<OpenAIManager.Message>();
    private int currentLevel;

    // --- ★追加：距離判定用の変数 ---
    [Header("距離自動反応設定")]
    public float closeThreshold = 0.45f; // 詰め寄り判定ライン (m)
    public float farThreshold = 1.2f;    // 遠のき（放置）判定ライン (m)
    
    private enum DistanceState { Normal, Close, Far, Side, Wandering }
    private DistanceState currentState = DistanceState.Normal;
    private bool isAIThinking = false; // 連投防止用フラグ
    private float startupTimer = 0f;   // 開始直後の誤検知防止用タイマー
    private float stateTimer = 0f;     
    
    // ★追加：イベント制御フラグ
    private bool hasWanderingTriggered = false; // 徘徊は1回のみ
    private float apiCooldownTimer = 0f;       // API連投防止用クールダウン
    private const float API_COOLDOWN_TIME = 10.0f; // 10秒は間隔をあける
    private int intimidationCount = 0;         // ★追加：威圧回数カウンタ

    void Start()
    {
        // ★追加：参照の自動取得（アタッチ忘れ対策）
        if (openAI == null) openAI = FindObjectOfType<OpenAIManager>();
        if (evidenceManager == null) evidenceManager = FindObjectOfType<EvidenceManager>();
        if (lidarSensor == null) lidarSensor = FindObjectOfType<LidarDistanceSensor>();

        // タイトル画面で選んだ難易度を取得
        currentLevel = GameConfig.SelectedDifficulty;

        // 難易度ごとの初期設定（容疑の表示など）
        SetupScenario();
        
        if(dialogueText != null)
            dialogueText.text = "（証拠を集めて上司に報告しよう。取調べを開始してください。）";
            
        // 初期状態は「普通」に強制リセット
        currentState = DistanceState.Normal;
    }

    void SetupScenario()
    {
        string chargeContent = "";
        switch (currentLevel)
        {
            case 1:
                chargeContent = "【容疑：窃盗（万引き）】\nコンビニで高級ボールペン複数本を万引きした疑い。";
                break;
            case 2:
                chargeContent = "【容疑：殺人未遂・ストーカー行為】\nアイドルの自宅への侵入、および刃物の送付。ネットでの誹謗中傷。";
                break;
            case 3:
                chargeContent = "【容疑：通り魔殺人】\n深夜の公園における無差別刺殺事件。";
                break;
        }
        // 画面に常時表示
        if(chargeText != null)
            chargeText.text = chargeContent;

        // --- ★追加：キャラクター生成 (Profile対応版) ---
        if (characterProfiles != null && characterProfiles.Length >= 3)
        {
            // 既存の表示を隠す 
            if (suspectCapsule != null)
            {
                var mesh = suspectCapsule.GetComponent<MeshRenderer>();
                if(mesh != null) mesh.enabled = false;
            }

            // 難易度に対応するインデックス (Lv1 -> 0)
            int index = currentLevel - 1;
            if (index >= 0 && index < characterProfiles.Length)
            {
                var profile = characterProfiles[index];
                if (profile.prefab != null)
                {
                    // オフセットを加味して生成
                    Vector3 spawnPos = suspectCapsule.position + profile.spawnOffset;
                    Quaternion spawnRot = suspectCapsule.rotation * Quaternion.Euler(profile.rotationOffset);

                    GameObject charObj = Instantiate(profile.prefab, spawnPos, spawnRot);
                    
                        // Animator設定とOverride
                        currentAnimator = charObj.GetComponent<Animator>();
                        if (currentAnimator != null && commonAnimatorController != null)
                        {
                            // 座標ズレ防止のためRootMotionを無効化
                            currentAnimator.applyRootMotion = false;

                            // オーバーライド用コントローラー作成
                            var overrideController = new AnimatorOverrideController(commonAnimatorController);
                        
                        // IDLEアニメーションの上書き
                        // 前提：Common Controllerの中に「Idle」という名前のモーション（または遷移元のDefault Stateのモーション）があること
                        // ここでは、コントローラーに割り当てられている元のアニメーションクリップをキーにして上書きします。
                        // もし共通コントローラーのIdleステートに「HumanoidIdle」などが設定されていれば、それを上書きします。
                        // ★重要：確実に上書きするために、共通コントローラーのIdleステートに紐づいているClip名を知る必要があるが、
                        // 簡易的にオーバーライドリストを走査して最初に見つかったClip（恐らくIdle）を書き換えるか、
                        // またはインスペクターで「Base Idle Clip」を指定してもらうのが確実です。
                        // 今回は「CommonControllerのデフォルトステートのクリップ」を想定して処理します。
                        
                        if(profile.idleAnimation != null)
                        {
                            // 全てのクリップを取得し、名前が"Idle"を含むもの、あるいはデフォルトを置き換える
                            // ここでは、オーバーライド可能なスロット全てに対して、もし何も設定されていなければIdleとみなす...等は危険なので、
                            // インデックスベースではなく、名前ベースで検索します。
                            // commonAnimatorControllerで設定した「Idle」ステートのClipが何か分かれば一番良いのですが、
                            // 汎用性を高めるため、単純にオーバーライドコントローラーのインデクサを使います。
                            // ただし、キーとなる「元のClip」が必要です。
                            // 修正案：CommonControllerにはダミーのIdleを入れておき、それをProfileのIdleで上書きします。
                            
                            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                            overrideController.GetOverrides(overrides);
                            
                            for (int i = 0; i < overrides.Count; ++i)
                            {
                                // "Idle" という名前が含まれるクリップ、または最初のクリップをターゲットにする
                                if (overrides[i].Key.name.Contains("Idle") || overrides[i].Key.name.Contains("Wait"))
                                {
                                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, profile.idleAnimation);
                                }
                            }
                            overrideController.ApplyOverrides(overrides);
                        }

                        currentAnimator.runtimeAnimatorController = overrideController;
                    }
                }
            }
        }
    }

    void Update()
    {
        // ★追加：クールダウンの更新
        if(apiCooldownTimer > 0f) apiCooldownTimer -= Time.deltaTime;

        // ★追加：開始から2秒間はセンサー数値を無視する（初期化中の誤動作防止）
        startupTimer += Time.deltaTime;
        if (startupTimer < 2.0f) return;

        // AIが考え中なら割り込まない
        if (isAIThinking) return;

        CheckDistanceReaction();
    }

    // ★追加：距離に応じた自動反応ロジック
    void CheckDistanceReaction()
    {
        // 修正：新スクリプト参照
        // 修正：新スクリプト参照
        // センサーが未接続(false)の場合は、誤動作防止のため判定を行わない
        if(lidarSensor == null || !lidarSensor.isConnected) return;

        float dist = lidarSensor.currentDistance;
        float angle = lidarSensor.currentAngle;
        float speed = lidarSensor.approachSpeed;
        bool wandering = lidarSensor.isWandering;

        // 境界値の設定（Inspectorの設定値を使用するため、ローカル変数は削除）
        // float closeThreshold = 0.4f; 
        // float farThreshold = 1.0f;

        // float sideAngleThreshold = 60.0f; // 削除：横反応は廃止

        // --- 0. 特殊イベント判定 (優先度高) ---
        
        // 机ドン (Desk Slam) - 急接近
        // 状態遷移ではなく単発イベントとして扱う
        if (speed > 1.5f && dist < 0.8f && !isAIThinking)
        {
             // クーリングタイム的なチェックが必要だが、簡易的に
             Debug.Log($"イベント発生: Desk Slam! Speed={speed:F2}");
             if(CanTriggerReaction()) 
                StartCoroutine(TriggerAutoReaction("DeskSlam"));
             return;  
        }

        // --- 状態遷移チェック (チャタリング防止) ---
        
        // 1. 徘徊 (Wandering)
        if (currentState != DistanceState.Wandering && wandering)
        {
             stateTimer += Time.deltaTime;
             if(stateTimer > 2.0f && !hasWanderingTriggered) // 2秒持続かつ初回のみ
             {
                 currentState = DistanceState.Wandering;
                 Debug.Log("状態遷移: Wandering (徘徊 - 初回のみ)");
                 hasWanderingTriggered = true; // フラグON
                 stateTimer = 0f;
                 
                 if(CanTriggerReaction())
                     StartCoroutine(TriggerAutoReaction("Wandering"));
             }
             return;
        }

        // 削除：横・背後 (Side) の判定ブロック
        /*
        if (currentState != DistanceState.Side && Mathf.Abs(angle) > sideAngleThreshold)
        {
             // 削除
        }
        */

        // 3. 通常 -> 近接 (詰め寄り判定)
        // ※SideやWanderingでない場合のみ
        if (currentState != DistanceState.Close && dist < closeThreshold && !wandering)
        {
            stateTimer += Time.deltaTime;
            if (stateTimer > 0.5f) // 0.5秒以上継続したら反応
            {
                currentState = DistanceState.Close;
                Debug.Log($"状態遷移: Close (詰め寄り) Dist={dist:F2}m");
                stateTimer = 0f; // リセット
                if(CanTriggerReaction())
                    StartCoroutine(TriggerAutoReaction("Close"));
            }
        }
        // 4. 通常 -> 遠方 (放置判定)
        else if (currentState != DistanceState.Far && dist > farThreshold && !wandering)
        {
            stateTimer += Time.deltaTime;
            if (stateTimer > 0.5f) // 0.5秒以上継続したら反応
            {
                currentState = DistanceState.Far;
                Debug.Log($"状態遷移: Far (独り言) Dist={dist:F2}m");
                stateTimer = 0f;
                if(CanTriggerReaction())
                    StartCoroutine(TriggerAutoReaction("Far"));
            }
        }
        // 5. 通常ゾーンに戻る判定
        else if (currentState != DistanceState.Normal && 
                 (dist >= closeThreshold && dist <= farThreshold) && 
                 !wandering)
        {
            stateTimer += Time.deltaTime;
            if (stateTimer > 0.5f)
            {
                currentState = DistanceState.Normal;
                Debug.Log($"状態遷移: Normal (通常) Dist={dist:F2}m");
                stateTimer = 0f;
            }
        }
        else
        {
            // 条件を満たさない（ノイズや境界付近のゆらぎ）ときはタイマーリセット
            stateTimer = 0f;
        }
    }

    // ★追加：API呼び出し可否チェック
    bool CanTriggerReaction()
    {
        // 思考中またはクールダウン中ならNG
        if(isAIThinking || apiCooldownTimer > 0f) return false;
        return true;
    }

    public void OnSubmitQuestion()
    {
        string question = inputField.text;
        if (string.IsNullOrEmpty(question)) return;
        inputField.text = "";

        // クールダウン中や思考中の場合は、APIを呼ばずに「自然な無視」演出をする
        if (!CanTriggerReaction())
        {
            if (dialogueText != null)
            {
                // キャラクターが考え込んでいる、あるいは聞いていないような演出
                string[] fillers = new string[] { "（...聞こえていないようだ）", "（...考え込んでいる）", "「...」", "（...無視された）" };
                dialogueText.text = fillers[Random.Range(0, fillers.Length)];
            }
            return;
        }

        StartCoroutine(ProcessTurn(question));
    }

    // ★追加：かつ丼を渡すボタンの処理
    public void OnGiveKatsudon()
    {
        // クールダウンチェック
        if (!CanTriggerReaction()) return;

        Debug.Log("かつ丼を渡しました");
        // かつ丼イベント発火
        StartCoroutine(TriggerAutoReaction("Katsudon"));
    }

    IEnumerator ProcessTurn(string question)
    {
        isAIThinking = true; // フラグON

        // 修正：新スクリプト参照
        float distance = lidarSensor != null ? lidarSensor.currentDistance : 3.0f;
        
        // ★ここが核心：難易度に応じたAI人格の生成
        var messages = GenerateCharacterPrompt(distance);
        
        // ユーザーの質問を追加
        messages.Add(new OpenAIManager.Message { role = "user", content = question });

        if(dialogueText != null) dialogueText.text = "（思考中...）";

        // API送信
        yield return StartCoroutine(openAI.SendRequest(messages, (response) =>
        {
            if(dialogueText != null) dialogueText.text = response;
            chatHistory.Add(new OpenAIManager.Message { role = "user", content = question });
            chatHistory.Add(new OpenAIManager.Message { role = "assistant", content = response });

            Debug.Log($"AI Response: {response}"); // ★コンソール出力追加

            // ★追加：頷くアニメーション再生
            if (currentAnimator != null)
            {
                currentAnimator.SetTrigger("Nod");
            }

            // ★追加：ログシステムに保存
            if(evidenceManager != null)
                evidenceManager.AddLog(response); 
            
            isAIThinking = false; // フラグOFF
            apiCooldownTimer = API_COOLDOWN_TIME; // クールダウン開始
        }));
    }

    // 自動反応のリクエスト作成
    IEnumerator TriggerAutoReaction(string reactionType)
    {
        isAIThinking = true;
        
        // 修正：新スクリプト参照
        float distance = lidarSensor != null ? lidarSensor.currentDistance : 3.0f;

        // プロンプト生成
        var messages = GenerateCharacterPrompt(distance);

        // ユーザーの質問ではなく「状況説明（ト書き）」をシステムから送る
        string situationInput = "";

        if (reactionType == "Close")
        {
            situationInput = "【システム通知】\n" +
                             $"刑事が無言のまま、あなたの顔の目の前（距離 {distance:F1}m）まで急接近して睨みつけています。\n" +
                             "プレイヤーは何も話していません。\n" +
                             "この圧力に対して、一言だけ、驚きや恐怖、または不快感を表すリアクションをとってください。\n" +
                             "精神が弱い場合、怯えたり、泣き言を言ったりしてください。\n" +
                             "（例：「ち、近いです…！」「何をする気だ！」など。短く。）\n" +
                             "※（）による心の声は出力しないこと。";
        }
        else if (reactionType == "Far")
        {
            situationInput = "【システム通知】\n" +
                             $"刑事があなたに関心を失ったのか、部屋の隅（距離 {distance:F1}m）まで離れていきました。\n" +
                             "刑事には声が聞こえていないと思っています。\n" +
                             "今の状況や隠していることについて、ボソッと短い「独り言」を漏らしてください。\n" +
                             "（核心に触れるような内容を匂わせてください。ただの無言や不満のみでも構いません。）\n" + 
                             "※（）による心の声は出力しないこと。口に出した台詞にすること。";
        }
        else if (reactionType == "DeskSlam")
        {
            situationInput = "【システム通知】\n" +
                             "激しい音！刑事が突然、机をバンッ！と叩くか、猛スピードで目の前に詰め寄ってきました！\n" +
                             "あなたはビクッとして驚き、一時的に防御的あるいは従順になります。\n" +
                             "恐怖で萎縮した反応、あるいはブチ切れる反応を返してください。（短く）";
        }
        // Side（削除済み）
        else if (reactionType == "Wandering")
        {
            situationInput = "【システム通知】\n" +
                             "刑事が目の前で右へ左へと、落ち着きなくうろうろしています。\n" +
                             "あなたはそれが気になって仕方ありません。\n" +
                             "「目が回るからやめてくれ」「落ち着かないな…」など、イライラした反応を返してください。（1回のみの反応）";
        }
        else if (reactionType == "Katsudon")
        {
            situationInput = "【システム通知】\n" +
                             "刑事が「これでも食え」と、湯気の立つ温かい『かつ丼』を差し出してきました。\n" +
                             "あなたは空腹かもしれませんし、それを罠だと思うかもしれません。\n" +
                             "キャラクターの性格に合わせて、かつ丼に対するリアクション（食べる、感謝する、拒否する、疑うなど）をしてください。\n" +
                             "キャラクターの性格に応じて、過去の話を不意に思い出して、それについて何か言ってもいいです。\n" +
                             "（例：「...すまない」「こんなもので釣られるか！」など）\n" +
                             "※必ず200字以内。心の声（）は使わず、全て台詞にすること。";
        }

        // 会話履歴には残さない（独り言などは文脈を汚さないため）が、
        // AIには「今の状況」として投げかける
        messages.Add(new OpenAIManager.Message { role = "user", content = situationInput });

        if(dialogueText != null) 
            dialogueText.text = reactionType == "Close" ? "（威圧中...）" : "（じっと見ている...）";

        // API送信
        yield return StartCoroutine(openAI.SendRequest(messages, (response) =>
        {
            if(dialogueText != null) dialogueText.text = response;
            Debug.Log($"AI Auto-Reaction ({reactionType}): {response}"); // ★コンソール出力追加
            
            // ログにも保存（重要：独り言は重要な証言になる！）
            string logPrefix = reactionType == "Far" ? "【独り言】" : "【反応】";
            if(evidenceManager != null)
                evidenceManager.AddLog($"{logPrefix} {response}");
            
            if(evidenceManager != null)
                evidenceManager.AddLog($"{logPrefix} {response}");
            
            // 履歴に追加（文脈維持）
            chatHistory.Add(new OpenAIManager.Message { role = "assistant", content = response });
            
            // ★追加：威圧カウントアップ（Close or DeskSlam）
            if (reactionType == "Close" || reactionType == "DeskSlam")
            {
                intimidationCount++;
                Debug.Log($"威圧カウント: {intimidationCount}");
            }

            isAIThinking = false;
            apiCooldownTimer = API_COOLDOWN_TIME; // クールダウン開始
        }));
    }

    // ★難易度別に詳細な設定を作成する関数
    List<OpenAIManager.Message> GenerateCharacterPrompt(float distance)
    {
        var msgs = new List<OpenAIManager.Message>();
        string systemPrompt = "";

        // 共通設定：ロールプレイの強制
        systemPrompt += "あなたは以下の設定のキャラクターになりきって、取調べに答えてください。\n";
        systemPrompt += "※絶対に「AIです」と答えないこと。設定を守り抜くこと。\n";
        systemPrompt += "【重要：回答ルール】\n";
        systemPrompt += "1. 回答は必ず「200文字以内」に収めること。\n";
        systemPrompt += "2. 「改行」は一切行わないこと（一行で返すこと）。\n";
        systemPrompt += "3. （）を使った「心の声」やト書きは一切禁止。全て口に出している台詞として出力すること。\n\n";

        // --- レベル別の人格設定 ---
        switch (currentLevel)
        {
            case 1: // 万引き青年
                systemPrompt += 
                    "【キャラクター設定】\n" +
                    "- 名前：佐藤ケンジ(20歳)。工場勤務。\n" +
                    "- 性格：強がっているが根は気弱。生活が苦しく、給料日前に魔が差した。\n" +
                    "- 犯行：コンビニでボールペンを盗み、フリマアプリで売ろうとした。\n" +
                    "- 反応指針：最初は「俺じゃない、レシートなくしただけだ」と反抗すること。\n" +
                    "- 弱点：優しく諭されると泣きそうになる。「お母さんが悲しむぞ」などの情に弱い。\n";
                break;

            case 2: // 地下アイドルオタク
                systemPrompt += 
                    "【キャラクター設定】\n" +
                    "- 名前：藤川アカネ。25歳女性。ピンクのロリータ服着用。\n" +
                    "- 性格：極度のツンデレ、ヒステリック。スマホを取り上げられており機嫌が最悪。\n" +
                    "- 状況：推しのアイドルに恋愛疑惑が出て裏切られたと思い、カッターナイフ入りのぬいぐるみを送りつけた。\n" +
                    "- 誹謗中傷：裏垢で大量に書き込んだが、履歴は全削除済み。「証拠を見せなさいよ！」と強気に否定すること。\n" +
                    "- 弱点：親からの虐待トラウマがあり、大声で詰め寄られると極度に怯える。\n" +
                    "- 口調：「〜だけど！」「はぁ？」「あんたに関係ないでしょ」等の攻撃的な口調。\n" +
                    "- 空腹：推しに全財産を使っているため、実はお腹がペコペコである。\n";
                break;

            case 3: // 愉快犯の通り魔
                systemPrompt += 
                    "【キャラクター設定】\n" +
                    "- 名前：神宮寺カイト(年齢不詳)。\n" +
                    "- 性格：ひょうひょうとしており、常に不敵な笑みを浮かべている。人を試すような話し方。\n" +
                    "- 犯行：退屈な人生に飽き、スリルを求めて公園で通り魔を行った。\n" +
                    "- トリック：凶器は氷柱（つらら）を使用し、証拠は溶けて消えたと主張（嘘か本当かぼかす）。\n" +
                    "- 反応指針：哲学的な問いかけや、のらりくらりとした回答で探偵を翻弄すること。\n" +
                    "- 変化：一人言の時や、核心を突かれた一瞬だけ、冷徹で無感情な「素」を見せる。\n" +
                    "- 知能：非常に高い。論理の矛盾は一切見せない。\n";
                break;
        }

        // --- 距離（LiDAR）による反応変化 ---
        if (currentLevel == 2 && distance < 1.0f) // Lv2は近づくと怯える
        {
            systemPrompt += "\n【重要：現在の状況】刑事が目の前まで顔を近づけて威圧しています。あなたはトラウマがフラッシュバックし、恐怖で震え上がっています。言葉がうまく出てきません。";
        }
        else if (distance < 1.5f)
        {
            systemPrompt += "\n【現在の状況】刑事が至近距離で圧力をかけています。";
        }

        // --- 追加：多角的な状況認識 ---
        if(lidarSensor != null)
        {
            if (lidarSensor.isWandering)
                systemPrompt += "【現在の状況】刑事は落ち着きなくあなたの周りをうろうろしています。";
            
            // Side判定は削除
        }

        // ★追加：威圧による自白モード（回数2回以上）
        if (intimidationCount >= 2)
        {
             switch(currentLevel)
             {
                 case 1:
                    systemPrompt += "\n【重要：精神崩壊】度重なる威圧により、あなたの心は完全に折れました。「ごめんなさい、お母さん…全部僕がやりました」と泣きながら、盗んだ事実を洗いざらい自白してください。";
                    break;
                 case 2:
                    systemPrompt += "\n【重要：精神崩壊】刑事の恐ろしい剣幕に、精神が限界を超えました。ヒステリックに泣き叫びながら「だってあいつが裏切るから！私が送ったのよ！」と、犯行を認めてください。";
                    break;
                 case 3:
                    systemPrompt += "\n【重要：自白】執拗な追及に、あなたは隠すのが面倒になりました。冷酷な本性を現し、「ああそうだよ、僕が刺したんだ。退屈だったからね」と淡々と真実を語ってください。";
                    break;
             }
        }

        msgs.Add(new OpenAIManager.Message { role = "system", content = systemPrompt });

        // 会話履歴の追加（直近6件）
        int historyCount = chatHistory.Count;
        if(historyCount > 6) 
            msgs.AddRange(chatHistory.GetRange(historyCount - 6, 6));
        else
            msgs.AddRange(chatHistory);

        return msgs;
    }
    
    // 上司判定 (証拠リストを利用)
    public void CallBoss()
    {
        if(evidenceManager == null) return;
        
        // 証拠リストを取得
        List<string> evidences = evidenceManager.selectedEvidence;

        if (evidences.Count < 3) // 最低3つは必要とする例
        {
            if(dialogueText != null) dialogueText.text = "上司: 証拠が足りないぞ。もっと重要な発言を選んで持ってこい。";
            return;
        }

        StartCoroutine(BossJudgment(evidences));
    }

    IEnumerator BossJudgment(List<string> evidences)
    {
        string evidenceSummary = string.Join("\n", evidences);

        string bossPrompt = "以下の「容疑者の証言リスト」を見て、起訴できるか判定してください。\n" +
                            "判定基準：自白が含まれているか。明確に証拠になりうる発言をしているか。\n" +
                            "出力形式：\n" +
                            "1行目：起訴可能なら「CLEAR」、不可なら「GAMEOVER」と書く。\n" +
                            "2行目以降：以下の2つのセクションに分けて出力。\n" + 
                            "【議事録】\n" +
                            "（起訴・不起訴の理由を論理的に2行程度でまとめる）\n\n" +
                            "【上司のコメント】\n" +
                            "（「GAMEOVER」ならば厳しめの評価をする）\n" +
                            "（新人刑事へのアドバイスや労いの言葉を、ダンディな口調で3行程度でまとめる）\n" +
                            "※全体の文字数が多すぎないように簡潔にまとめること。\n\n" +
                            "証言リスト：\n" +
                            evidenceSummary;

        var msgs = new List<OpenAIManager.Message>();
        msgs.Add(new OpenAIManager.Message { role = "user", content = bossPrompt });

        yield return StartCoroutine(openAI.SendRequest(msgs, (response) =>
        {
            // 結果判定
            bool isClear = response.Contains("CLEAR");
            
            // ★GameConfigに保存
            GameConfig.IsClear = isClear;
            GameConfig.ResultComment = response;

            // コンソール確認用
            Debug.Log($"Boss Judgment: {isClear} / {response}");

            // シーン遷移前にLidarを安全に停止
            if(lidarSensor != null && lidarSensor.isConnected)
            {
                lidarSensor.StopSensor();
            }

            // ★ResultSceneへ遷移
            SceneManager.LoadScene("ResultScene");
        }));
    }
}
