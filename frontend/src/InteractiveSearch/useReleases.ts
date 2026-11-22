// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/InteractiveSearch/useReleases.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { PATH as QUEUE_PATH } from 'Activity/Queue/useQueue';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { QualityModel } from 'Quality/Quality';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

const PATH = '/release';

/** The `/release` resource (Sonarr's `ReleaseResource` without scene/language/custom-format fields). */
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
}

const NO_RELEASES: Release[] = [];

/** Searches the indexers when the modal opens; results aren't cached client-side (each open searches again). */
const useReleases = (episodeId: number) => {
  const result = useApiQuery<Release[]>({
    path: PATH,
    queryParams: { episodeId },
    queryOptions: { gcTime: 0, staleTime: Infinity, retry: false },
  });

  return { ...result, data: result.data ?? NO_RELEASES };
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
        headers: {
          'X-Api-Key': window.Generral.apiKey,
          'X-Generral-Client': 'Generral',
        },
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
