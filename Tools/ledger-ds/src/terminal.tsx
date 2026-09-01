import type { CSSProperties, ReactNode } from 'react';

/** The three readings a figure can take, plus the sheet's own inks. */
export type Tone = 'ink' | 'red' | 'amber' | 'green' | 'muted' | 'paperBlue';

const toneVar: Record<Tone, string> = {
  ink: 'var(--lg-ink2)',
  red: 'var(--lg-red)',
  amber: 'var(--lg-amber)',
  green: 'var(--lg-green)',
  muted: 'var(--lg-muted)',
  paperBlue: 'var(--lg-paper-blue)',
};

export interface PanelProps {
  /** The dark head band's label - what the panel IS. Omit for a bare panel. */
  head?: string;
  /** A stamp or a count held to the head band's right. */
  headRight?: string;
  /** Panel face colour - defaults to the sheet's Panel stock. */
  face?: string;
  width?: number | string;
  style?: CSSProperties;
  children?: ReactNode;
}

/**
 * A panel: a flat card laid on the sheet under the design's two-layer drop
 * shadow - a tight contact shadow and a wide soft one. No texture, no tilt:
 * what makes it read as laid ON something is the shadow and nothing else.
 * Give it a `head` for the dark band that names it.
 */
export function Panel({ head, headRight, face, width, style, children }: PanelProps) {
  return (
    <div
      className="lg-panel lg-root"
      style={{ width, ...(face ? { background: face } : null), ...style }}
    >
      {head != null && (
        <div className="lg-panel-head">
          <span className="lg-panel-head-label">{head}</span>
          {headRight != null && <span className="lg-panel-head-right">{headRight}</span>}
        </div>
      )}
      <div className="lg-panel-body">{children}</div>
    </div>
  );
}

export interface PageHeadProps {
  /** The page's name, set in the condensed gothic. */
  title: string;
  /** The line of mono under it that says what the page IS. */
  sub?: string;
  style?: CSSProperties;
}

/**
 * A page's head: its name in the condensed gothic, the mono line under it,
 * and the heavy 3px rule that closes the pair.
 */
export function PageHead({ title, sub, style }: PageHeadProps) {
  return (
    <div className="lg-root" style={style}>
      <div className="lg-pagehead-title">{title}</div>
      {sub != null && <div className="lg-pagehead-sub">{sub}</div>}
      <div className="lg-pagehead-rule" />
    </div>
  );
}

export interface SectionHeadProps {
  /** The section's numbered title - the design's "I. CHAIN OF COMMAND". */
  title: string;
  /** An aside held to the right margin over the hairline. */
  aside?: string;
  style?: CSSProperties;
}

/** A numbered section head inside a page, closed with a single hairline. */
export function SectionHead({ title, aside, style }: SectionHeadProps) {
  return (
    <div className="lg-root" style={style}>
      <div className="lg-section">
        <span className="lg-section-title">{title}</span>
        {aside != null && <span className="lg-section-aside">{aside}</span>}
      </div>
      <div className="lg-section-rule" />
    </div>
  );
}

export interface TextProps {
  /**
   * The book's voices: `label` mono small-caps over a value; `figure` bold
   * mono for anything measured; `name` the condensed gothic a reader scans;
   * `copy` the serif a reader READS; `kicker` the letter-spaced caps label.
   */
  variant?: 'label' | 'figure' | 'name' | 'copy' | 'kicker';
  tone?: Tone;
  /** Serif copy set in italic - the hand in the margin. */
  italic?: boolean;
  style?: CSSProperties;
  children?: ReactNode;
}

/** One line of the ledger's type, in the five voices the book owns. */
export function Text({ variant = 'copy', tone, italic, style, children }: TextProps) {
  return (
    <div
      className={`lg-root lg-text-${variant}`}
      style={{
        ...(tone ? { color: toneVar[tone] } : null),
        ...(italic ? { fontStyle: 'italic' } : null),
        ...style,
      }}
    >
      {children}
    </div>
  );
}

export interface KeyProps {
  /** The verb, printed in mono caps. */
  label: string;
  /**
   * How the key reads. `dark` is the design's filled key with the hard shadow
   * under it; `red` is the verb that cannot be taken back; `outline` a
   * hairline box for the verb that does not commit; `ghost` bare red type
   * for the verb that UNDOES something.
   */
  variant?: 'dark' | 'outline' | 'ghost' | 'red';
  disabled?: boolean;
  onClick?: () => void;
  style?: CSSProperties;
}

/**
 * A key: flat, hard-edged, with the design's drop shadow under the filled
 * ones. Nothing rounds, nothing gradients.
 */
export function Key({ label, variant = 'outline', disabled, onClick, style }: KeyProps) {
  return (
    <button
      type="button"
      className={`lg-key lg-key--${variant}`}
      disabled={disabled}
      onClick={onClick}
      style={style}
    >
      {label}
    </button>
  );
}

export interface SegmentedProps {
  /** The run's labels - one question, one answer. */
  labels: string[];
  /** Which cell is struck dark. */
  active: number;
  onPick?: (index: number) => void;
  style?: CSSProperties;
}

/**
 * A segmented run: a single hairline box divided into butted cells with the
 * chosen cell struck dark. Separate keys say "here are four things you may
 * press"; a segmented bar says "the sheet is showing this one of these".
 */
export function Segmented({ labels, active, onPick, style }: SegmentedProps) {
  return (
    <div className="lg-segmented lg-root" style={style}>
      {labels.map((label, i) => (
        <button
          key={label}
          type="button"
          className={`lg-segment${i === active ? ' lg-segment--active' : ''}`}
          onClick={onPick ? () => onPick(i) : undefined}
        >
          {label}
        </button>
      ))}
    </div>
  );
}

