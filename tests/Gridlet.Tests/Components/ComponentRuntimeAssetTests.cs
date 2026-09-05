using Gridlet.Components;
using Xunit;

namespace Gridlet.Tests.Components;

/// <summary>
/// The standalone runtime carries its whole stylesheet as one template literal. A backtick written
/// inside it - in a comment, say, quoting a property name - ends the literal early, and the rest of
/// the sheet is then parsed as JavaScript. The file still parses, so a syntax check says nothing;
/// what happens instead is that every published component fails to render at run time. These tests
/// read the shipped asset and check the literal is intact.
/// </summary>
public sealed class ComponentRuntimeAssetTests
{
    private const string Marker = "runtimeStyle.textContent = `";

    private static string RuntimeScript()
    {
        var assembly = typeof(GridletComponentsOptions).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Gridlet.Components.UI.wwwroot.runtime.js");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The stylesheet the runtime injects, read from the opening backtick to the one that closes it.
    /// </summary>
    private static string RuntimeStyleSheet(string script)
    {
        var start = script.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "the runtime no longer builds its stylesheet from a template literal");
        var from = start + Marker.Length;
        var end = script.IndexOf('`', from);
        Assert.True(end > from, "the runtime stylesheet literal is never closed");
        return script[from..end];
    }

    [Fact]
    public void The_runtime_stylesheet_is_one_unbroken_literal()
    {
        var sheet = RuntimeStyleSheet(RuntimeScript());

        // The first and last rules of the sheet. Both present means no backtick cut it short: a
        // stray one would end the literal somewhere in between and the tail would be missing.
        Assert.Contains("@layer gridlet-reset, gridlet-chrome, gridlet;", sheet, StringComparison.Ordinal);
        Assert.Contains(".gridlet-runtime-message", sheet, StringComparison.Ordinal);
        Assert.Contains("prefers-color-scheme: dark", sheet, StringComparison.Ordinal);

        // Braces balance, which they cannot if the literal ended inside a rule.
        Assert.Equal(sheet.Count(c => c == '{'), sheet.Count(c => c == '}'));
    }

    [Fact]
    public void The_runtime_stylesheet_quotes_nothing_in_backticks()
    {
        var script = RuntimeScript();
        var sheet = RuntimeStyleSheet(script);

        // Belt and braces: whatever the sheet ends up containing, it must not contain the one
        // character that would end it. Comments inside it name CSS properties without quoting them.
        Assert.DoesNotContain('`', sheet);

        // And the literal really does reach the end of the sheet rather than stopping at a stray
        // backtick that happens to sit before the closing one.
        var after = script[(script.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length + sheet.Length)..];
        Assert.StartsWith("`;", after.TrimStart(), StringComparison.Ordinal);
    }
}
