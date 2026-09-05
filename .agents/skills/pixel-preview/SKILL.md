---
name: pixel-preview
description: "Compare Gridlet designer Preview with the actual published/public component rendering, diagnose visual drift, and add deterministic pixel or structural parity regression coverage."
---

# Pixel Preview

Use this skill when a Gridlet component looks different in Preview, on its published route, or when a visual regression test needs to prove that the two surfaces agree. The goal is evidence from the actual rendered pixels; DOM and computed-style checks explain failures but cannot replace the screenshot comparison.

## Establish the comparison

1. Read `AGENTS.md`, record `git status --short`, and preserve unrelated or user-owned changes. Do not stage, commit, push, or publish unless explicitly asked.
2. Identify the exact component, route, isolate mode, theme, initial data, and interaction state being compared. Compare the real designer Preview page with the real public route; do not render both assertions from the same DOM or mock the public renderer.
3. Use the same Chromium browser context for both pages and fix every raster-affecting input: viewport size, device scale factor/DPR, color scheme/theme, locale/timezone, fonts, storage/cookies, and test data. Keep the component root's available width and height identical. Make the public URL and API base explicit when route configuration is involved.

## Stabilize and capture

Before either capture, wait for the exact root locator to be visible, await `document.fonts.ready`, wait for incomplete images to load or error, disable animation/transition/caret effects, and wait at least two `requestAnimationFrame` passes. Reset the root's scroll position. Keep this stabilization CSS limited to nondeterministic motion and caret painting; never hide, restyle, or remove the component's content to make a comparison pass.

Capture the exact component-root locator, not the full viewport or surrounding designer/public shell. Use CSS-pixel scale and disabled animations. Ensure both screenshots are taken at the same state immediately after stabilization. In Gridlet, reuse and extend `tests/Gridlet.BrowserTests/BrowserPixelParity.cs` when it covers the needed operation instead of inventing a second comparator.

## Compare pixels strictly

Decode both PNGs to RGBA and compare every pixel. Require exact image dimensions. Track differing-pixel count and ratio, maximum channel delta, and difference bounds. A small antialias budget is allowed only for browser rasterization (the established default is at most 0.1% different pixels and a maximum channel delta of 32); do not increase tolerances, use a vague similarity score, blur, crop away a mismatch, or mask a failing region. A shifted control, missing border, wrong font, overflow, or incorrect color must fail.

On failure, write `preview.png`, `published.png`, `diff.png`, and readable metrics (dimensions, counts, ratio, maximum delta, bounds, and thresholds) to a temporary parity-artifact directory and report its path. Use the artifacts to locate the real rendering defect before changing the test.

Add a focused browser regression test for each fixed drift. Assert high-signal geometry and computed styles alongside the pixel result: root dimensions, control bounding boxes, font/color/background/border/overflow, values, and relevant scroll dimensions. These assertions should make failures diagnosable, not loosen the pixel gate.

## Isolated components

An isolated component intentionally drops Gridlet's styling, and native controls can rasterize differently across documents. For isolate-on, use structural parity in addition to visual diagnostics: match semantic control names/types and order, relative boxes within a small subpixel bound (the established default is 0.5px), text/values/checked/disabled/selection state, and client/scroll dimensions. Do not claim pixel-perfect parity for isolated native controls unless the captured pixels actually pass; structural parity is the explicit fallback for this mode.

## Validate

Run the focused parity test first, then the relevant broader browser/component suite and `dotnet test --configuration Release` when practical. Run `git diff --check`, inspect the final diff, and verify no staged files or unrelated changes were altered. Summarize whether the public route now matches, the exact comparison thresholds/results, artifact locations for any failure, and any remaining intentional isolate-mode differences.
