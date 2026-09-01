import { HudPanel } from '@gangsters/ledger-1987';

export const StreetChrome = () => (
  <div style={{ background: '#3a4a3f', padding: 24, display: 'grid', gap: 16, width: 300 }}>
    <HudPanel variant="dark" style={{ padding: '10px 14px' }}>
      OCT 12 · 21:40 · THE BOULEVARD
    </HudPanel>
    <HudPanel variant="raised" style={{ padding: '10px 14px' }}>
      CREW OF MORETTI · 4 MEN OUT
    </HudPanel>
    <HudPanel variant="sunken" style={{ padding: '8px 14px' }}>
      HEAT ██████░░░░
    </HudPanel>
  </div>
);
