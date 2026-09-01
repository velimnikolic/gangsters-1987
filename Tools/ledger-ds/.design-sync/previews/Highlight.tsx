import { Highlight, LeaderRow } from '@gangsters/ledger-1987';

export const PickedRow = () => (
  <div style={{ background: 'var(--lg-card)', padding: 24, width: 300, display: 'grid', gap: 2 }}>
    <div style={{ padding: '7px 10px' }}>
      <LeaderRow label="S. Greco" figure="$120" />
    </div>
    <Highlight style={{ padding: '7px 10px' }}>
      <LeaderRow label="R. Ochoa" figure="$85" tone="red" />
    </Highlight>
    <div style={{ padding: '7px 10px' }}>
      <LeaderRow label="M. Delgado" figure="$140" />
    </div>
    <Highlight green style={{ padding: '7px 10px' }}>
      <LeaderRow label="Drop here - crew of Moretti" figure="" tone="green" />
    </Highlight>
  </div>
);
