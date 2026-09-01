import { Mark, Text } from '@gangsters/ledger-1987';

export const PaperAgainstStreet = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, width: 320, display: 'grid', gap: 10 }}>
    <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
      <Mark kind="street" color="#8f2119" />
      <Text variant="label">Greco holds it on the street</Text>
    </div>
    <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
      <Mark kind="paper" color="#2f4a7a" />
      <Text variant="label">The deed says Ochoa - paper only</Text>
    </div>
    <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
      <Mark kind="street" color="#3f6b3a" />
      <Mark kind="paper" color="#3f6b3a" />
      <Text variant="label">Held AND deeded - the clean kind</Text>
    </div>
  </div>
);
