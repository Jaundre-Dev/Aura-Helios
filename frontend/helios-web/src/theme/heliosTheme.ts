import {
  createDarkTheme,
  createLightTheme,
  type BrandVariants,
  type Theme,
} from '@fluentui/react-components';

/**
 * Plan section 14: a professional engineering workstation, not a generic AI gradient.
 * Brand ramp stays restrained so hierarchy comes from typography and spacing.
 */
const heliosBrand: BrandVariants = {
  10: '#03060D',
  20: '#0B1220',
  30: '#122036',
  40: '#182D4B',
  50: '#1E3B61',
  60: '#254A78',
  70: '#2C5990',
  80: '#3469A9',
  90: '#3D79C2',
  100: '#4A89D6',
  110: '#5F99DE',
  120: '#77A9E5',
  130: '#91B9EB',
  140: '#ACC9F1',
  150: '#C7D9F6',
  160: '#E3ECFB',
};

export const heliosDark: Theme = createDarkTheme(heliosBrand);
export const heliosLight: Theme = createLightTheme(heliosBrand);

// TODO Phase 0: high-contrast theme and a user-level theme switch.
