// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Episode/useEpisodes.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { PATH as SERIES_PATH } from 'Series/useSeries';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import Episode from './Episode';

export const PATH = '/episode';

const NO_EPISODES: Episode[] = [];

const headers = {
  'X-Api-Key': window.Generral.apiKey,
  'X-Generral-Client': 'Generral',
};

/** All of a series' episodes, orphaned ones included so they can be shown greyed out. */
const useEpisodes = (seriesId: number) => {
  const result = useApiQuery<Episode[]>({
    path: PATH,
    queryParams: { seriesId, includeOrphaned: true },
  });

  return { ...result, data: result.data ?? NO_EPISODES };
};

export default useEpisodes;

export const useSeasonEpisodes = (seriesId: number, seasonNumber: number) => {
  const { data, ...result } = useEpisodes(seriesId);

  const episodes = useMemo(
    () =>
      data
        .filter((episode) => episode.seasonNumber === seasonNumber)
        .sort((a, b) => b.episodeNumber - a.episodeNumber),
    [data, seasonNumber]
  );

  return { ...result, data: episodes };
};

/** Patches the episodes in the cache and refetches the series, whose statistics count monitored episodes. */
const useUpdateEpisodesCache = () => {
  const queryClient = useQueryClient();

  return useCallback(
    (updated: Episode[]) => {
      const byId = new Map(updated.map((episode) => [episode.id, episode]));

      queryClient.setQueriesData<Episode[]>(
        { queryKey: [PATH] },
        (oldData) => oldData?.map((episode) => byId.get(episode.id) ?? episode)
      );
      queryClient.invalidateQueries({ queryKey: [SERIES_PATH] });
    },
    [queryClient]
  );
};

export const useToggleEpisodesMonitored = () => {
  const updateEpisodesCache = useUpdateEpisodesCache();

  const { mutate, isPending, variables } = useMutation<
    Episode[],
    ApiError,
    { episodeIds: number[]; monitored: boolean }
  >({
    mutationFn: ({ episodeIds, monitored }) =>
      episodeIds.length === 1
        ? fetchJson<Episode, { monitored: boolean }>({
            path: getQueryPath(`${PATH}/${episodeIds[0]}`),
            method: 'PUT',
            headers,
            body: { monitored },
          }).then((episode) => [episode])
        : fetchJson<Episode[], { episodeIds: number[]; monitored: boolean }>({
            path: getQueryPath(`${PATH}/monitor`),
            method: 'PUT',
            headers,
            body: { episodeIds, monitored },
          }),
    onSuccess: updateEpisodesCache,
  });

  return {
    toggleEpisodesMonitored: mutate,
    isToggling: isPending,
    togglingEpisodeIds: isPending ? (variables?.episodeIds ?? []) : [],
  };
};

export const useToggleEpisodeLocked = (episodeId: number) => {
  const updateEpisodesCache = useUpdateEpisodesCache();

  const { mutate, isPending } = useMutation<Episode, ApiError, boolean>({
    mutationFn: (numberLocked) =>
      fetchJson<Episode, { numberLocked: boolean }>({
        path: getQueryPath(`${PATH}/${episodeId}/lock`),
        method: 'PUT',
        headers,
        body: { numberLocked },
      }),
    onSuccess: (episode) => updateEpisodesCache([episode]),
  });

  return { toggleEpisodeLocked: mutate, isTogglingLocked: isPending };
};
