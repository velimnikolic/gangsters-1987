/**
 * The Gangsters 1987 ledger design system.
 *
 * Two editions share one drawer: the PAPER edition (a typed manila file on a
 * walnut desk - sheets, telex slips, rubber stamps, Polaroids, gold stars)
 * and the TERMINAL edition (the 1987 frame it grew into - flat panels, dark
 * head bands, keys, pips, meters). The HUD pieces dress the street chrome in
 * the CC0 "Waste No Space" 9-patch sheet.
 *
 * Import './ledger.css' (tokens + fonts + component chrome) once per app.
 */
export {
  Panel, PageHead, SectionHead, Text, Key, Segmented, StatusChip,
  Meter, Pips, LeaderRow, Mark,
} from './terminal';
export type {
  PanelProps, PageHeadProps, SectionHeadProps, TextProps, KeyProps,
  SegmentedProps, StatusChipProps, MeterProps, PipsProps, LeaderRowProps,
  MarkProps, Tone,
} from './terminal';

export {
  PaperSheet, TelexSlip, Stamp, Polaroid, Plate, StickyNote, StepBar,
  TapeButton, Highlight, Desk,
} from './paper';
export type {
  PaperSheetProps, TelexSlipProps, StampProps, PolaroidProps, PlateProps,
  StickyNoteProps, StepBarProps, TapeButtonProps, HighlightProps, DeskProps,
  Stock,
} from './paper';

export { Stars } from './stars';
export type { StarsProps } from './stars';

export { HudPanel, HudButton } from './hud';
export type { HudPanelProps, HudButtonProps } from './hud';
