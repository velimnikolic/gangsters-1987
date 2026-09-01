import type { CSSProperties, ReactNode } from 'react';

/** The stocks the stationery drawer holds, top colour and the fall's low stop. */
const stocks = {
  paper: ['var(--lg-paper)', 'var(--lg-paper-deep)'],
  card: ['var(--lg-card)', 'var(--lg-card-low)'],
  printout: ['var(--lg-printout)', 'var(--lg-printout-low)'],
  indexCard: ['var(--lg-index-card)', 'var(--lg-index-card-low)'],
  slip: ['var(--lg-slip)', 'var(--lg-slip-low)'],
  newsprint: ['var(--lg-newsprint)', 'var(--lg-newsprint-low)'],
  greenbar: ['var(--lg-greenbar)', 'var(--lg-greenbar-low)'],
  carbon: ['var(--lg-carbon)', 'var(--lg-carbon-low)'],
  manila: ['var(--lg-manila)', 'var(--lg-manila-low)'],
} as const;

export type Stock = keyof typeof stocks;

export interface PaperSheetProps {
  /** Which stock the sheet is cut from - card, printout, index card, telex
   *  slip, newsprint, accountant's greenbar, a carbon copy, or the manila
   *  shell itself. */
  stock?: Stock;
  /** Degrees off square - paper nobody squared up. Small values: -1.5 to 1.5. */
  tilt?: number;
  /** Lay the aging pass over the stock: lamplight, one crease, foxing blooms. */
  aging?: boolean;
  /** Punched holes down the left edge - a sheet torn out of a ring binder. */
  punched?: boolean;
  /** The ring a coffee cup left on the top right. */
  coffeeRing?: boolean;
  /** Blue rules at the ledger's pitch with the red margin line. */
  ruled?: boolean;
  /** Greenbar banding - the stripe an accounting sheet prints every other line. */
  banded?: boolean;
  width?: number | string;
  style?: CSSProperties;
  children?: ReactNode;
}

/**
 * A sheet of the ledger's stock laid on the page: gradient fall, paper grain,
 * two shadows (a tight contact one and a wide cast one), the cut edge and the
 * lit top edge. Flags add what the stationery drawer offers: aging, punched
 * holes, a coffee ring, blue ruling, greenbar banding.
 */
export function PaperSheet({
  stock = 'card', tilt = 0, aging, punched, coffeeRing, ruled, banded,
  width, style, children,
}: PaperSheetProps) {
  const [top, low] = stocks[stock];
  return (
    <div
      className="lg-sheet lg-grain lg-root"
      style={{
        width,
        background: `linear-gradient(180deg, ${top} 0%, ${low} 100%)`,
        ...(tilt ? { transform: `rotate(${tilt}deg)` } : null),
        ...style,
      }}
    >
      {aging && (
        <div className="lg-aging" aria-hidden>
          <div className="lg-aging-light" />
          <div className="lg-aging-crease" />
          <div className="lg-foxing" style={{ left: '10%', top: '58%', width: 96, height: 88 }} />
          <div className="lg-foxing" style={{ left: '72%', top: '16%', width: 68, height: 62 }} />
          <div className="lg-foxing" style={{ left: '40%', top: '80%', width: 120, height: 70 }} />
        </div>
      )}
      {punched && <div className="lg-punches" style={{ left: 6 }} aria-hidden />}
      {coffeeRing && (
        <div
          className="lg-coffee"
          style={{ right: 18, top: 10, width: 66, height: 66 }}
          aria-hidden
        />
      )}
      <div
        className={`lg-sheet-body${ruled ? ' lg-ruled' : ''}${banded ? ' lg-greenbar-bands' : ''}`}
        style={punched ? { paddingLeft: 34 } : undefined}
      >
        {children}
      </div>
    </div>
  );
}

export interface TelexSlipProps {
  /** Who the wire came from - WIRE · DOWNTOWN. */
  source: string;
  /** When it printed - 02:14. */
  time: string;
  style?: CSSProperties;
  width?: number | string;
  children?: ReactNode;
}

/**
 * A telex slip: cream stock, a red rule down its left edge, the source and
 * the time in small caps across the head, the message under. What came in
 * over the night - never a thing to press.
 */
export function TelexSlip({ source, time, width, style, children }: TelexSlipProps) {
  return (
    <div className="lg-slip lg-grain lg-root" style={{ width, ...style }}>
      <div className="lg-slip-head">
        <span className="lg-slip-source">{source}</span>
        <span className="lg-slip-time">{time}</span>
      </div>
      <div className="lg-slip-body">{children}</div>
    </div>
  );
}

export interface StampProps {
  /** The word the block presses - OVERDUE, PAID, CLOSED. */
  word: string;
  /** Degrees off square. A stamp nobody would cant on purpose sits at -7.4. */
  tilt?: number;
  style?: CSSProperties;
}

/**
 * A rubber stamp: a double-ruled box and a letter-spaced word, canted off
 * square. Deliberately UNEVEN - one side of the frame takes more ink than the
 * other, because a rubber stamp is a hand pressing a wet block onto paper.
 */
export function Stamp({ word, tilt = -7.4, style }: StampProps) {
  return (
    <span className="lg-stamp" style={{ transform: `rotate(${tilt}deg)`, ...style }}>
      {word}
    </span>
  );
}

