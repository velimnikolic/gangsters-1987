import { StatusChip } from '@gangsters/ledger-1987';

export const Verdicts = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, display: 'flex', gap: 10, flexWrap: 'wrap', width: 300 }}>
    <StatusChip word="Active" tone="green" />
    <StatusChip word="Jailed" tone="filed" />
    <StatusChip word="Overdue" tone="red" />
    <StatusChip word="At the limit" tone="amber" />
    <StatusChip word="Filed" tone="ink" />
  </div>
);

export const Ranks = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, display: 'flex', gap: 10 }}>
    <StatusChip word="Boss" tone="boss" />
    <StatusChip word="Lieutenant" tone="lieutenant" />
  </div>
);
