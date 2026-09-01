import { Segmented } from '@gangsters/ledger-1987';

export const SortOrder = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24 }}>
    <Segmented labels={['Roster order', 'By wage', 'By skill']} active={0} />
  </div>
);

export const Shelf = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24 }}>
    <Segmented labels={['Guns', 'Vehicles', 'Explosives']} active={1} />
  </div>
);
