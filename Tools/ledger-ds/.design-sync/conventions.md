# Building with the Gangsters 1987 ledger

This DS is a 1987 mob ledger: a typed manila file on a walnut desk (the
**Paper** components) and the terminal frame it grew into (the **Terminal**
components). The **HUD** pair is pixel street-chrome. Fidelity rules:

## Setup

No provider is needed. Every component styles itself from `styles.css`'s
token closure and ships its five fonts (Lekton, IBM Plex Mono, PT Serif,
Oswald, Silkscreen). One hard rule: **never lay ledger pieces on pure white**.
Compose on `<Desk>` (the walnut desk with lamp + vignette) or on a div with
`background: var(--lg-ground)` (the sheet ground) / `var(--lg-panel)` (a
panel face). Paper components (`PaperSheet`, `Polaroid`, `Stamp`,
`TelexSlip`, `StickyNote`) look right on `<Desk>` or `var(--lg-ground)`;
Terminal components (`Panel`, `Key`, `Meter`, `Pips`, `Segmented`,
`StatusChip`, `LeaderRow`, `PageHead`, `SectionHead`, `Text`, `Mark`) live on
`var(--lg-ground)` or inside a `Panel`.

## Styling idiom: tokens, not classes

Style your own layout glue with inline styles or your own CSS **using the
`--lg-*` custom properties** — never invent colors and never hand-write the
DS's internal `lg-*` class names. The families:

- Ground & panels: `--lg-ground`, `--lg-panel`, `--lg-panel-band`, `--lg-panel-dark`, `--lg-head` (dark band), `--lg-head-ink`, `--lg-head-cream`
- Ink weights (light sheets): `--lg-ink2`, `--lg-body`, `--lg-muted`, `--lg-label`, `--lg-faint`
- The three readings: `--lg-red` (bad), `--lg-amber` (at the limit), `--lg-green` (good); `--lg-paper-blue` = true on paper, not on the street
- Paper stocks: `--lg-paper`, `--lg-card`, `--lg-printout`, `--lg-index-card`, `--lg-slip`, `--lg-newsprint`, `--lg-greenbar`, `--lg-carbon`, `--lg-manila` (each with a darker `-low`/`-deep` stop)
- Typewriter inks (paper edition): `--lg-ink`, `--lg-ink-soft`, `--lg-ink-dim`, `--lg-ink-label`, `--lg-red-pen`, `--lg-green-ok`, `--lg-ballpoint`
- Dark rail/chrome: `--lg-rail`, `--lg-rail-label`, `--lg-rail-value`, `--lg-rail-green`, `--lg-rail-red`, `--lg-rail-gold`, `--lg-rail-amber`, `--lg-rail-safe-gold`, `--lg-rail-trough`
- Rules: `--lg-rule`, `--lg-hair`, `--lg-dotted`, `--lg-sheet-rule`
- Fonts: `--lg-font-mono` (figures, labels — anything measured), `--lg-font-serif` (copy a reader READS), `--lg-font-cond` (Oswald — names, headings, stamps), `--lg-font-type` (Lekton typewriter), `--lg-font-pixel` (HUD only)

## Voice rules the game enforces

- Labels/verbs are set in caps BY the components — pass normal text, they uppercase.
- Money prints exact: `$1,247`, `-$300` — never `$1.2k`.
- A bounded reading is a **count of marks** (`Pips`, `StepBar`, `Stars` in half-steps 0–10), never a percentage bar; `Meter` is for capacity and always says in words what the figure means.
- `Key`: `dark` = the doing verb, `red` = the verb that cannot be taken back, `outline` = does not commit, `ghost` = undoes.
- Use `Text` variants (`kicker`/`name`/`label`/`figure`/`copy`) instead of styling raw text.

## The idiomatic snippet

```tsx
<div style={{ background: 'var(--lg-ground)', padding: 24 }}>
  <PageHead title="The Outfit" sub="11 men on the books · wages $2,140 a week" />
  <div style={{ height: 16 }} />
  <Panel head="This Week" headRight="Day 41" width={300}>
    <LeaderRow label="Dues collected" figure="$3,615" tone="green" />
    <div style={{ height: 8 }} />
    <Meter label="Men on the books" current={7} maximum={10} unit="man" plural="men" />
    <div style={{ height: 12 }} />
    <Key label="Commit" variant="red" />
  </Panel>
</div>
```

Full token list: `styles.css` → `_ds_bundle.css` (`:root` block). Per-component
API: each `components/<group>/<Name>/<Name>.d.ts` + `.prompt.md`.
