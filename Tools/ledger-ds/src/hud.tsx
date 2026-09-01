import type { CSSProperties, ReactNode } from 'react';
import { useState } from 'react';
import { patches } from './patches';

export interface HudPanelProps {
  /** `dark` is the pack's popup panel - the default street chrome; `raised`
   *  the lighter framed panel for surfaces that sit on other surfaces;
   *  `sunken` the pressed box a bar's trough reads as. */
  variant?: 'dark' | 'raised' | 'sunken';
  style?: CSSProperties;
  children?: ReactNode;
}

const panelPatch = {
  dark: patches.panelDark,
  raised: patches.panelRaised,
  sunken: patches.sunken,
} as const;

/**
 * The street-level HUD's chrome: the CC0 "Waste No Space" 64x64 GUI sheet
 * sliced into 9-patch panels at the pack's own 3x pixel scale. This skin
 * covers menus, popups and bars - it stops at the ledger book's cover.
 */
export function HudPanel({ variant = 'dark', style, children }: HudPanelProps) {
  return (
    <div
      className="lg-hud-panel"
      style={{ borderImageSource: `url("${panelPatch[variant]}")`, ...style }}
    >
      {children}
    </div>
  );
}

export interface HudButtonProps {
  label: string;
  disabled?: boolean;
  onClick?: () => void;
  style?: CSSProperties;
}

/**
 * The pack's button in its four states - normal, hover, pressed, disabled -
 * swapped exactly the way the sheet's own preview scene swaps them.
 */
export function HudButton({ label, disabled, onClick, style }: HudButtonProps) {
  const [state, setState] = useState<'normal' | 'hover' | 'pressed'>('normal');
  const patch = disabled
    ? patches.buttonDisabled
    : state === 'pressed'
      ? patches.buttonPressed
      : state === 'hover'
        ? patches.buttonHover
        : patches.buttonNormal;
  return (
    <button
      type="button"
      className="lg-hud-button"
      disabled={disabled}
      onClick={onClick}
      onMouseEnter={() => setState('hover')}
      onMouseLeave={() => setState('normal')}
      onMouseDown={() => setState('pressed')}
      onMouseUp={() => setState('hover')}
      style={{ borderImageSource: `url("${patch}")`, ...style }}
    >
      {label}
    </button>
  );
}
