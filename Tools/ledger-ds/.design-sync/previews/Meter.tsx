import { Meter } from '@gangsters/ledger-1987';

export const Room = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, width: 300 }}>
    <Meter label="Men on the books" current={7} maximum={10} unit="man" plural="men" />
  </div>
);

export const AtTheLimit = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, width: 300 }}>
    <Meter label="Crew of Moretti" current={5} maximum={5} unit="man" plural="men" />
  </div>
);

export const OverTheLimit = () => (
  <div style={{ background: 'var(--lg-panel)', padding: 24, width: 300 }}>
    <Meter label="Blocks answered for" current={12} maximum={10} unit="block" plural="blocks" />
  </div>
);

export const OnTheRail = () => (
  <div style={{ background: 'var(--lg-rail)', padding: 24, width: 300 }}>
    <Meter label="The safe" current={8} maximum={20} unit="grand" plural="grand" dark />
  </div>
);
