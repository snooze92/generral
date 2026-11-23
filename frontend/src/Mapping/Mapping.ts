import ModelBase from 'App/ModelBase';
import { SeriesType } from 'Series/Series';
import { EventStatus } from 'Sources/Source';

/** The `/mapping` resource (docs/specs/11-api.md, M1b). */
interface Mapping extends ModelBase {
  name: string;
  seriesId: number;
  sourceId: number;
  priority: number;
  enabled: boolean;
  definition: string;
  version: number;
}

export default Mapping;

export type MappingChange =
  | 'new'
  | 'changed'
  | 'unchanged'
  | 'orphaned'
  | 'filtered'
  | 'error';

export interface MappingPreviewRequest {
  id?: number;
  seriesId?: number;
  sourceId: number;
  definition: string;
}

export interface MappingPreviewRow {
  event: {
    id: number;
    key: string;
    title: string;
    start?: string;
    status: EventStatus;
  };
  kept: boolean;
  rejectedBy?: string;
  episodeId?: number;
  currentSeasonNumber?: number;
  currentEpisodeNumber?: number;
  seasonNumber?: number;
  episodeNumber?: number;
  absoluteEpisodeNumber?: number;
  title?: string;
  overview?: string;
  airDate?: string;
  airDateUtc?: string;
  runtime?: number;
  thumb?: string;
  poster?: string;
  numberLocked: boolean;
  renumbered: boolean;
  change: MappingChange;
  errors: string[];
}

export interface MappingPreviewSummary {
  events: number;
  kept: number;
  episodes: number;
  new: number;
  changed: number;
  unchanged: number;
  orphaned: number;
  filtered: number;
  errors: number;
  renumbered: number;
}

export interface MappingPreview {
  rows: MappingPreviewRow[];
  summary: MappingPreviewSummary;
  seriesType: SeriesType;
  seasons: { seasonNumber: number; title?: string }[];
  errors: string[];
}
