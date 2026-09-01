import type { CSSProperties } from 'react';
import { useId } from 'react';

// The ten-vertex star polygon UiSkin bakes, point up: outer radius 0.96,
// inner 0.44, first vertex at 90 degrees. SVG's y runs down, so y is flipped.
function starPoints(scale: number): string {
  const pts: string[] = [];
  for (let i = 0; i < 10; i++) {
    const angle = ((90 + i * 36) * Math.PI) / 180;
    const radius = (i % 2 === 0 ? 0.96 : 0.44) * scale;
    pts.push(`${(Math.cos(angle) * radius).toFixed(4)},${(-Math.sin(angle) * radius).toFixed(4)}`);
  }
  return pts.join(' ');
}

const OUTER = starPoints(1);
const INNER = starPoints(0.78);

type Fill = 'full' | 'half' | 'empty';

function Star({ fill, size }: { fill: Fill; size: number }) {
  const id = useId();
  const gold = (
    <g clipPath={fill === 'half' ? `url(#${id}-clip)` : undefined}>
      <polygon points={OUTER} fill="var(--lg-gold-rim)" />
      <polygon points={INNER} fill={`url(#${id}-gold)`} />
      <circle cx="-0.22" cy="-0.30" r="0.24" fill={`url(#${id}-glint)`} />
    </g>
  );
  const empty = (
    <g>
      <polygon points={OUTER} fill="rgba(140,153,140,0.55)" />
      <polygon points={INNER} fill="rgba(184,199,184,0.35)" />
    </g>
  );
  return (
    <svg width={size} height={size} viewBox="-1 -1 2 2" aria-hidden>
      <defs>
        <linearGradient id={`${id}-gold`} x1="0" y1="1" x2="0" y2="-1"
          gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="var(--lg-gold-bottom)" />
          <stop offset="1" stopColor="var(--lg-gold-top)" />
        </linearGradient>
        <radialGradient id={`${id}-glint`}>
          <stop offset="0" stopColor="#fffff0" stopOpacity="0.95" />
          <stop offset="1" stopColor="#fffff0" stopOpacity="0" />
        </radialGradient>
        {fill === 'half' && (
          <clipPath id={`${id}-clip`}>
            <rect x="-1" y="-1" width="1" height="2" />
          </clipPath>
        )}
      </defs>
      {fill !== 'full' && empty}
      {fill !== 'empty' && gold}
    </svg>
  );
}

export interface StarsProps {
  /** The rating in HALF steps, 0-10: 7 prints three and a half stars. The
   *  ledger's attributes are always dealt in halves. */
  halfSteps: number;
  /** Edge of one star in px. */
  size?: number;
  style?: CSSProperties;
}

/**
 * Five gold stars - the star stickers a 1987 personnel form gets: a vertical
 * gold gradient with a glint high on the left, a dark rim, and a pen-outlined
 * empty for the slots not yet earned. Halves only when earned.
 */
export function Stars({ halfSteps, size = 19, style }: StarsProps) {
  return (
    <span className="lg-stars" style={style}>
      {Array.from({ length: 5 }, (_, slot) => (
        <Star
          key={slot}
          size={size}
          fill={halfSteps >= (slot + 1) * 2 ? 'full' : halfSteps === slot * 2 + 1 ? 'half' : 'empty'}
        />
      ))}
    </span>
  );
}
