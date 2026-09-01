import { Plate } from '@gangsters/ledger-1987';

export const BlockOuts = () => (
  <div style={{ background: 'var(--lg-card)', padding: 28, display: 'flex', gap: 18 }}>
    <Plate caption="Plate 7 · The Block" width={150} height={116} />
    <Plate caption="Plate 8 · The Front" width={150} height={116} />
  </div>
);

export const Wide = () => (
  <div style={{ background: 'var(--lg-card)', padding: 28 }}>
    <Plate caption="The waterfront, from the crane" width={320} height={130} />
  </div>
);
