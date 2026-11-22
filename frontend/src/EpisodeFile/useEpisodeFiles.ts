import { useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import ModelBase from 'App/ModelBase';
import { PATH as EPISODE_PATH } from 'Episode/useEpisodes';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { QualityModel } from 'Quality/Quality';
import { PATH as SERIES_PATH } from 'Series/useSeries';

export const PATH = '/episodefile';

/** The `/episodefile` resource (Sonarr's `EpisodeFileResource`). */
export interface EpisodeFile extends ModelBase {
  seriesId: number;
  seasonNumber: number;
  relativePath: string;
  path: string;
  size: number;
  dateAdded: string;
  sceneName?: string;
  releaseGroup?: string;
  quality: QualityModel;
  qualityCutoffNotMet: boolean;
}

const NO_FILES: EpisodeFile[] = [];

const useEpisodeFiles = (seriesId: number) => {
  const { data = NO_FILES, ...result } = useApiQuery<EpisodeFile[]>({
    path: PATH,
    queryParams: { seriesId },
  });

  const byId = useMemo(
    () => new Map(data.map((file) => [file.id, file])),
    [data]
  );

  return { ...result, data, byId };
};

export default useEpisodeFiles;

/** Deletes the file (to the recycle bin), then refetches the files, episodes and series. */
export const useDeleteEpisodeFile = (id: number) => {
  const queryClient = useQueryClient();

  const { mutate, isPending } = useApiMutation<void, void>({
    path: `${PATH}/${id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        [PATH, EPISODE_PATH, SERIES_PATH].forEach((path) =>
          queryClient.invalidateQueries({ queryKey: [path] })
        );
      },
    },
  });

  return { deleteEpisodeFile: mutate, isDeleting: isPending };
};
