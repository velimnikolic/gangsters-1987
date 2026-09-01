import { TelexSlip } from '@gangsters/ledger-1987';

export const NightWires = () => (
  <div style={{ background: 'var(--lg-ground)', padding: 24, display: 'grid', gap: 14, width: 340 }}>
    <TelexSlip source="Wire · Downtown" time="02:14">
      RIVERA CREW SEEN OUTSIDE THE CAR WASH ON 12TH. THREE MEN, ONE STAYED
      IN THE VAN. NOBODY WENT IN.
    </TelexSlip>
    <TelexSlip source="Wire · The Port" time="04:51">
      SHIPMENT LANDED SHORT. HARBORMASTER ASKING FOR HIS ENVELOPE EARLY.
    </TelexSlip>
  </div>
);

export const OneSlip = () => (
  <div style={{ background: 'var(--lg-ground)', padding: 24, width: 320 }}>
    <TelexSlip source="Wire · Precinct 8" time="23:40">
      PATROLS DOUBLED ON THE BOULEVARD THROUGH SUNDAY. KEEP THE CARS OFF IT.
    </TelexSlip>
  </div>
);
