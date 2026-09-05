using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace Gridlet.BrowserTests;

/// <summary>
/// Pixel-level assertions for component surfaces. DOM and computed-style assertions are useful for
/// explaining a failure, but they miss the cascade and native control differences that users see.
/// This helper compares decoded RGBA pixels from exact component-root locator screenshots.
/// </summary>
internal static class BrowserPixelParity
{
    // Chromium can rasterize an identical native control on two pages with a tiny antialiasing
    // difference. The budget is deliberately expressed in pixels, not a broad image similarity
    // score: a shifted control or missing border changes thousands of pixels and still fails.
    public const double DefaultMaximumDifferentPixelRatio = 0.001; // 0.1% of the surface
    public const int DefaultMaximumChannelDelta = 32;

    /// <summary>
    /// Settles a page before capturing it. The preview and public route intentionally live in
    /// different documents, so waiting for one request is not enough: web fonts, image decodes,
    /// native control painting and a pending layout frame can otherwise make identical markup differ
    /// by a handful of antialiased pixels.
    /// </summary>
    public static async Task StabilizeAsync(IPage page, ILocator surface)
    {
        await surface.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = """
                *, *::before, *::after {
                  animation: none !important;
                  transition: none !important;
                  caret-color: transparent !important;
                  scroll-behavior: auto !important;
                }
                """,
        });
        await page.EvaluateAsync("""
            async () => {
              if (document.fonts?.ready) await document.fonts.ready;
              await Promise.all([...document.images]
                .filter(image => !image.complete)
                .map(image => new Promise(resolve => {
                  image.addEventListener('load', resolve, { once: true });
                  image.addEventListener('error', resolve, { once: true });
                })));
              // One frame settles font/layout changes; a second catches controls whose native
              // appearance is repainted after the first layout pass.
              await new Promise(requestAnimationFrame);
              await new Promise(requestAnimationFrame);
            }
            """);
        await surface.EvaluateAsync("root => { root.scrollTop = 0; root.scrollLeft = 0; }");
    }

    public static async Task<PixelComparison> CompareAsync(
        ILocator expected,
        ILocator actual,
        string artifactName,
        int channelTolerance = 0,
        double maxDifferentPixelRatio = DefaultMaximumDifferentPixelRatio,
        int maxChannelDelta = DefaultMaximumChannelDelta)
    {
        var screenshotOptions = new LocatorScreenshotOptions
        {
            Animations = ScreenshotAnimations.Disabled,
            Caret = ScreenshotCaret.Hide,
            Scale = ScreenshotScale.Css,
            Style = "*, *::before, *::after { animation: none !important; transition: none !important; caret-color: transparent !important; }",
        };
        // Native controls can paint differently depending on which page is active. The preview
        // and published surfaces intentionally live in separate pages, so activate each owning
        // page immediately before taking its screenshot instead of relying on whichever page the
        // browser happened to leave in front after the last navigation/assertion.
        var expectedPng = await CaptureAsync(expected, screenshotOptions);
        var actualPng = await CaptureAsync(actual, screenshotOptions);
        var expectedImage = PngImage.Decode(expectedPng);
        var actualImage = PngImage.Decode(actualPng);

        var diffWidth = Math.Max(expectedImage.Width, actualImage.Width);
        var diffHeight = Math.Max(expectedImage.Height, actualImage.Height);
        var diff = new PngImage(diffWidth, diffHeight);
        var differentPixels = 0;
        var maximumChannelDelta = 0;
        var minX = diffWidth;
        var minY = diffHeight;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < diffHeight; y++)
        {
            for (var x = 0; x < diffWidth; x++)
            {
                var expectedAt = x < expectedImage.Width && y < expectedImage.Height
                    ? (y * expectedImage.Width + x) * 4
                    : -1;
                var actualAt = x < actualImage.Width && y < actualImage.Height
                    ? (y * actualImage.Width + x) * 4
                    : -1;
                var delta = expectedAt < 0 || actualAt < 0
                    ? 255
                    : Math.Max(
                        Math.Abs(expectedImage.Pixels[expectedAt] - actualImage.Pixels[actualAt]),
                        Math.Max(
                            Math.Abs(expectedImage.Pixels[expectedAt + 1] - actualImage.Pixels[actualAt + 1]),
                            Math.Max(
                                Math.Abs(expectedImage.Pixels[expectedAt + 2] - actualImage.Pixels[actualAt + 2]),
                                Math.Abs(expectedImage.Pixels[expectedAt + 3] - actualImage.Pixels[actualAt + 3]))));
                maximumChannelDelta = Math.Max(maximumChannelDelta, delta);
                if (delta <= channelTolerance) continue;

                differentPixels++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                // A magenta diff is easy to spot, while its alpha records how large the local
                // difference was. Fully transparent pixels keep the artifact useful on a dark page.
                var diffAt = (y * diffWidth + x) * 4;
                diff.Pixels[diffAt] = 255;
                diff.Pixels[diffAt + 1] = 0;
                diff.Pixels[diffAt + 2] = 255;
                diff.Pixels[diffAt + 3] = (byte)Math.Clamp(delta * 4, 96, 255);
            }
        }

        var totalPixels = checked(diffWidth * diffHeight);
        var ratio = totalPixels == 0 ? 1 : (double)differentPixels / totalPixels;
        var match = expectedImage.Width == actualImage.Width && expectedImage.Height == actualImage.Height
            && ratio <= maxDifferentPixelRatio
            && maximumChannelDelta <= maxChannelDelta;
        var diffBounds = maxX < 0 ? null : new PixelBounds(minX, minY, maxX, maxY);
        string? artifactDirectory = null;
        if (!match)
        {
            artifactDirectory = Path.Combine(
                Path.GetTempPath(), "gridlet-browser-pixel-parity", Sanitize(artifactName));
            Directory.CreateDirectory(artifactDirectory);
            await File.WriteAllBytesAsync(Path.Combine(artifactDirectory, "preview.png"), expectedPng);
            await File.WriteAllBytesAsync(Path.Combine(artifactDirectory, "published.png"), actualPng);
            await File.WriteAllBytesAsync(Path.Combine(artifactDirectory, "diff.png"), diff.Encode());
            await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "metrics.txt"),
                $"Expected: {expectedImage.Width}x{expectedImage.Height}{Environment.NewLine}"
                + $"Published: {actualImage.Width}x{actualImage.Height}{Environment.NewLine}"
                + $"Different pixels: {differentPixels}/{totalPixels} ({ratio:P6}){Environment.NewLine}"
                + $"Maximum channel delta: {maximumChannelDelta}{Environment.NewLine}"
                + $"Difference bounds: {diffBounds?.ToString() ?? "none"}{Environment.NewLine}"
                + $"Channel tolerance: {channelTolerance}{Environment.NewLine}"
                + $"Maximum different-pixel ratio: {maxDifferentPixelRatio:P6}{Environment.NewLine}"
                + $"Maximum channel delta: {maxChannelDelta}{Environment.NewLine}");
        }

        return new PixelComparison(
            match,
            expectedImage.Width,
            expectedImage.Height,
            actualImage.Width,
            actualImage.Height,
            differentPixels,
            totalPixels,
            maximumChannelDelta,
            diffBounds,
            artifactDirectory);
    }

    /// <summary>
    /// Compares an isolated component's contract without pretending that native controls can be
    /// raster-identical across two documents. Names and control kinds must match exactly; authored
    /// geometry is compared in each component's own coordinate system; values, text, state and
    /// scroll/client dimensions must remain the same.
    /// </summary>
    public static async Task<StructureComparison> CompareStructureAsync(
        ILocator expected,
        ILocator actual,
        double maximumRelativeBoxDelta = 0.5)
    {
        var expectedStructure = await ReadStructureAsync(expected);
        var actualStructure = await ReadStructureAsync(actual);
        var differences = new List<string>();

        CompareDimension("root width", expectedStructure.Width, actualStructure.Width,
            maximumRelativeBoxDelta, differences);
        CompareDimension("root height", expectedStructure.Height, actualStructure.Height,
            maximumRelativeBoxDelta, differences);
        CompareInteger("root clientWidth", expectedStructure.ClientWidth, actualStructure.ClientWidth,
            differences);
        CompareInteger("root clientHeight", expectedStructure.ClientHeight, actualStructure.ClientHeight,
            differences);
        CompareInteger("root scrollWidth", expectedStructure.ScrollWidth, actualStructure.ScrollWidth,
            differences);
        CompareInteger("root scrollHeight", expectedStructure.ScrollHeight, actualStructure.ScrollHeight,
            differences);

        if (expectedStructure.Controls.Count != actualStructure.Controls.Count)
        {
            differences.Add($"control count {expectedStructure.Controls.Count} != {actualStructure.Controls.Count}");
        }

        var count = Math.Min(expectedStructure.Controls.Count, actualStructure.Controls.Count);
        for (var index = 0; index < count; index++)
        {
            var expectedControl = expectedStructure.Controls[index];
            var actualControl = actualStructure.Controls[index];
            var prefix = $"control[{index}]";
            if (!string.Equals(expectedControl.Name, actualControl.Name, StringComparison.Ordinal))
            {
                differences.Add($"{prefix} name '{expectedControl.Name}' != '{actualControl.Name}'");
            }

            if (!string.Equals(expectedControl.Type, actualControl.Type, StringComparison.Ordinal))
            {
                differences.Add($"{prefix} type '{expectedControl.Type}' != '{actualControl.Type}'");
            }

            CompareDimension($"{prefix} left", expectedControl.Left, actualControl.Left,
                maximumRelativeBoxDelta, differences);
            CompareDimension($"{prefix} top", expectedControl.Top, actualControl.Top,
                maximumRelativeBoxDelta, differences);
            CompareDimension($"{prefix} width", expectedControl.Width, actualControl.Width,
                maximumRelativeBoxDelta, differences);
            CompareDimension($"{prefix} height", expectedControl.Height, actualControl.Height,
                maximumRelativeBoxDelta, differences);
            CompareString($"{prefix} text", expectedControl.Text, actualControl.Text, differences);
            CompareString($"{prefix} value", expectedControl.Value, actualControl.Value, differences);
            CompareNullableBoolean($"{prefix} checked", expectedControl.Checked, actualControl.Checked, differences);
            CompareBoolean($"{prefix} disabled", expectedControl.Disabled, actualControl.Disabled, differences);
            CompareNullableInteger($"{prefix} selectedIndex", expectedControl.SelectedIndex,
                actualControl.SelectedIndex, differences);
            CompareInteger($"{prefix} clientWidth", expectedControl.ClientWidth, actualControl.ClientWidth,
                differences);
            CompareInteger($"{prefix} clientHeight", expectedControl.ClientHeight, actualControl.ClientHeight,
                differences);
            CompareInteger($"{prefix} scrollWidth", expectedControl.ScrollWidth, actualControl.ScrollWidth,
                differences);
            CompareInteger($"{prefix} scrollHeight", expectedControl.ScrollHeight, actualControl.ScrollHeight,
                differences);
        }

        return new StructureComparison(differences.Count == 0, differences, expectedStructure, actualStructure);
    }

    private static async Task<SurfaceStructure> ReadStructureAsync(ILocator surface)
    {
        var snapshot = await surface.EvaluateAsync<JsonElement>("""
            root => {
              const rootBox = root.getBoundingClientRect();
              const round = value => Math.round(value * 100) / 100;
              const textOf = element => element.matches('input, textarea, select')
                ? ''
                : (element.textContent || '').replace(/\\s+/g, ' ').trim();
              const valueOf = element => 'value' in element ? String(element.value ?? '') : null;
              const checkedOf = element => element instanceof HTMLInputElement
                && element.type === 'checkbox' ? element.checked : null;
              const selectedIndexOf = element => element instanceof HTMLSelectElement
                ? element.selectedIndex : null;
              const viewportOf = element => element.closest('.gridlet-grid-viewport, .gfd-grid-viewport');
              // The designer positions a named control inside a `.gfd-control`, while the
              // published runtime positions the named element itself (and gives a grid its own
              // viewport). Build the same logical control records from both DOM shapes: the
              // designer's data-type is the semantic kind, and a published data-role is its
              // equivalent. Do not pair the controls by DOM order or by the tag used to render
              // them; a checkbox is a label in one surface and a role-bearing label in the other.
              const designerHosts = [...root.querySelectorAll('.gfd-control[data-control-box]')];
              const semanticTypeOf = element => {
                const role = element.getAttribute('data-role');
                if (role) return role;
                const tag = element.tagName.toLowerCase();
                if (tag === 'span') return 'label';
                if (tag === 'input') return element.type === 'checkbox' ? 'checkbox' : 'textbox';
                if (tag === 'textarea') return 'textarea';
                return tag;
              };
              const hosts = designerHosts.length
                ? designerHosts.map(host => ({ host, element: host.querySelector('[data-name]'),
                    type: host.getAttribute('data-type') || '' }))
                : [...root.querySelectorAll('[data-name]')].map(element => ({
                    host: viewportOf(element) || element,
                    element,
                    type: semanticTypeOf(element),
                  }));
              const controls = hosts.map(({ host, element, type }) => {
                if (!element) return null;
                const viewport = viewportOf(element);
                const box = (viewport || host).getBoundingClientRect();
                const scrollSurface = viewport || element;
                return {
                  name: element.getAttribute('data-name') || '',
                  type,
                  left: round(box.left - rootBox.left),
                  top: round(box.top - rootBox.top),
                  width: round(box.width),
                  height: round(box.height),
                  text: textOf(element),
                  value: valueOf(element),
                  checked: checkedOf(element),
                  disabled: Boolean(element.disabled),
                  selectedIndex: selectedIndexOf(element),
                  clientWidth: scrollSurface.clientWidth,
                  clientHeight: scrollSurface.clientHeight,
                  scrollWidth: scrollSurface.scrollWidth,
                  scrollHeight: scrollSurface.scrollHeight,
                };
              }).filter(Boolean);
              controls.sort((left, right) =>
                left.name.localeCompare(right.name) || left.type.localeCompare(right.type));
              return {
                root: {
                  width: round(rootBox.width),
                  height: round(rootBox.height),
                  clientWidth: root.clientWidth,
                  clientHeight: root.clientHeight,
                  scrollWidth: root.scrollWidth,
                  scrollHeight: root.scrollHeight,
                },
                controls,
              };
            }
            """);

        var root = snapshot.GetProperty("root");
        var controls = snapshot.GetProperty("controls").EnumerateArray()
            .Select(control => new ControlStructure(
                control.GetProperty("name").GetString() ?? string.Empty,
                control.GetProperty("type").GetString() ?? string.Empty,
                control.GetProperty("left").GetDouble(),
                control.GetProperty("top").GetDouble(),
                control.GetProperty("width").GetDouble(),
                control.GetProperty("height").GetDouble(),
                control.GetProperty("text").GetString() ?? string.Empty,
                control.GetProperty("value").ValueKind is JsonValueKind.Null
                    ? null
                    : control.GetProperty("value").GetString(),
                control.GetProperty("checked").ValueKind is JsonValueKind.Null
                    ? null
                    : control.GetProperty("checked").GetBoolean(),
                control.GetProperty("disabled").GetBoolean(),
                control.GetProperty("selectedIndex").ValueKind is JsonValueKind.Null
                    ? null
                    : control.GetProperty("selectedIndex").GetInt32(),
                control.GetProperty("clientWidth").GetInt32(),
                control.GetProperty("clientHeight").GetInt32(),
                control.GetProperty("scrollWidth").GetInt32(),
                control.GetProperty("scrollHeight").GetInt32()))
            .ToArray();

        return new SurfaceStructure(
            root.GetProperty("width").GetDouble(),
            root.GetProperty("height").GetDouble(),
            root.GetProperty("clientWidth").GetInt32(),
            root.GetProperty("clientHeight").GetInt32(),
            root.GetProperty("scrollWidth").GetInt32(),
            root.GetProperty("scrollHeight").GetInt32(),
            controls);
    }

    private static void CompareDimension(string name, double expected, double actual,
        double maximumDelta, ICollection<string> differences)
    {
        if (Math.Abs(expected - actual) > maximumDelta)
        {
            differences.Add($"{name} {expected:0.##} != {actual:0.##} (delta {Math.Abs(expected - actual):0.##})");
        }
    }

    private static void CompareInteger(string name, int expected, int actual, ICollection<string> differences)
    {
        if (expected != actual) differences.Add($"{name} {expected} != {actual}");
    }

    private static void CompareString(string name, string? expected, string? actual, ICollection<string> differences)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            differences.Add($"{name} '{expected}' != '{actual}'");
    }

    private static void CompareBoolean(string name, bool expected, bool actual, ICollection<string> differences)
    {
        if (expected != actual) differences.Add($"{name} {expected} != {actual}");
    }

    private static void CompareNullableBoolean(string name, bool? expected, bool? actual, ICollection<string> differences)
    {
        if (expected != actual) differences.Add($"{name} {expected?.ToString() ?? "null"} != {actual?.ToString() ?? "null"}");
    }

    private static void CompareNullableInteger(string name, int? expected, int? actual, ICollection<string> differences)
    {
        if (expected != actual) differences.Add($"{name} {expected?.ToString() ?? "null"} != {actual?.ToString() ?? "null"}");
    }

    private static async Task<byte[]> CaptureAsync(
        ILocator surface,
        LocatorScreenshotOptions screenshotOptions)
    {
        await surface.Page.BringToFrontAsync();
        // Keep focus state independent of the last interaction used to reach each surface. A
        // focused designer button can otherwise leave native controls in the preview painted
        // differently from the same controls on a freshly navigated published page.
        await surface.Page.EvaluateAsync("document.activeElement?.blur();");
        // Rasterization can depend on the absolute origin of a component even when its relative
        // geometry is identical. Move only the root's painted box to a common integer viewport
        // origin for this screenshot; restore the complete authored style immediately afterwards
        // so this diagnostic capture cannot affect the page or its layout.
        var originalStyle = await surface.EvaluateAsync<string>("root => root.hasAttribute('style') ? '1' + root.getAttribute('style') : '0'");
        await surface.EvaluateAsync("""
            root => {
              const rect = root.getBoundingClientRect();
              root.style.transform = `translate(${Math.round(rect.left) - rect.left}px, ${Math.round(rect.top) - rect.top}px)`;
            }
            """);
        // Bringing a page forward can schedule a native-control repaint. Let that repaint and
        // its resulting layout frame settle while this page remains active.
        try
        {
            await surface.Page.EvaluateAsync("""
                async () => {
                  await new Promise(requestAnimationFrame);
                  await new Promise(requestAnimationFrame);
                }
                """);
            return await surface.ScreenshotAsync(screenshotOptions);
        }
        finally
        {
            await surface.EvaluateAsync("""
                (root, originalStyle) => {
                  if (originalStyle === '0') root.removeAttribute('style');
                  else root.setAttribute('style', originalStyle.slice(1));
                }
                """, originalStyle);
        }
    }

    private static string Sanitize(string value)
    {
        var safe = new string(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "comparison" : safe;
    }

    internal sealed record PixelComparison(
        bool IsMatch,
        int ExpectedWidth,
        int ExpectedHeight,
        int ActualWidth,
        int ActualHeight,
        int DifferentPixels,
        int TotalPixels,
        int MaximumChannelDelta,
        PixelBounds? DifferenceBounds,
        string? ArtifactDirectory)
    {
        public override string ToString()
            => $"Expected {ExpectedWidth}x{ExpectedHeight}, actual {ActualWidth}x{ActualHeight}; "
                + $"{DifferentPixels}/{TotalPixels} pixels differ, maximum channel delta "
                + $"{MaximumChannelDelta}."
                + (DifferenceBounds is null ? string.Empty : $" Difference bounds: {DifferenceBounds}.")
                + (ArtifactDirectory is null ? string.Empty : $" Artifacts: {ArtifactDirectory}");
    }

    internal sealed record PixelBounds(int Left, int Top, int Right, int Bottom)
    {
        public override string ToString() => $"({Left},{Top})-({Right},{Bottom})";
    }

    internal sealed record StructureComparison(
        bool IsMatch,
        IReadOnlyList<string> Differences,
        SurfaceStructure Expected,
        SurfaceStructure Actual)
    {
        public override string ToString()
            => Differences.Count == 0
                ? "Component structures match."
                : "Component structures differ: " + string.Join("; ", Differences);
    }

    internal sealed record SurfaceStructure(
        double Width,
        double Height,
        int ClientWidth,
        int ClientHeight,
        int ScrollWidth,
        int ScrollHeight,
        IReadOnlyList<ControlStructure> Controls);

    internal sealed record ControlStructure(
        string Name,
        string Type,
        double Left,
        double Top,
        double Width,
        double Height,
        string Text,
        string? Value,
        bool? Checked,
        bool Disabled,
        int? SelectedIndex,
        int ClientWidth,
        int ClientHeight,
        int ScrollWidth,
        int ScrollHeight);

    /// <summary>A small PNG codec kept in the test project so image parity has no native dependency.</summary>
    private sealed class PngImage
    {
        private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

        public PngImage(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[checked(width * height * 4)];
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] Pixels { get; }

        public static PngImage Decode(byte[] png)
        {
            if (!png.AsSpan(0, Math.Min(Signature.Length, png.Length)).SequenceEqual(Signature))
            {
                throw new InvalidDataException("The screenshot is not a PNG image.");
            }

            var at = Signature.Length;
            var width = 0;
            var height = 0;
            var bitDepth = 0;
            var colourType = 0;
            var interlace = 0;
            using var compressed = new MemoryStream();
            while (at + 12 <= png.Length)
            {
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at, 4)));
                var type = Encoding.ASCII.GetString(png, at + 4, 4);
                var dataAt = at + 8;
                if (dataAt + length + 4 > png.Length) throw new InvalidDataException("A PNG chunk is truncated.");
                var data = png.AsSpan(dataAt, length);
                switch (type)
                {
                    case "IHDR":
                        if (length != 13) throw new InvalidDataException("The PNG header is invalid.");
                        width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                        height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                        bitDepth = data[8];
                        colourType = data[9];
                        interlace = data[12];
                        break;
                    case "IDAT":
                        compressed.Write(data);
                        break;
                    case "IEND":
                        at = png.Length;
                        continue;
                }

                at = dataAt + length + 4;
            }

            if (width <= 0 || height <= 0 || bitDepth != 8 || colourType is not (2 or 6) || interlace != 0)
            {
                throw new InvalidDataException("Only non-interlaced 8-bit RGB/RGBA PNGs are supported.");
            }

            var channels = colourType == 6 ? 4 : 3;
            var rowBytes = checked(width * channels);
            var encoded = new byte[checked((rowBytes + 1) * height)];
            compressed.Position = 0;
            using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
            {
                var read = 0;
                while (read < encoded.Length)
                {
                    var count = inflater.Read(encoded, read, encoded.Length - read);
                    if (count == 0) throw new InvalidDataException("The PNG image data is truncated.");
                    read += count;
                }
            }

            var image = new PngImage(width, height);
            var previous = new byte[rowBytes];
            var current = new byte[rowBytes];
            for (var y = 0; y < height; y++)
            {
                var source = encoded.AsSpan(y * (rowBytes + 1), rowBytes + 1);
                var filter = source[0];
                source[1..].CopyTo(current);
                Unfilter(current, previous, filter, channels);
                for (var x = 0; x < width; x++)
                {
                    var sourceAt = x * channels;
                    var destinationAt = (y * width + x) * 4;
                    image.Pixels[destinationAt] = current[sourceAt];
                    image.Pixels[destinationAt + 1] = current[sourceAt + 1];
                    image.Pixels[destinationAt + 2] = current[sourceAt + 2];
                    image.Pixels[destinationAt + 3] = channels == 4 ? current[sourceAt + 3] : (byte)255;
                }

                (previous, current) = (current, previous);
            }

            return image;
        }

        public byte[] Encode()
        {
            var scanlines = new byte[checked((Width * 4 + 1) * Height)];
            for (var y = 0; y < Height; y++)
            {
                var row = y * (Width * 4 + 1);
                scanlines[row] = 0;
                Pixels.AsSpan(y * Width * 4, Width * 4).CopyTo(scanlines.AsSpan(row + 1));
            }

            using var compressed = new MemoryStream();
            using (var deflater = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflater.Write(scanlines);
            }

            using var png = new MemoryStream();
            png.Write(Signature);
            Span<byte> header = stackalloc byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)Width));
            BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)Height));
            header[8] = 8;
            header[9] = 6;
            WriteChunk(png, "IHDR", header);
            WriteChunk(png, "IDAT", compressed.ToArray());
            WriteChunk(png, "IEND", []);
            return png.ToArray();
        }

        private static void Unfilter(byte[] row, byte[] previous, int filter, int bytesPerPixel)
        {
            if (filter == 0) return;
            if (filter is < 1 or > 4) throw new InvalidDataException($"Unsupported PNG filter {filter}.");
            for (var i = 0; i < row.Length; i++)
            {
                var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : (byte)0;
                var above = previous[i];
                var upperLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : (byte)0;
                row[i] = filter switch
                {
                    1 => unchecked((byte)(row[i] + left)),
                    2 => unchecked((byte)(row[i] + above)),
                    3 => unchecked((byte)(row[i] + ((left + above) / 2))),
                    _ => unchecked((byte)(row[i] + Paeth(left, above, upperLeft))),
                };
            }
        }

        private static byte Paeth(byte left, byte above, byte upperLeft)
        {
            var estimate = left + above - upperLeft;
            var leftDistance = Math.Abs(estimate - left);
            var aboveDistance = Math.Abs(estimate - above);
            var upperLeftDistance = Math.Abs(estimate - upperLeft);
            return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
                ? left
                : aboveDistance <= upperLeftDistance ? above : upperLeft;
        }

        private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
            output.Write(length);
            var typeBytes = Encoding.ASCII.GetBytes(type);
            output.Write(typeBytes);
            output.Write(data);
            Span<byte> crc = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes, data));
            output.Write(crc);
        }

        private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            var crc = 0xffffffffu;
            foreach (var value in type) crc = UpdateCrc(crc, value);
            foreach (var value in data) crc = UpdateCrc(crc, value);
            return ~crc;
        }

        private static uint UpdateCrc(uint crc, byte value)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
            }

            return crc;
        }
    }
}
