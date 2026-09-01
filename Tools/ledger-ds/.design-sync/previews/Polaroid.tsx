import { Polaroid } from '@gangsters/ledger-1987';

export const CrewPhotos = () => (
  <div style={{ background: 'var(--lg-desk)', padding: 32, display: 'flex', gap: 20, alignItems: 'flex-start' }}>
    <Polaroid initials="SG" caption="S. Greco" tilt={-3} />
    <Polaroid initials="EM" caption="E. Moretti" tilt={2} />
    <Polaroid initials="RD" caption="R. Delgado" tilt={-1} />
  </div>
);

export const Unphotographed = () => (
  <div style={{ background: 'var(--lg-desk)', padding: 32 }}>
    <Polaroid initials="?" caption="No print yet" photoSize={120} />
  </div>
);
