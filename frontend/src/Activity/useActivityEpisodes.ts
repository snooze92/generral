import { useMemo } from 'react';
import Episode from 'Episode/Episode';
import { PATH as EPISODE_PATH } from 'Episode/useEpisodes';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Series from 'Series/Series';
import useSeries from 'Series/useSeries';
import padNumber from 'Utilities/Number/padNumber';

export function formatEpisodeNumber({
  seasonNumber,
  episodeNumber,
}: Pick<Episode, 'seasonNumber' | 'episodeNumber'>) {
  return `${seasonNumber}x${padNumber(episodeNumber, 2)}`;
}

/** The series and episodes referenced by a page of activity records, fetched in one request. */
const useActivityEpisodes = (episodeIds: number[]) => {
  const ids = useMemo(
    () => [...new Set(episodeIds)].sort((a, b) => a - b),
    [episodeIds]
  );
  const { data: allSeries } = useSeries();
  const { data: episodes } = useApiQuery<Episode[]>({
    path: EPISODE_PATH,
    queryParams: { episodeIds: ids },
    queryOptions: { enabled: ids.length > 0 },
  });

  return useMemo(() => {
    const seriesById = new Map<number, Series>(
      allSeries.map((series) => [series.id, series])
    );
    const episodesById = new Map<number, Episode>(
      (episodes ?? []).map((episode) => [episode.id, episode])
    );

    return {
      getSeries: (id?: number) =>
        id === undefined ? undefined : seriesById.get(id),
      getEpisodes: (episodeIdsOfRecord: number[]) =>
        episodeIdsOfRecord
          .map((id) => episodesById.get(id))
          .filter((episode): episode is Episode => !!episode),
    };
  }, [allSeries, episodes]);
};

export default useActivityEpisodes;
