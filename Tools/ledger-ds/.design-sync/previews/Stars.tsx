import { Stars, Text } from '@gangsters/ledger-1987';

export const AttributeRows = () => (
  <div style={{ background: 'var(--lg-card)', padding: 24, width: 300, display: 'grid', gap: 8 }}>
    {[
      ['Combat', 9],
      ['Streetwise', 7],
      ['Driving', 4],
      ['Leadership', 1],
    ].map(([label, half]) => (
      <div key={label as string}
        style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Text variant="label">{label}</Text>
        <Stars halfSteps={half as number} />
      </div>
    ))}
  </div>
);

export const TheScale = () => (
  <div style={{ background: 'var(--lg-card)', padding: 24, display: 'grid', gap: 6 }}>
    <Stars halfSteps={10} />
    <Stars halfSteps={7} />
    <Stars halfSteps={5} />
    <Stars halfSteps={2} />
    <Stars halfSteps={0} />
  </div>
);
