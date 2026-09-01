import {
  Desk, PaperSheet, Polaroid, Stamp, Text, LeaderRow, TapeButton,
} from '@gangsters/ledger-1987';

export const TheFileOnTheDesk = () => (
  <Desk padding={44} style={{ width: 560, boxSizing: 'border-box' }}>
    <div style={{ display: 'flex', gap: 24, alignItems: 'flex-start' }}>
      <PaperSheet stock="card" aging punched tilt={-0.35} width={300}>
        <Text variant="kicker">Personnel · Dossier</Text>
        <div style={{ height: 8 }} />
        <Text variant="name">Salvatore "Sally Shoes" Greco</Text>
        <div style={{ height: 4 }} />
        <Text variant="label">Lieutenant · Crew of 4</Text>
        <div style={{ height: 12 }} />
        <LeaderRow label="Wage" figure="$260 / wk" />
        <div style={{ height: 8 }} />
        <LeaderRow label="Collected" figure="$1,247" tone="green" />
        <div style={{ height: 16 }} />
        <TapeButton label="Promote" />
      </PaperSheet>
      <div style={{ display: 'grid', gap: 18, justifyItems: 'start' }}>
        <Polaroid initials="SG" caption="S. Greco" tilt={2.5} />
        <Stamp word="Active" tilt={-5} />
      </div>
    </div>
  </Desk>
);
