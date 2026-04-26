using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json; 
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

public class OpenAIManager : MonoBehaviour
{
    [SerializeField] private string apiKey = "YOUR_OPENAI_API_KEY";
    // Using the proxy URL as requested
    private string apiUrl = "https://openai-api-proxy-746164391621.us-west1.run.app";

    [System.Serializable]
    public class Message
    {
        public string role;
        public string content;
    }

    // 外部から呼ぶ関数
    public IEnumerator SendRequest(List<Message> messages, System.Action<string> callback)
    {
        // リクエストボディの作成
        var requestData = new
        {
            model = "gpt-4o", // または gpt-3.5-turbo
            messages = messages,
            max_tokens = 150
        };

        string json = JsonConvert.SerializeObject(requestData);

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                JObject response = JObject.Parse(request.downloadHandler.text);
                string content = response["choices"][0]["message"]["content"].ToString();
                callback(content);
            }
            else
            {
                Debug.LogError("OpenAI Error: " + request.error + "\n" + request.downloadHandler.text);
                callback("エラーが発生しました。");
            }
        }
    }
}
