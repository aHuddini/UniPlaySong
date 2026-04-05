namespace Playnite;

public static partial class Loc
{

    /// <summary>
    /// "Fluent test string"
    /// </summary>
    public static string example_string()
    {
        return GetString("example_string");
    }
}

public static partial class LocId
{
    public static readonly HashSet<string> StringIds = new()
    {
        "example_string"
    };

    /// <summary>
    /// "Fluent test string"
    /// </summary>
    public const string example_string = "example_string";
}
