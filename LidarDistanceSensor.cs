using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

// GitHubライブラリのクラスを使用するための定義
public class LidarDistanceSensor : MonoBehaviour
{
    [Header("接続設定")]
    public string portName = "COM4"; // A1M8のポート
    
    [Header("検知パラメータ")]
    public float minRange = 0.05f; // これより近いのはノイズとして無視 (m)
    public float maxRange = 5.0f; // これより遠いのは無視 (m)
    
    // 背面の壁などを無視するための角度フィルタ (オプション)
    // 0〜360度。全方位検知なら 0-360
    public float angleMin = 0f;   
    public float angleMax = 360f; 

    [Header("出力値（読み取り専用）")]
    public float currentDistance = 3.0f; // メインの距離データ
    public bool isConnected = false;

    // 内部変数
    private bool isScanning = false;
    private LidarData[] dataBuffer; // データ受け取り用バッファ

    void Start()
    {
        // バッファ確保 (十分なサイズ)
        dataBuffer = new LidarData[2048];

        // 1. ドライバの初期化と接続
        try 
        {
            // ライブラリの関数を呼び出して接続
            int result = RplidarBinding.OnConnect(portName);
            if (result == 0)
            {
                // 成功時のみ開始
                RplidarBinding.StartMotor();
                RplidarBinding.StartScan();
                
                isConnected = true;
                isScanning = true;
                Debug.Log($"LiDAR接続成功: {portName}");
            }
            else
            {
                // 失敗時
                isConnected = false;
                Debug.LogError($"LiDAR接続失敗: {portName} (Error Code: {result})");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"LiDAR接続エラー: {e.Message}");
        }
    }

    void OnDestroy()
    {
        StopSensor();
    }

    public void StopSensor()
    {
        if (isConnected)
        {
            isScanning = false; // 先にスキャンフラグを落とす
            
            try 
            {
                RplidarBinding.EndScan();
                RplidarBinding.EndMotor();
                RplidarBinding.OnDisconnect();
            }
            catch(Exception e)
            {
                // エラーが出てもフリーズさせない
                Debug.LogWarning($"Lidar Disconnect Error: {e.Message}");
            }
            finally
            {
                isConnected = false;
            }
        }
    }

    // ★追加：負荷軽減のためのスキャン間隔
    public float scanInterval = 0.1f; // 0.1秒ごとに処理 (10FPS)
    private float scanTimer = 0f;

    void Update()
    {
        if (!isScanning) return;

        // タイマー更新
        scanTimer += Time.deltaTime;
        if (scanTimer < scanInterval) return; // 間隔未満ならスキップ
        scanTimer = 0f; // リセット

        // 2. データの取得
        // RplidarBinding.GetData を使用
        try
        {
            // バッファオーバーフロー防止のリセットも検討すべきだが、ドライバ仕様に任せる
            int count = RplidarBinding.GetData(ref dataBuffer);
            if (count > 0)
            {
                ProcessScanData(count);
            }
        }
        catch (Exception)
        {
            // エラー無視
        }
    }

    // --- 追加：高度な検知パラメータ ---
    [Header("高度な検知 (読み取り専用)")]
    public float currentAngle = 0f;    // 最短距離への角度
    public float approachSpeed = 0f;   // 接近速度 (m/s): 正なら接近
    public bool isWandering = false;   // 落ち着きがないかどうか
    
    // 内部計算用
    private float previousDistance = 0f;
    private float velocitySmooth = 0f;
    private float angleChangeSum = 0f;
    private float wanderCheckTimer = 0f;

    // 3. データ解析：最短距離を見つける
    void ProcessScanData(int count)
    {
        float minDistFound = float.MaxValue;
        float minAngleFound = 0f; // その時の角度
        bool foundValidPoint = false;

        // デバッグ：最初の数点の生データを表示（ユニット確認用）
        if (count > 0 && UnityEngine.Random.Range(0, 30) == 0) // たまにログ出力
        {
             // Debug.Log($"Raw Data [0]: Dist={dataBuffer[0].distant}, Angle={dataBuffer[0].theta}");
        }

        for (int i = 0; i < count; i++)
        {
            float rawDist = dataBuffer[i].distant;
            float angle = dataBuffer[i].theta;

            // 単位補正: もし値が巨大(10.0以上)なら、mm単位とみなしてmに直す
            if (rawDist > 10.0f) 
            {
                rawDist /= 1000.0f; // mm -> m
            }
            
            // 0は無効値
            if (rawDist <= 0.001f) continue;

            // ノイズ除去
            if (rawDist < minRange || rawDist > maxRange) continue;

            // 角度制限
            if (angle < angleMin || angle > angleMax) continue;

            // 最短距離を更新
            if (rawDist < minDistFound)
            {
                minDistFound = rawDist;
                minAngleFound = angle;
                foundValidPoint = true;
            }
        }

        // 有効な点が見つかったら距離を更新
        if (foundValidPoint)
        {
            // 距離の変化速度 (m/s) を計算
            // LowPassFilterでノイズ軽減
            float rawDiff = previousDistance - minDistFound; // 正なら近づいている
            float speed = rawDiff / Time.deltaTime;
            
            // 異常な速度(瞬間移動)は無視
            if(Mathf.Abs(speed) < 10.0f) 
            {
                // スムージング
                approachSpeed = Mathf.Lerp(approachSpeed, speed, Time.deltaTime * 5f);
            }

            // 角度の変化量を計測 (Wandering判定)
            float angleDiff = Mathf.DeltaAngle(currentAngle, minAngleFound);
            angleChangeSum += Mathf.Abs(angleDiff);

            // 値更新
            currentAngle = minAngleFound;
            currentDistance = Mathf.Lerp(currentDistance, minDistFound, Time.deltaTime * 10f);
            previousDistance = minDistFound;
        }
        else
        {
             // 見つからない＝遠い
             currentDistance = Mathf.Lerp(currentDistance, maxRange, Time.deltaTime * 2f);
             approachSpeed = Mathf.Lerp(approachSpeed, 0f, Time.deltaTime * 2f);
        }
        
        // --- 徘徊 (Wandering) 判定ロジック ---
        // 0.5秒ごとに「角度の総移動量」をチェック
        wanderCheckTimer += Time.deltaTime;
        if (wanderCheckTimer > 0.5f)
        {
            // 0.5秒で一定以上角度が動いていたら「落ち着きがない」
            // 人が左右に揺れると 0.5秒で 20~30度くらい動く想定
            if (angleChangeSum > 30.0f)
            {
                isWandering = true;
            }
            else
            {
                isWandering = false;
            }
            
            // リセット
            angleChangeSum = 0f;
            wanderCheckTimer = 0f;
        }
    }
}
