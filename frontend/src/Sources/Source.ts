import { ProviderBase } from 'Settings/Provider/Provider';

export type EventStatus =
  'scheduled' | 'postponed' | 'cancelled' | 'completed' | 'unknown' | 'gone';

interface Source extends ProviderBase {
  type: string;
  typeName?: string;
  enabled: boolean;
  /** Minutes. */
  refreshInterval: number;
  tags: number[];
  lastRefresh?: string;
  lastSuccess?: string;
  lastError?: string;
  nextRefresh?: string;
  eventCount: number;
}

export interface SourceEvent {
  id: number;
  sourceId: number;
  key: string;
  title: string;
  start?: string;
  end?: string;
  timezone?: string;
  status: EventStatus;
  fields: Record<string, unknown>;
  firstSeen?: string;
  lastSeen?: string;
  lastChanged?: string;
  missedRefreshes: number;
}

export interface SourceItemError {
  index: number;
  url?: string;
  fields: Record<string, unknown>;
  errors: string[];
}

export interface RawResponse {
  url: string;
  status: number;
  contentType?: string;
  body: string;
  truncated: boolean;
  fromCache: boolean;
}

export interface SourceTestResult {
  success: boolean;
  error?: string;
  events: SourceEvent[];
  itemErrors: SourceItemError[];
  responses: RawResponse[];
  warnings: string[];
}

export default Source;
