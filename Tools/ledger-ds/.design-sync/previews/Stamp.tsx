import { Stamp } from '@gangsters/ledger-1987';

export const TheWords = () => (
  <div style={{ background: 'var(--lg-card)', padding: 32, display: 'flex', gap: 28, alignItems: 'center', flexWrap: 'wrap' }}>
    <Stamp word="Overdue" />
    <Stamp word="Paid" tilt={4} />
    <Stamp word="Closed" tilt={-3} />
  </div>
);

export const OnEvidence = () => (
  <div style={{ background: 'var(--lg-card)', padding: 32 }}>
    <Stamp word="Seen by the boss" tilt={-6} />
  </div>
);
