# design-sync notes — @gangsters/ledger-1987

This package is NOT a pre-existing web DS: it is a hand-authored React port of the
game's Unity ledger look, built for this sync. Sources of truth are the game's C#:
`Assets/Scripts/UI/LedgerStyle.cs` (tokens, procedural textures),
`Assets/Scripts/UI/LedgerKit.cs` (paper edition), `Assets/Scripts/UI/LedgerV2.cs`
(terminal edition), `Assets/Scripts/UI/UiSkin.cs` (Waste No Space 9-patch, star bake).

- Build: `npm run build` in `Tools/ledger-ds` (extract-patches → esbuild → tsc).
  Always run it BEFORE the converter — `dist/ledger.css` is assembled from
  `src/{fonts,tokens,components}.css` and goes stale otherwise (an old Arial Narrow
  fallback once kept `[FONT_MISSING]` firing for exactly this reason).
- `tools/extract-patches.mjs` decodes `assets/WasteNoSpaceGui.png` (CC0, flatus.itch.io)
  and regenerates `src/patches.ts` (9-patch data URIs). The pack's chrome is DARK —
  HUD text must be cream (`--lg-hud-cream`), not ink.
- Fonts are the game's own OFL ttfs copied from
  `Assets/Fonts/Ledger1987/Resources/Ledger1987/`; licenses in `licenses/`.
- TMP optical factors are baked into the CSS px sizes (mono ×0.831, condensed ×0.864,
  serif ×1.017, typewriter ×1.082) — see the header of `src/components.css`.
- Groups come from `.design-sync/docs/*.md` frontmatter stubs (Terminal/Paper/HUD)
  via `docsDir`; the stubs are category-only, prompt bodies stay synthesized.
- Overrides: most previews are `cardMode: column` (sheets are wide); `Desk` is
  `single` (the hero composition).

## Known render warns
- none (validate exits clean, 24/24).

## Re-sync risks
- The C# tokens can drift: if `LedgerStyle.cs`/`LedgerV2.cs` colors change, nothing
  breaks here — the port silently goes stale. Diff hex values against those files
  when the game's ledger look changes.
- `WasteNoSpaceGui.png` and the font ttfs are copied, not referenced; re-copy if the
  Unity assets change.
- Playwright chromium lives in `~/Library/Caches/ms-playwright` (headless shell 151 /
  playwright v1234 pin); a fresh machine needs the §4.1 install-or-ask step again.
