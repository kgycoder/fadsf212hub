using System.Text.Json;
using System.Web;

namespace SYNC;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
		
		MainWebView.Source = "index.html";
	}

	private async void OnNavigating(object sender, WebNavigatingEventArgs e)
	{
		if (e.Url.StartsWith("sync://"))
		{
			e.Cancel = true;
			var uri = new Uri(e.Url);
			var type = uri.Host;
			var query = HttpUtility.ParseQueryString(uri.Query);
			var dataJson = query["data"];
			
			if (string.IsNullOrEmpty(dataJson)) return;
			
			var doc = JsonSerializer.Deserialize<JsonElement>(dataJson);
			await HandleMessage(type, doc);
		}
	}

	private async Task HandleMessage(string type, JsonElement doc)
	{
		string? callbackId = doc.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;

		switch (type)
		{
			case "search":
				var q = doc.GetProperty("query").GetString();
				var searchRes = await MusicService.DoSearch(q ?? "");
				await SendToJs(searchRes, callbackId);
				break;

			case "suggest":
				var sq = doc.GetProperty("query").GetString();
				var sugRes = await MusicService.DoSuggest(sq ?? "");
				await SendToJs(sugRes, callbackId);
				break;

			case "fetchLyrics":
				var title = doc.GetProperty("title").GetString();
				var ch = doc.GetProperty("channel").GetString();
				var dur = doc.GetProperty("duration").GetDouble();
				var lyrRes = await MusicService.DoFetchLyrics(title ?? "", ch ?? "", dur);
				await SendToJs(lyrRes, callbackId);
				break;

			case "close":
				Application.Current?.Quit();
				break;
		}
	}

	private async Task SendToJs(object payload, string? callbackId)
	{
		var json = JsonSerializer.Serialize(payload);
		if (callbackId != null)
		{
			// Add id to payload if it's a callback response
			var obj = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
			if (obj != null)
			{
				obj["id"] = callbackId;
				json = JsonSerializer.Serialize(obj);
			}
		}

		var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
		await MainWebView.EvaluateJavaScriptAsync($"window.__sync && window.__sync(atob('{b64}'))");
	}
}
