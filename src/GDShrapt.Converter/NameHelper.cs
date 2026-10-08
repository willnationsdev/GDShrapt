using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GDShrapt.Converter;

public static class NameHelper
{
    private static readonly Regex WordSplitter = new Regex(@"[_\-\s]+|(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

    /// <summary>
    /// Converts a string to PascalCase. 
    /// Used for Classes, Structs, Enums, Properties, Methods, and Public Fields.
    /// </summary>
    public static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string[] words = WordSplitter.Split(input);
        var result = new StringBuilder();
        var textInfo = CultureInfo.InvariantCulture.TextInfo;

        foreach (string word in words)
        {
            if (string.IsNullOrEmpty(word)) continue;
            result.Append(textInfo.ToTitleCase(word.ToLower(CultureInfo.InvariantCulture)));
        }

        return result.ToString();
    }

    /// <summary>
    /// Converts a string to camelCase. 
    /// Used for Local Variables and Method Arguments.
    /// </summary>
    public static string ToCamelCase(string input)
    {
        string pascal = ToPascalCase(input);
        if (string.IsNullOrEmpty(pascal)) return string.Empty;

        return char.ToLower(pascal[0], CultureInfo.InvariantCulture) + pascal.Substring(1);
    }

    /// <summary>
    /// Converts a string to _camelCase. 
    /// Used for Private and Internal Fields.
    /// </summary>
    public static string ToPrivateField(string input)
    {
        string camel = ToCamelCase(input);
        if (string.IsNullOrEmpty(camel)) return string.Empty;
        
        return camel.StartsWith("_") ? camel : "_" + camel;
    }

    /// <summary>
    /// Converts a string to PascalCase with an 'I' prefix. 
    /// Used for Interfaces.
    /// </summary>
    public static string ToInterface(string input)
    {
        string pascal = ToPascalCase(input);
        if (string.IsNullOrEmpty(pascal)) return string.Empty;

        return pascal.StartsWith("I") && pascal.Length > 1 && char.IsUpper(pascal[1]) 
            ? pascal 
            : "I" + pascal;
    }
}