export interface PolaroidProps {
  /** The initials printed on the unexposed dark until a print lands. */
  initials: string;
  /** The photograph itself, when one has landed. */
  src?: string;
  /** The caption typed on the bottom lip. */
  caption?: string;
  /** Degrees off square. */
  tilt?: number;
  /** Edge of the square photo area in px. */
  photoSize?: number;
  style?: CSSProperties;
}

/**
 * A Polaroid: white border, the print inside, a wider strip at the bottom for
 * the caption. The initials are the placeholder AND the fallback - the print
 * covers them when the studio lands one.
 */
export function Polaroid({
  initials, src, caption, tilt = 0, photoSize = 96, style,
}: PolaroidProps) {
  return (
    <span
      className="lg-polaroid lg-root"
      style={{ ...(tilt ? { transform: `rotate(${tilt}deg)` } : null), ...style }}
    >
      <span className="lg-polaroid-photo" style={{ width: photoSize, height: photoSize }}>
        <span className="lg-polaroid-initials" style={{ fontSize: photoSize * 0.29 }}>
          {initials}
        </span>
        {src && <img className="lg-polaroid-print" src={src} alt="" />}
      </span>
      {caption != null && <span className="lg-polaroid-caption">{caption}</span>}
    </span>
  );
}

export interface PlateProps {
  /** The part number typed under the plate. */
  caption?: string;
  /** The photograph, when the file owns one. */
  src?: string;
  width?: number;
  height?: number;
  style?: CSSProperties;
}

/**
 * A printer's plate: the halftone block-out that stands where a picture goes
 * - two offset dot screens over a lit gradient, the burnt edge of an
 * exposure. What a photograph reproduced in a typed file in 1987 actually was.
 */
export function Plate({ caption, src, width = 140, height = 110, style }: PlateProps) {
  return (
    <span className="lg-plate lg-root" style={{ display: 'inline-block', width, height, ...style }}>
      {src && <img className="lg-plate-print" src={src} alt="" />}
      {caption != null && <span className="lg-plate-caption">{caption}</span>}
    </span>
  );
}

export interface StickyNoteProps {
  /** Degrees off square - a note slapped on, not placed. */
  tilt?: number;
  width?: number | string;
  style?: CSSProperties;
  children?: ReactNode;
}

/** A yellow sticky note - the hover notes' paper. */
export function StickyNote({ tilt = 1.5, width, style, children }: StickyNoteProps) {
  return (
    <span
      className="lg-sticky lg-root"
      style={{ transform: `rotate(${tilt}deg)`, width, ...style }}
    >
      {children}
    </span>
  );
}

export interface StepBarProps {
  /** How many typed blocks the reading has room for. */
  steps: number;
  /** How many are struck. */
  filled: number;
  /** The struck block's colour. */
  color?: string;
  style?: CSSProperties;
}

/**
 * The paper edition's meter: a run of typed blocks, so many struck and the
 * rest hollow. It is a COUNT, never a percentage.
 */
export function StepBar({ steps, filled, color = 'var(--lg-ink)', style }: StepBarProps) {
  return (
    <span className="lg-stepbar" style={style}>
      {Array.from({ length: steps }, (_, i) => (
        <span
          key={i}
          className="lg-step"
          style={{ background: color, opacity: i < filled ? 1 : 0.22 }}
        />
      ))}
    </span>
  );
}

export interface TapeButtonProps {
  /** The verb, in cream condensed caps. */
  label: string;
  /** The verb that cannot be taken back. */
  red?: boolean;
  /** The same button with the fill taken away - a verb that UNDOES something. */
  outline?: boolean;
  disabled?: boolean;
  onClick?: () => void;
  style?: CSSProperties;
}

/**
 * The paper edition's action button: square, ink-black (or red for the verb
 * that commits), cream condensed caps, a hard 2-unit edge under it the way a
 * key on a desk toy sits proud.
 */
export function TapeButton({ label, red, outline, disabled, onClick, style }: TapeButtonProps) {
  return (
    <button
      type="button"
      className={`lg-tape${red ? ' lg-tape--red' : ''}${outline ? ' lg-tape--outline' : ''}`}
      disabled={disabled}
      onClick={onClick}
      style={style}
    >
      {label}
    </button>
  );
}

export interface HighlightProps {
  /** A clerk's green tick instead of the red one - a valid drop target. */
  green?: boolean;
  style?: CSSProperties;
  children?: ReactNode;
}

/**
 * The selected row: a wash of red across it and a heavy rule down its left
 * edge - a clerk's tick, not a UI selection.
 */
export function Highlight({ green, style, children }: HighlightProps) {
  return (
    <div className={`lg-highlight${green ? ' lg-highlight--green' : ''}`} style={style}>
      {children}
    </div>
  );
}

export interface DeskProps {
  /** Padding around the file on the desk. */
  padding?: number | string;
  style?: CSSProperties;
  children?: ReactNode;
}

/**
 * The walnut desk under everything: a three-stop fall out of the lamp's pool,
 * the warm glow off the ceiling fixture, and the vignette that closes the
 * corners of the room. Lay sheets and panels on it.
 */
export function Desk({ padding = 40, style, children }: DeskProps) {
  return (
    <div className="lg-desk lg-root" style={{ padding, ...style }}>
      {children}
    </div>
  );
}
