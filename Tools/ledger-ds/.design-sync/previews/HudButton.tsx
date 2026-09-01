import { HudButton } from '@gangsters/ledger-1987';

export const Buttons = () => (
  <div style={{ background: '#3a4a3f', padding: 24, display: 'flex', gap: 14, alignItems: 'center' }}>
    <HudButton label="FOLLOW" />
    <HudButton label="HOLD" />
    <HudButton label="DRIVE" disabled />
  </div>
);
