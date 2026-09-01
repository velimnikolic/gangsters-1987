import { StepBar, Text } from '@gangsters/ledger-1987';

export const Counts = () => (
  <div style={{ background: 'var(--lg-card)', padding: 24, width: 280, display: 'grid', gap: 10 }}>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
      <Text variant="label">Practice · Combat</Text>
      <StepBar steps={10} filled={6} />
    </div>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
      <Text variant="label">Practice · Driving</Text>
      <StepBar steps={10} filled={9} color="var(--lg-green-ok)" />
    </div>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
      <Text variant="label">Heat on the block</Text>
      <StepBar steps={10} filled={3} color="var(--lg-red-pen)" />
    </div>
  </div>
);
