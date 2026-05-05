using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SYNC;

public class MusicService
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        UseCookies = false
    }) { Timeout = TimeSpan.FromSeconds(15) };

    static MusicService()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    }

    public static async Task<object> DoSearch(string query)
    {
        try
        {
            const string KEY = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8";
            const string URL = "https://www.youtube.com/youtubei/v1/search?key=" + KEY;

            var body = new
            {
                context = new { client = new { clientName = "WEB", clientVersion = "2.20240101.00.00", hl = "ko", gl = "KR" } },
                query,
                @params = "EgIQAQ%3D%3D"
            };

            var res = await _http.PostAsJsonAsync(URL, body);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync();
            var tracks = ParseResults(json);
            return new { type = "searchResult", success = true, tracks };
        }
        catch (Exception ex)
        {
            return new { type = "searchResult", success = false, error = ex.Message };
        }
    }

    public static async Task<object> DoSuggest(string query)
    {
        try
        {
            var url = $"https://suggestqueries.google.com/complete/search?client=firefox&ds=yt&q={Uri.EscapeDataString(query)}&hl=ko";
            var json = await _http.GetStringAsync(url);
            var arr = JsonSerializer.Deserialize<JsonElement>(json);
            var suggestions = new List<string>();
            if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 1)
            {
                foreach (var s in arr[1].EnumerateArray())
                {
                    var sug = s.GetString();
                    if (!string.IsNullOrEmpty(sug)) suggestions.Add(sug);
                    if (suggestions.Count >= 8) break;
                }
            }
            return new { type = "suggestResult", success = true, suggestions };
        }
        catch (Exception ex)
        {
            return new { type = "suggestResult", success = false, suggestions = Array.Empty<string>(), error = ex.Message };
        }
    }

    public static async Task<object> DoFetchLyrics(string title, string channel, double duration)
    {
        var lines = await TryLrclib(title, channel, duration);
        if (lines == null) lines = await TryNetEase(title, channel, duration);

        return new { type = "lyricsResult", success = lines != null, lines = lines ?? Array.Empty<object>() };
    }

    private static object[] ParseResults(string json)
    {
        var list = new List<object>();
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            var sections = doc.GetProperty("contents").GetProperty("twoColumnSearchResultsRenderer").GetProperty("primaryContents").GetProperty("sectionListRenderer").GetProperty("contents");

            foreach (var sec in sections.EnumerateArray())
            {
                if (!sec.TryGetProperty("itemSectionRenderer", out var isr)) continue;
                if (!isr.TryGetProperty("contents", out var items)) continue;

                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("videoRenderer", out var vr)) continue;
                    if (!vr.TryGetProperty("videoId", out var vid)) continue;
                    var id = vid.GetString(); if (string.IsNullOrEmpty(id)) continue;

                    var title = vr.TryGetProperty("title", out var ti) ? ti.GetProperty("runs")[0].GetProperty("text").GetString() : "";
                    var ch = vr.TryGetProperty("ownerText", out var ow) ? ow.GetProperty("runs")[0].GetProperty("text").GetString() : "";
                    var durStr = (vr.TryGetProperty("lengthText", out var lt) && lt.TryGetProperty("simpleText", out var st)) ? st.GetString() : "";

                    list.Add(new { id, title, channel = ch, dur = ParseDur(durStr), thumb = $"https://i.ytimg.com/vi/{id}/mqdefault.jpg" });
                    if (list.Count >= 20) break;
                }
                if (list.Count >= 20) break;
            }
        }
        catch { }
        return list.ToArray();
    }

    private static int ParseDur(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var p = s.Split(':');
        try { return p.Length == 3 ? int.Parse(p[0]) * 3600 + int.Parse(p[1]) * 60 + int.Parse(p[2]) : p.Length == 2 ? int.Parse(p[0]) * 60 + int.Parse(p[1]) : 0; }
        catch { return 0; }
    }

    // Lyrics logic (simplified port)
    private static async Task<object[]?> TryLrclib(string title, string artist, double dur)
    {
        try {
            var url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(title + " " + artist)}";
            var json = await _http.GetStringAsync(url);
            var results = JsonSerializer.Deserialize<JsonElement>(json);
            if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0) return null;
            var lrc = results[0].TryGetProperty("syncedLyrics", out var sl) ? sl.GetString() : null;
            return string.IsNullOrEmpty(lrc) ? null : ParseLrc(lrc);
        } catch { return null; }
    }

    private static async Task<object[]?> TryNetEase(string title, string artist, double dur)
    {
        try {
            var url = $"https://music.163.com/api/search/get?s={Uri.EscapeDataString(title + " " + artist)}&type=1&limit=5";
            var res = await _http.GetAsync(url);
            var json = await res.Content.ReadAsStringAsync();
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (!doc.TryGetProperty("result", out var result) || !result.TryGetProperty("songs", out var songs) || songs.GetArrayLength() == 0) return null;
            long id = songs[0].GetProperty("id").GetInt64();
            
            var lrcUrl = $"https://music.163.com/api/song/lyric?id={id}&lv=1&kv=1&tv=-1";
            var lrcJson = await _http.GetStringAsync(lrcUrl);
            var lrcDoc = JsonSerializer.Deserialize<JsonElement>(lrcJson);
            var lrcText = lrcDoc.TryGetProperty("lrc", out var l) ? l.GetProperty("lyric").GetString() : null;
            return string.IsNullOrEmpty(lrcText) ? null : ParseLrc(lrcText);
        } catch { return null; }
    }

    private static object[] ParseLrc(string lrc)
    {
        var list = new List<object>();
        foreach (var line in lrc.Split('\n'))
        {
            var match = Regex.Match(line.Trim(), @"^\[(\d+):(\d+)\.(\d+)\](.*)");
            if (!match.Success) continue;
            var t = int.Parse(match.Groups[1].Value) * 60.0 + int.Parse(match.Groups[2].Value) + int.Parse(match.Groups[3].Value.PadRight(3, '0')[..3]) / 1000.0;
            var text = match.Groups[4].Value.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            list.Add(new { start = t, text });
        }
        var result = new List<object>();
        for (int i = 0; i < list.Count; i++)
        {
            var cur = (dynamic)list[i];
            var end = i + 1 < list.Count ? ((dynamic)list[i + 1]).start : cur.start + 5.0;
            result.Add(new { start = cur.start, end, text = cur.text });
        }
        return result.ToArray();
    }
}
