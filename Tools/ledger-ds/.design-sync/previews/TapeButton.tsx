import { TapeButton } from '@gangsters/ledger-1987';

export const Verbs = () => (
  <div style={{ background: 'var(--lg-card)', padding: 24, display: 'flex', gap: 12, alignItems: 'center' }}>
    <TapeButton label="Promote" />
    <TapeButton label="Order" />
    <TapeButton label="Commit" red />
  </div>
);

export const OutlinedAndDisabled = () => (
  <div style={{ background: 'var(--lg-card)', padding: 24, display: 'flex', gap: 12, alignItems: 'center' }}>
    <TapeButton label="Call off" outline red />
    <TapeButton label="Inspect" outline />
    <TapeButton label="Promote" disabled />
  </div>
);
