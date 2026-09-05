namespace Gridlet;

/// <summary>Validation and normalization for Gridlet-owned URL paths.</summary>
/// <remarks>
/// Values accepted here are route paths, not arbitrary URI strings. They may contain multiple
/// non-empty segments, but never query strings, fragments, encoded separators, dot segments or
/// characters that ASP.NET could interpret differently from the value used for lookup.
/// </remarks>
public static class GridletRoutePath
{
    /// <summary>
    /// Normalizes a route path by removing surrounding slashes and whitespace. Returns
    /// <see langword="false"/> when the path is not safe for use in an endpoint template.
    /// </summary>
    public static bool TryNormalize(
        string? value,
        out string normalized,
        bool allowEmpty = false,
        int maxLength = 256)
    {
        normalized = string.Empty;
        if (value is null)
        {
            return allowEmpty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return allowEmpty;
        }

        // Reject percent escapes and backslashes before trimming. Encoded '/' and '\\' must not
        // become a second route segment after a later URI decode.
        if (trimmed.Contains('%', StringComparison.Ordinal) || trimmed.Contains('\\'))
        {
            return false;
        }

        trimmed = trimmed.Trim('/');
        if (trimmed.Length == 0)
        {
            return allowEmpty;
        }

        if (trimmed.Length > maxLength || trimmed.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = trimmed.Split('/');
        if (segments.Length == 0)
        {
            return allowEmpty;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." || !IsSafeSegment(segment))
            {
                return false;
            }
        }

        normalized = string.Join('/', segments);
        return true;
    }

    /// <summary>Returns whether two normalized paths overlap by equality or ancestry.</summary>
    public static bool IsEqualOrAncestor(string first, string second)
        => first.Equals(second, StringComparison.OrdinalIgnoreCase) ||
           (second.Length > first.Length &&
            second.StartsWith(first, StringComparison.OrdinalIgnoreCase) &&
            second[first.Length] == '/');

    /// <summary>Returns whether a path is composed only of safe ASCII route characters.</summary>
    public static bool IsSafeSegment(string segment)
    {
        if (segment.Length == 0 || segment.Length > 128)
        {
            return false;
        }

        foreach (var character in segment)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gets the first segment from an already normalized non-empty path.</summary>
    public static string FirstSegment(string normalized)
    {
        var separator = normalized.IndexOf('/');
        return separator < 0 ? normalized : normalized[..separator];
    }
}
