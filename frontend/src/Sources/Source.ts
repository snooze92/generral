import ModelBase from 'App/ModelBase';
import Field from 'typings/Field';

export type EventStatus =
  'scheduled' | 'postponed' | 'cancelled' | 'completed' | 'unknown' | 'gone';

export interface SourceSelectOption {
  value: string;
  name: string;
  hint?: string;
}

export interface SourceField extends Omit<Field, 'value'> {
  value?: unknown;
  helpText?: string;
  unit?: string;
  placeholder?: string;
  selectOptions?: SourceSelectOption[];
}

interface Source extends ModelBase {
  name: string;
  type: string;
  typeName?: string;
  enabled: boolean;
  /** Minutes. */
  refreshInterval: number;
  tags: number[];
  fields: SourceField[];
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
