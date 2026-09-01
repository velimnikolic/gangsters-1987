import { Key } from '@gangsters/ledger-1987';

export const FourFaces = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, display: 'flex', gap: 12, alignItems: 'center' }}>
    <Key label="Promote" variant="dark" />
    <Key label="Inspect" variant="outline" />
    <Key label="Call off" variant="ghost" />
    <Key label="Commit" variant="red" />
  </div>
);

export const Disabled = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, display: 'flex', gap: 12 }}>
    <Key label="Promote" variant="dark" disabled />
    <Key label="Inspect" variant="outline" disabled />
  </div>
);
