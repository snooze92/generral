// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Wanted/Missing/useMissing.tsx and CutoffUnmet/useCutoffUnmet.tsx
import Episode from 'Episode/Episode';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { SortDirection } from 'Helpers/Props/sortDirections';
import Series from 'Series/Series';

export type WantedKind = 'missing' | 'cutoffUnmet';

export interface WantedEpisode extends Episode {
  series?: Series;
}

const PATHS: Record<WantedKind, string> = {
  missing: '/wanted/missing',
  cutoffUnmet: '/wanted/cutoff',
};

export const PAGE_SIZE = 20;

interface WantedOptions {
  page: number;
  sortKey: string;
  sortDirection: SortDirection;
  monitored: boolean;
}

const useWanted = (
  kind: WantedKind,
  { page, sortKey, sortDirection, monitored }: WantedOptions
) =>
  usePagedApiQuery<WantedEpisode>({
    path: PATHS[kind],
    page,
    pageSize: PAGE_SIZE,
    sortKey,
    sortDirection,
    queryParams: { includeSeries: true, monitored },
  });

export default useWanted;
