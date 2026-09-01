import { StickyNote } from '@gangsters/ledger-1987';

export const HoverNote = () => (
  <div style={{ background: 'var(--lg-card)', padding: 32 }}>
    <StickyNote width={190}>
      Ask Moretti about the second book under the till. Friday, before the
      round.
    </StickyNote>
  </div>
);

export const TwoNotes = () => (
  <div style={{ background: 'var(--lg-card)', padding: 32, display: 'flex', gap: 22 }}>
    <StickyNote width={160} tilt={-2}>The harbormaster wants his envelope EARLY.</StickyNote>
    <StickyNote width={160} tilt={3}>Nobody drives the boulevard through Sunday.</StickyNote>
  </div>
);
