import { Panel, LeaderRow, Text } from '@gangsters/ledger-1987';

export const WithHead = () => (
  <div style={{ background: 'var(--lg-ground)', padding: 24 }}>
    <Panel head="The Outfit" headRight="File 002-A" width={300}>
      <LeaderRow label="Safe" figure="$12,480" />
      <div style={{ height: 8 }} />
      <LeaderRow label="Wages due" figure="-$2,140" tone="red" />
      <div style={{ height: 8 }} />
      <LeaderRow label="Dues collected" figure="$3,615" tone="green" />
    </Panel>
  </div>
);

export const Bare = () => (
  <div style={{ background: 'var(--lg-ground)', padding: 24 }}>
    <Panel width={300}>
      <Text variant="kicker">Intelligence note</Text>
      <div style={{ height: 6 }} />
      <Text variant="copy">
        The Riveras keep two men outside the pawnshop on Calle Ocho after dark.
        Nobody has seen the owner since Tuesday.
      </Text>
    </Panel>
  </div>
);

export const DarkFace = () => (
  <div style={{ background: 'var(--lg-ground)', padding: 24 }}>
    <Panel head="This Week" headRight="Day 41" face="var(--lg-panel-dark)" width={300}>
      <LeaderRow label="Jobs out" figure="3" />
      <div style={{ height: 8 }} />
      <LeaderRow label="Needs an answer" figure="2" tone="amber" />
    </Panel>
  </div>
);
