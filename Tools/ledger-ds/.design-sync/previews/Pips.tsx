import { Pips, Text } from '@gangsters/ledger-1987';

export const OnTheRail = () => (
  <div style={{ background: 'var(--lg-rail)', padding: 24, display: 'grid', gap: 12, width: 260 }}>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
      <Text variant="label" style={{ color: 'var(--lg-rail-label)' }}>Heat</Text>
      <Pips total={10} filled={6} color="var(--lg-rail-amber)" />
    </div>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
      <Text variant="label" style={{ color: 'var(--lg-rail-label)' }}>The safe</Text>
      <Pips total={10} filled={8} color="var(--lg-rail-safe-gold)" />
    </div>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
      <Text variant="label" style={{ color: 'var(--lg-rail-label)' }}>Fear</Text>
      <Pips total={10} filled={3} color="var(--lg-rail-red)" />
    </div>
  </div>
);

export const OnTheSheet = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, display: 'grid', gap: 10 }}>
    <Pips total={6} filled={4} color="var(--lg-ink2)" empty="rgba(43,36,24,0.18)" />
    <Pips total={6} filled={6} color="var(--lg-green)" empty="rgba(43,36,24,0.18)" />
    <Pips total={6} filled={1} color="var(--lg-red)" empty="rgba(43,36,24,0.18)" />
  </div>
);