export interface StatusChipProps {
  /** The word the form is stamped with - ACTIVE, JAILED, OVERDUE. */
  word: string;
  /** The chip's fill; the word always prints in cream. */
  tone?: 'red' | 'amber' | 'green' | 'filed' | 'ink' | 'lieutenant' | 'boss';
  style?: CSSProperties;
}

const statusVar: Record<NonNullable<StatusChipProps['tone']>, string> = {
  red: 'var(--lg-red)',
  amber: 'var(--lg-amber)',
  green: 'var(--lg-green)',
  filed: 'var(--lg-filed)',
  ink: 'var(--lg-head)',
  lieutenant: 'var(--lg-lieutenant)',
  boss: 'var(--lg-boss)',
};

/**
 * A status chip: a filled block with the word set in cream mono caps - what
 * the terminal sheet says instead of a rubber stamp.
 */
export function StatusChip({ word, tone = 'ink', style }: StatusChipProps) {
  return (
    <span className="lg-status lg-root" style={{ background: statusVar[tone], ...style }}>
      {word}
    </span>
  );
}

export interface MeterProps {
  /** What the meter measures - MEN ON THE BOOKS, SAFE. */
  label: string;
  current: number;
  maximum: number;
  /** The unit, singular then plural - "man", "men". Feeds the plain-English note. */
  unit?: string;
  plural?: string;
  /** Set on a dark rail so the trough and inks swap to the rail's palette. */
  dark?: boolean;
  style?: CSSProperties;
}

/**
 * A capacity meter: the label, the figure it comes to, the trough with its
 * fill, and the line of plain English under it that says what the figure
 * MEANS. The last is the point - a ratio nobody can act on is not a readout.
 */
export function Meter({
  label, current, maximum, unit = 'more', plural, dark, style,
}: MeterProps) {
  const over = maximum > 0 && current > maximum;
  const full = maximum > 0 && current >= maximum;
  const colour = over
    ? (dark ? 'var(--lg-boss)' : 'var(--lg-red)')
    : full
      ? 'var(--lg-amber)'
      : (dark ? 'var(--lg-head-cream)' : 'var(--lg-ink2)');
  const room = maximum - current;
  const note = over
    ? `OVER BY ${current - maximum} · the outfit will not add more`
    : current === maximum
      ? `at the limit · no room for another ${unit}`
      : `${room} more ${room === 1 ? unit : plural ?? unit} will fit`;
  const fraction = maximum > 0 ? Math.min(1, current / maximum) : 0;
  return (
    <div className={`lg-root${dark ? ' lg-meter--dark' : ''}`} style={style}>
      <div className="lg-meter-row">
        <span
          className="lg-text-label"
          style={dark ? { color: 'var(--lg-head-dim)' } : undefined}
        >
          {label}
        </span>
        <span className="lg-text-figure" style={{ fontSize: '11.6px', color: colour }}>
          {current} / {maximum}
        </span>
      </div>
      <div className="lg-meter-trough">
        <div className="lg-meter-fill" style={{ width: `${fraction * 100}%`, background: colour }} />
      </div>
      <div
        className="lg-meter-note"
        style={over ? { color: dark ? 'var(--lg-boss)' : 'var(--lg-red)', fontWeight: 700 } : undefined}
      >
        {note}
      </div>
    </div>
  );
}

export interface PipsProps {
  /** How many pips the reading has room for. */
  total: number;
  /** How many are struck. */
  filled: number;
  /** The struck pip's colour - a token or any CSS colour. */
  color?: string;
  /** The unstruck pip's colour. The design gives it a colour of its OWN so a
   *  row of pips can be counted on a dark rail. */
  empty?: string;
  /** Pip edge in px. */
  size?: number;
  style?: CSSProperties;
}

/**
 * A run of hard square pips - a reading with a ceiling. The design's meter is
 * a row of blocks, never a bar with a rounded end: six of ten reads as six
 * marks, not as a bar that happens to stop somewhere.
 */
export function Pips({
  total, filled, color = 'var(--lg-rail-amber)', empty = 'var(--lg-pip-empty)',
  size = 9, style,
}: PipsProps) {
  return (
    <span className="lg-pips" style={style}>
      {Array.from({ length: total }, (_, i) => (
        <span
          key={i}
          className="lg-pip"
          style={{ width: size, height: size, background: i < filled ? color : empty }}
        />
      ))}
    </span>
  );
}

export interface LeaderRowProps {
  /** The label on the left. */
  label: string;
  /** The figure that answers it, held to the right margin. */
  figure: string;
  tone?: Tone;
  style?: CSSProperties;
}

/**
 * A label, the dotted leader, and the figure that answers it. The dots are
 * stronger than a hairline on purpose - half of a dotted rule is gaps.
 */
export function LeaderRow({ label, figure, tone = 'ink', style }: LeaderRowProps) {
  return (
    <div className="lg-leader lg-root" style={style}>
      <span className="lg-text-label">{label}</span>
      <span className="lg-leader-dots" />
      <span className="lg-text-figure" style={{ color: toneVar[tone] }}>{figure}</span>
    </div>
  );
}

export interface MarkProps {
  /** `paper` is the hatched square beside a name that is only on PAPER;
   *  `street` the solid square beside one that is true on the street. */
  kind: 'paper' | 'street';
  /** The owning gang's colour. */
  color?: string;
  size?: number;
  style?: CSSProperties;
}

/** The paper-vs-street mark the block ledger prints beside a name. */
export function Mark({ kind, color = 'var(--lg-signature)', size = 12, style }: MarkProps) {
  return (
    <span
      className={`lg-mark lg-mark--${kind}`}
      style={{ color, width: size, height: size, ...style }}
    />
  );
}
