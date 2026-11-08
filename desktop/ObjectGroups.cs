using System.Text.Json.Nodes;

namespace Flint;

public static class ObjectGroups
{
    public static string[] Validate(IEnumerable<string> path)
    {
        var ids=path.ToArray();
        if(ids.Length>32 || ids.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>128) || ids.Distinct().Count()!=ids.Length)
            throw new ArgumentException("Invalid or excessively nested object group.");
        return ids;
    }
    public static IReadOnlyList<string> FromJson(JsonArray? value) => value==null?Array.Empty<string>():Validate(value.Select(n=>n!.GetValue<string>()));
}
