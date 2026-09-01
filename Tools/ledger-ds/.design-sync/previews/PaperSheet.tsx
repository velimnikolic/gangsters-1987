import { PaperSheet, Text, LeaderRow, Stamp } from '@gangsters/ledger-1987';

export const AgedFile = () => (
  <div style={{ background: 'var(--lg-desk)', padding: 32 }}>
    <PaperSheet stock="card" aging punched coffeeRing tilt={-0.35} width={340}>
      <Text variant="kicker">Case file · The Pawnshop</Text>
      <div style={{ height: 8 }} />
      <Text variant="name">1214 Calle Ocho</Text>
      <div style={{ height: 10 }} />
      <Text variant="copy">
        Owner pays without a word since the window went in. Two of Rivera's
        men watch the corner after dark; the take goes uptown on Fridays.
      </Text>
      <div style={{ height: 14 }} />
      <LeaderRow label="Protection" figure="$120 / wk" />
    </PaperSheet>
  </div>
);

export const Greenbar = () => (
  <div style={{ background: 'var(--lg-desk)', padding: 32 }}>
    <PaperSheet stock="greenbar" banded width={320}>
      <Text variant="kicker" style={{ color: 'var(--lg-greenbar-ink)' }}>Balance · Week 6</Text>
      <div style={{ height: 8 }} />
      <div style={{ display: 'grid', gap: 8 }}>
        <LeaderRow label="Dues" figure="$3,615" />
        <LeaderRow label="The card game" figure="$890" />
        <LeaderRow label="Wages" figure="-$2,140" tone="red" />
        <LeaderRow label="Held over" figure="$2,365" tone="green" />
      </div>
    </PaperSheet>
  </div>
);

export const CarbonCopy = () => (
  <div style={{ background: 'var(--lg-desk)', padding: 32 }}>
    <PaperSheet stock="carbon" tilt={0.5} width={280}>
      <Text variant="kicker" style={{ color: 'var(--lg-carbon-ink)' }}>Stock book · Carbon</Text>
      <div style={{ height: 8 }} />
      <Text variant="copy" style={{ color: 'var(--lg-carbon-ink)' }}>
        Issued to Moretti: two pistols, one shotgun. Signed against the
        armory ledger, day 39.
      </Text>
    </PaperSheet>
  </div>
);

export const StampedNotice = () => (
  <div style={{ background: 'var(--lg-desk)', padding: 32 }}>
    <PaperSheet stock="printout" ruled width={300}>
      <Text variant="name">Ramon Ochoa · Grocer</Text>
      <div style={{ height: 6 }} />
      <Text variant="copy">Three knocks, no answer. The till was light twice in May.</Text>
      <div style={{ height: 12 }} />
      <Stamp word="Overdue" />
    </PaperSheet>
  </div>
);
