/** Must match Helios.Contracts.Realtime.HubRoutes. */
export const hubRoutes = {
  workspace: '/hubs/workspace',
  agent: '/hubs/agent',
  project: '/hubs/project',
  notification: '/hubs/notification',
  workflow: '/hubs/workflow',
  monitoring: '/hubs/monitoring',
} as const;

export const hubMethods = {
  receiveEvent: 'ReceiveEvent',
  receiveStreamChunk: 'ReceiveStreamChunk',
  receiveNotification: 'ReceiveNotification',
} as const;
