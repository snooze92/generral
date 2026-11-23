// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/InteractiveSearch/useReleases.ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import _ from 'lodash';
import { useMemo } from 'react';
import { create } from 'zustand';
import { PATH as QUEUE_PATH } from 'Activity/Queue/useQueue';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';
import { SortDirection } from 'Helpers/Props/sortDirections';
import Language from 'Language/Language';
import { QualityModel } from 'Quality/Quality';
import fetchJson, {
  ApiError,
  ApiErrorResponse,
} from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import translate from 'Utilities/String/translate';

const PATH = '/release';
const TIMED_OUT_HEADER = 'X-Generral-Timed-Out-Indexers';

const headers = {
  'X-Api-Key': window.Generral.apiKey,
  'X-Generral-Client': 'Generral',
};

/** The `/release` resource (Sonarr's `ReleaseResource` without scene/custom-format fields). */
export interface Release {
  guid: string;
  quality: QualityModel;
  qualityWeight: number;
  age: number;
  ageHours: number;
  ageMinutes: number;
  size: number;
  indexerId: number;
  indexer: string;
  title: string;
  approved: boolean;
  rejected: boolean;
  rejections: string[];
  publishDate: string;
  infoUrl?: string;
  downloadAllowed: boolean;
  releaseWeight: number;
  seeders?: number;
  leechers?: number;
  protocol: DownloadProtocol;
  languages: Language[];
}

interface ReleaseSearch {
  releases: Release[];
  timedOutIndexers: string[];
}

/** Sonarr's sort predicates; `releaseWeight` is the server's order (approved → quality → seeders). */
const SORT_PREDICATES: Record<string, (item: Release) => number | string> = {
  age: (item) => item.ageMinutes,
  indexer: (item) => item.indexer.toLowerCase(),
  languages: (item) => {
    if (item.languages.length > 1) {
      return translate('MultiLanguages');
    }

    const language = item.languages[0];

    return !language || language.id === 0 ? '\uffff' : language.name;
  },
  peers: (item) => (item.seeders || 0) * 1000000 + (item.leechers || 0),
  protocol: (item) => item.protocol,
  qualityWeight: (item) => item.qualityWeight,
  rejections: (item) =>
    item.rejections.length !== 0
      ? item.releaseWeight + 1000000
      : item.releaseWeight,
  releaseWeight: (item) => item.releaseWeight,
  size: (item) => item.size,
  title: (item) => item.title.toLowerCase(),
};

export const FIXED_SORT_DIRECTIONS: Record<string, SortDirection> = {
  rejections: 'ascending',
  releaseWeight: 'ascending',
};

export function sortReleases(
  releases: Release[],
  sortKey: string,
  sortDirection: SortDirection
) {
  const predicate = SORT_PREDICATES[sortKey] ?? SORT_PREDICATES.releaseWeight;

  return _.orderBy(
    releases,
    [predicate, SORT_PREDICATES.releaseWeight],
    [sortDirection === 'ascending' ? 'asc' : 'desc', 'asc']
  );
}

interface ReleaseSort {
  sortKey: string;
  sortDirection: SortDirection;
}

const releaseSortStore = create<ReleaseSort>(() => ({
  sortKey: 'releaseWeight',
  sortDirection: 'ascending',
}));

export const useReleaseSort = () => releaseSortStore();

export const setReleaseSort = (
  sortKey: string,
  sortDirection: SortDirection | undefined
) => {
  releaseSortStore.setState((state) => {
    if (sortDirection) {
      return { sortKey, sortDirection };
    }

    if (state.sortKey === sortKey) {
      return {
        sortKey,
        sortDirection:
          state.sortDirection === 'ascending' ? 'descending' : 'ascending',
      };
    }

    return { sortKey, sortDirection: state.sortDirection };
  });
};

async function searchReleases(
  episodeId: number,
  signal: AbortSignal
): Promise<ReleaseSearch> {
  const path = `${getQueryPath(PATH)}?episodeId=${episodeId}`;
  const response = await fetch(path, {
    headers: { ...headers, Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    const body = (await response.json().catch(() => undefined)) as
      | ApiErrorResponse
      | undefined;

    throw new ApiError(path, response.status, response.statusText, body);
  }

  const timedOut = response.headers.get(TIMED_OUT_HEADER);

  return {
    releases: (await response.json()) as Release[],
    timedOutIndexers: timedOut ? timedOut.split(', ') : [],
  };
}

const NO_RELEASES: Release[] = [];
const NO_INDEXERS: string[] = [];

/** Searches the indexers when the modal opens; results aren't cached client-side (each open searches again). */
const useReleases = (episodeId: number) => {
  const { sortKey, sortDirection } = useReleaseSort();
  const result = useQuery<ReleaseSearch, ApiError>({
    queryKey: [PATH, { episodeId }],
    queryFn: ({ signal }) => searchReleases(episodeId, signal),
    gcTime: 0,
    staleTime: Infinity,
    retry: false,
  });

  const releases = result.data?.releases;
  const data = useMemo(
    () =>
      releases ? sortReleases(releases, sortKey, sortDirection) : NO_RELEASES,
    [releases, sortKey, sortDirection]
  );

  return {
    ...result,
    data,
    timedOutIndexers: result.data?.timedOutIndexers ?? NO_INDEXERS,
    sortKey,
    sortDirection,
  };
};

export default useReleases;

export const useGrabRelease = ({ guid, indexerId }: Release) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, isSuccess, error } = useMutation<
    Release,
    ApiError,
    void
  >({
    mutationFn: () =>
      fetchJson<Release, Pick<Release, 'guid' | 'indexerId'>>({
        path: getQueryPath(PATH),
        method: 'POST',
        headers,
        body: { guid, indexerId },
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [QUEUE_PATH] }),
  });

  return {
    grabRelease: mutate,
    isGrabbing: isPending,
    isGrabbed: isSuccess,
    grabError: error,
  };
};
