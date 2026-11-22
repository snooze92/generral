// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Activity/Blocklist/useBlocklist.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import ModelBase from 'App/ModelBase';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { QualityModel } from 'Quality/Quality';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

const PATH = '/blocklist';

/** The `/blocklist` resource (Sonarr's `BlocklistResource`). */
export interface Blocklist extends ModelBase {
  seriesId: number;
  episodeIds: number[];
  sourceTitle: string;
  quality: QualityModel;
  date: string;
  protocol: DownloadProtocol;
  indexer?: string;
  message?: string;
}

export const PAGE_SIZE = 20;

const useBlocklist = (page: number) =>
  usePagedApiQuery<Blocklist>({ path: PATH, page, pageSize: PAGE_SIZE });

export default useBlocklist;

export const useRemoveBlocklistItem = (id: number) => {
  const queryClient = useQueryClient();

  const { mutate, isPending } = useMutation<object, ApiError, void>({
    mutationFn: () =>
      fetchJson<object, void>({
        path: getQueryPath(`${PATH}/${id}`),
        method: 'DELETE',
        headers: {
          'X-Api-Key': window.Generral.apiKey,
          'X-Generral-Client': 'Generral',
        },
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [PATH] }),
  });

  return { removeBlocklistItem: mutate, isRemoving: isPending };
};
