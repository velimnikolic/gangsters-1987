import { Text } from '@gangsters/ledger-1987';

export const Voices = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, width: 360, display: 'grid', gap: 10 }}>
    <Text variant="kicker">The Outfit · This Week</Text>
    <Text variant="name">Salvatore "Sally Shoes" Greco</Text>
    <Text variant="label">Protection collected</Text>
    <Text variant="figure">$1,247</Text>
    <Text variant="copy">
      Runs the counter at the pawnshop since March. Pays on the first knock,
      but the neighbours say he keeps a second book under the till.
    </Text>
    <Text variant="copy" italic tone="muted">
      — filed by E. Moretti, doorstep round, day 38
    </Text>
  </div>
);

export const Tones = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, width: 260, display: 'grid', gap: 8 }}>
    <Text variant="figure" tone="ink">$4,120 on hand</Text>
    <Text variant="figure" tone="red">-$300 short</Text>
    <Text variant="figure" tone="amber">at the limit</Text>
    <Text variant="figure" tone="green">paid in full</Text>
    <Text variant="figure" tone="paperBlue">on paper only</Text>
  </div>
);
