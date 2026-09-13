using System.Text.Json;
using System.Text.RegularExpressions;
using MementoMori.Ortega.Share.Enums;

namespace MementoMori.BlazorShared.Models;

public static class ChatEmoticons
{
    private static readonly Regex Tokens = new(@"(#[0-9]{1,9}#)");
    private static readonly Atlas Data = Load();
    public static IEnumerable<int> Ids => Data.Sprites.Keys;
    public static string ReactionUrl(ChatReactionType reaction) => $"_content/MementoMori.BlazorShared/chat/reaction-{(int)reaction}-{Data.Version}.webp";
    public static string[] Split(string? text) => Tokens.Split(text ?? "");
    public static bool TryGetId(string token, out int id)
    {
        id = 0;
        return token.Length > 2 && token[0] == '#' && token[^1] == '#'
            && int.TryParse(token.AsSpan(1, token.Length - 2), out id) && Data.Sprites.ContainsKey(id);
    }

    public static string Style(int id, int size = 48)
    {
        var frame = Data.Sprites[id];
        var scale = (double)size / frame[2];
        return FormattableString.Invariant($"display:inline-block;vertical-align:middle;width:{size}px;height:{frame[3] * scale}px;background-image:url('./_content/MementoMori.BlazorShared/chat/emoticons-{Data.Version}.webp');background-repeat:no-repeat;background-size:{Data.Width * scale}px {Data.Height * scale}px;background-position:{-frame[0] * scale}px {-frame[1] * scale}px");
    }

    private static Atlas Load()
    {
        using var stream = typeof(ChatEmoticons).Assembly.GetManifestResourceStream("MementoMori.BlazorShared.wwwroot.chat.emoticons.json")!;
        return JsonSerializer.Deserialize<Atlas>(stream)!;
    }
    private sealed record Atlas(string Version, int Width, int Height, Dictionary<int, int[]> Sprites);
}
