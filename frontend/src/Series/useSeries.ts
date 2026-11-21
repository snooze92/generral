// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Series/useSeries.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import Series from './Series';

export const PATH = '/series';

const NO_SERIES: Series[] = [];

const headers = {
  'X-Api-Key': window.Generral.apiKey,
  'X-Generral-Client': 'Generral',
};

const useSeries = () => {
  const result = useApiQuery<Series[]>({ path: PATH });

  return { ...result, data: result.data ?? NO_SERIES };
};

export default useSeries;

export const useSingleSeries = (seriesId: number | undefined) => {
  const { data } = useSeries();

  return data.find((series) => series.id === seriesId);
};

export const useUpdateSeriesCache = () => {
  const queryClient = useQueryClient();

  return useCallback(
    (updated: Series) => {
      queryClient.setQueryData<Series[]>([PATH], (oldData = []) =>
        oldData.map((series) => (series.id === updated.id ? updated : series))
      );
    },
    [queryClient]
  );
};

/** PUTs the whole series with the given changes, like Sonarr's toggles. */
export const useSaveSeries = (seriesId: number) => {
  const series = useSingleSeries(seriesId);
  const updateSeriesCache = useUpdateSeriesCache();

  const { mutate, isPending, error } = useMutation<
    Series,
    ApiError,
    Partial<Series>
  >({
    mutationFn: (changes) =>
      fetchJson<Series, Series>({
        path: getQueryPath(`${PATH}/${seriesId}`),
        method: 'PUT',
        headers,
        body: { ...series!, ...changes },
      }),
    onSuccess: updateSeriesCache,
  });

  return { saveSeries: mutate, isSaving: isPending, saveError: error };
};

export const useToggleSeriesMonitored = (seriesId: number) => {
  const { saveSeries, isSaving } = useSaveSeries(seriesId);

  const toggleSeriesMonitored = useCallback(
    (monitored: boolean) => saveSeries({ monitored }),
    [saveSeries]
  );

  return {
    toggleSeriesMonitored,
    isTogglingSeriesMonitored: isSaving,
  };
};

/** Like Sonarr, the API also sets the season's episodes, so they are refetched. */
export const useToggleSeasonMonitored = (seriesId: number) => {
  const queryClient = useQueryClient();
  const series = useSingleSeries(seriesId);
  const updateSeriesCache = useUpdateSeriesCache();

  const { mutate, isPending, variables } = useMutation<
    Series,
    ApiError,
    { seasonNumber: number; monitored: boolean }
  >({
    mutationFn: ({ seasonNumber, monitored }) =>
      fetchJson<Series, Series>({
        path: getQueryPath(`${PATH}/${seriesId}`),
        method: 'PUT',
        headers,
        body: {
          ...series!,
          seasons: series!.seasons.map((season) =>
            season.seasonNumber === seasonNumber
              ? { ...season, monitored }
              : season
          ),
        },
      }),
    onSuccess: (updated) => {
      updateSeriesCache(updated);
      queryClient.invalidateQueries({ queryKey: ['/episode'] });
    },
  });

  return {
    toggleSeasonMonitored: mutate,
    isTogglingSeasonMonitored: isPending,
    togglingSeasonNumber: isPending ? variables?.seasonNumber : undefined,
  };
};

export const useDeleteSeries = (seriesId: number) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error, reset } = useApiMutation<void, void>({
    path: `${PATH}/${seriesId}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.setQueryData<Series[]>([PATH], (oldData = []) =>
          oldData.filter((series) => series.id !== seriesId)
        );
        queryClient.removeQueries({ queryKey: ['/episode'] });
        queryClient.invalidateQueries({ queryKey: ['/tag/detail'] });
      },
    },
  });

  return {
    deleteSeries: mutate,
    isDeleting: isPending,
    deleteError: error,
    resetDeleteError: reset,
  };
};
