using System.Runtime.CompilerServices;

namespace Godot.Extensions;

public static class StringExtensions
{
    public static real_t ToReal(this string s)
    {
#if GODOT_REAL_T_IS_DOUBLE
        return (real_t)s.ToFloat();
#else
        return s.ToFloat();
#endif
    }

    /// <summary>
    /// Returns <c>true</c> if this string contains a valid float. This is inclusive of integers,
    /// and also supports exponents.
    /// </summary>
    /// <param name="s">The string to check.</param>
    /// <returns><c>true</c> if the string contains a valid floating point number.</returns>
    public static bool IsValidReal(this string s)
    {
        real_t result;
#if GODOT_REAL_T_IS_DOUBLE
        return double.TryParse(s, out result);
#else
        return float.TryParse(s, out result);
#endif
    }

    /// <summary>
    /// <para>
    /// Copied from <c>Chickensoft.AutoInject</c>.
    /// </para>
    /// Converts an ASCII string to PascalCase. This looks insane, but it is the
    /// fastest out of all the benchmarks I did.
    /// </para>
    /// <para>
    /// Since messing with strings can be slow and looking up nodes is a common
    /// operation, this is a good place to optimize. No heap allocations!
    /// </para>
    /// <para>
    /// Removes underscores, always capitalizes the first letter, and capitalizes
    /// the first letter after an underscore.
    /// </para>
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string AsciiToPascalCase(this string input)
    {
        var span = input.AsSpan();
        Span<char> output = stackalloc char[span.Length + 1];
        var outputIndex = 1;

        output[0] = '%';

        for (var i = 1; i < span.Length + 1; i++)
        {
            var c = span[i - 1];

            if (c == '_') continue;

            output[outputIndex++] = i == 1 || span[i - 2] == '_'
                ? (char)(c & 0xDF)
                : c;
        }

        return new string(output[..outputIndex]);
    }
}
