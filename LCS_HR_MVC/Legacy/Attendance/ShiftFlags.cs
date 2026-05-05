using System.Collections.Generic;

public class ShiftFlags
{
    private readonly Dictionary<string, string> _statusCollection = new()
    {
        { "RA", "Rule Absent" },
        { "N", "Normal" },
        { "NO", "Not Out" },
        { "A", "Absent" },
        { "L", "Late" },
        { "E", "Early" },
        { "NI", "Not In" },
        { "HD", "Half Day" },
        { "LE", "Leave" }
    };

    public string this[string statusCode] => _statusCollection.TryGetValue(statusCode, out var statusValue)
        ? statusValue
        : string.Empty;

    public static string Normal => "N";
    public static string Late => "L";
    public static string NotIn => "NI";
    public static string NotOut => "NO";
    public static string Early => "E";
    public static string RuleAbsent => "RA";
    public static string Absent => "A";
    public static string HalfDay => "HD";
    public static string Leave => "LE";
}
