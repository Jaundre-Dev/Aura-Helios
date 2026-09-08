/** Plan section 14.1. Single source of truth for the left rail and the command palette. */
export interface NavItem {
  label: string;
  path: string;
  children?: NavItem[];
}

export const navigation: NavItem[] = [
  { label: 'Overview', path: '/' },
  {
    label: 'Workspace',
    path: '/workspace',
    children: [
      { label: 'Projects', path: '/workspace/projects' },
      { label: 'Repositories', path: '/workspace/repositories' },
      { label: 'Environments', path: '/workspace/environments' },
      { label: 'Activity', path: '/workspace/activity' },
    ],
  },
  {
    label: 'Build',
    path: '/build',
    children: [
      { label: 'Engineering', path: '/build/engineering' },
      { label: 'Architecture', path: '/build/architecture' },
      { label: 'QA', path: '/build/qa' },
      { label: 'Legacy', path: '/build/legacy' },
    ],
  },
  {
    label: 'Operate',
    path: '/operate',
    children: [
      { label: 'Incidents', path: '/operate/incidents' },
      { label: 'Security', path: '/operate/security' },
      { label: 'Decisions', path: '/operate/decisions' },
    ],
  },
  {
    label: 'Intelligence',
    path: '/intelligence',
    children: [
      { label: 'Knowledge', path: '/intelligence/knowledge' },
      { label: 'Agents', path: '/intelligence/agents' },
      { label: 'Teams', path: '/intelligence/teams' },
      { label: 'Learning', path: '/intelligence/learning' },
    ],
  },
  {
    label: 'Platform',
    path: '/platform',
    children: [
      { label: 'Models', path: '/platform/models' },
      { label: 'Providers', path: '/platform/providers' },
      { label: 'Tools', path: '/platform/tools' },
      { label: 'Workflows', path: '/platform/workflows' },
      { label: 'Evaluations', path: '/platform/evaluations' },
      { label: 'Observability', path: '/platform/observability' },
      { label: 'Settings', path: '/platform/settings' },
    ],
  },
];

export const flatNavigation: NavItem[] = navigation.flatMap((item) =>
  item.children ? item.children : [item],
);
