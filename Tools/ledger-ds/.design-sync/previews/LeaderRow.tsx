import { LeaderRow } from '@gangsters/ledger-1987';

export const BalanceLines = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, width: 300, display: 'grid', gap: 9 }}>
    <LeaderRow label="Dues, doorstep round" figure="$1,615" />
    <LeaderRow label="The card game" figure="$890" />
    <LeaderRow label="Wages paid" figure="-$2,140" tone="red" />
    <LeaderRow label="Held in the safe" figure="$12,480" tone="green" />
  </div>
);

export const Standing = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, width: 300, display: 'grid', gap: 9 }}>
    <LeaderRow label="Blocks held" figure="14" />
    <LeaderRow label="Contested" figure="3" tone="amber" />
    <LeaderRow label="Lost this month" figure="1" tone="red" />
  </div>
);
