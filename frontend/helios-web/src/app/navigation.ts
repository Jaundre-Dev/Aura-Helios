/** Planned API-platform navigation. Screens remain explicitly under development. */
export interface NavItem {
  label: string;
  path: string;
  children?: NavItem[];
}

export const navigation: NavItem[] = [
  { label: 'Overview', path: '/' },
  { label: 'API Catalogue', path: '/catalogue' },
  { label: 'Playground', path: '/playground' },
  { label: 'Requests', path: '/requests' },
  { label: 'API Keys', path: '/api-keys' },
  { label: 'Usage', path: '/usage' },
  { label: 'Billing', path: '/billing' },
  { label: 'Webhooks', path: '/webhooks' },
  { label: 'Company', path: '/company' },
  { label: 'Team', path: '/team' },
  { label: 'Settings', path: '/settings' },
];

export const flatNavigation: NavItem[] = navigation.flatMap((item) =>
  item.children ? item.children : [item],
);
