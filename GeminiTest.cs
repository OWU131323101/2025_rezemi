using UnityEngine;
using System.Threading.Tasks;
using Google.GenAI;
using Google.GenAI.Types;

public class GeminiTest : MonoBehaviour
{
    [SerializeField] private string apiKey = "ここにAPIキーを貼り付ける";

    async void Start()
    {
        // SDK expects GEMINI_API_KEY environment variable by default
        if (!string.IsNullOrEmpty(apiKey))
        {
            System.Environment.SetEnvironmentVariable("GOOGLE_API_KEY", apiKey);
        }

        await MainTask();
    }

    private async Task MainTask()
    {
        try
        {
            var client = new Client();
            var response = await client.Models.GenerateContentAsync(
                model: "gemini-2.5-flash", // cURLコマンドに合わせて変更
                contents: "あなたは銀河鉄道の車掌です。一言挨拶してください。"
            );
            
            // Output the result
            if (response.Candidates != null && response.Candidates.Count > 0)
            {
                foreach (var part in response.Candidates[0].Content.Parts)
                {
                    Debug.Log(part.Text);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Gemini Error: " + e.Message);
        }
    }
}