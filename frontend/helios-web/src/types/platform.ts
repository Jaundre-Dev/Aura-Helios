/** Mirrors Helios.Contracts. Keep the string unions in step with the C# enums. */

export type DataClassification = 'Public' | 'Internal' | 'Confidential' | 'Restricted';

export type RiskLevel = 'Low' | 'Medium' | 'High' | 'Critical';

export type AgentRunState =
  | 'Created'
  | 'Queued'
  | 'Planning'
  | 'AwaitingApproval'
  | 'Executing'
  | 'WaitingForTool'
  | 'Evaluating'
  | 'Completed'
  | 'Failed'
  | 'Retrying'
  | 'NeedsHuman'
  | 'Cancelled';

export interface ModelInfo {
  providerId: string;
  modelId: string;
  displayName: string;
  contextWindow: number;
  supportsStreaming: boolean;
  supportsTools: boolean;
  supportsVision: boolean;
  isLocal: boolean;
  isAvailable: boolean;
}

export interface AgentRunSummary {
  id: string;
  goal: string;
  state: AgentRunState;
  resolvedProviderId?: string;
  resolvedModelId?: string;
  startedAt?: string;
  completedAt?: string;
  estimatedCost?: number;
  toolCallCount: number;
}

export interface HeliosEvent {
  eventId: string;
  eventType: string;
  occurredAt: string;
  workspaceId?: string;
  projectId?: string;
  agentRunId?: string;
  [key: string]: unknown;
}
