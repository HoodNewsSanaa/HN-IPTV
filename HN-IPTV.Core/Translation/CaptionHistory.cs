using System.Text.RegularExpressions;
namespace HnIptv.Translation;

public sealed class CaptionHistory
{
    private string _text = "";
    public void Clear() => _text = "";
    public string Append(string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return _text;
        var old = _text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words = fragment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int overlap = 0;
        for (int count = Math.Min(30, Math.Min(old.Length, words.Length)); count > 0; count--)
            if (old.TakeLast(count).Select(Key).SequenceEqual(words.Take(count).Select(Key))) { overlap = count; break; }
        var combined = old.Concat(words.Skip(overlap)).TakeLast(42).ToArray();
        _text = string.Join(" ", combined);
        if (_text.Length > 240) {
            int split = _text.IndexOf(' ', _text.Length - 230);
            _text = "… " + _text[(split < 0 ? _text.Length - 230 : split + 1)..];
        }
        return _text;
    }
    private static string Key(string word) => Regex.Replace(word, @"[^\p{L}\p{N}]", "").ToUpperInvariant();
}
