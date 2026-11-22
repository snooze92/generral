// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Activity/Queue/useQueue.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import ModelBase from 'App/ModelBase';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import usePagedApiQuery, {
  PagedQueryResponse,
} from 'Helpers/Hooks/usePagedApiQuery';
import { QualityModel } from 'Quality/Quality';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

export const PATH = '/queue';

/** While visible, the queue is refetched this often (there is no SignalR yet). */
export const QUEUE_REFRESH_INTERVAL = 5000;

export type QueueTrackedDownloadStatus = 'ok' | 'warning' | 'error';

export type QueueTrackedDownloadState =
  | 'downloading'
  | 'importBlocked'
  | 'importPending'
  | 'importing'
  | 'imported'
  | 'failedPending'
  | 'failed'
  | 'ignored';

export interface StatusMessage {
  title: string;
  messages: string[];
}

/** The `/queue` resource (Sonarr's `QueueResource`). */
export interface Queue extends ModelBase {
  seriesId?: number;
  episodeId?: number;
  episodeIds: number[];
  quality: QualityModel;
  size: number;
  title: string;
  sizeleft: number;
  timeleft?: string;
  estimatedCompletionTime?: string;
  added?: string;
  status: string;
  trackedDownloadStatus: QueueTrackedDownloadStatus;
  trackedDownloadState: QueueTrackedDownloadState;
  statusMessages: StatusMessage[];
  errorMessage?: string;
  downloadId: string;
  protocol: DownloadProtocol;
  downloadClient: string;
  indexer?: string;
  outputPath?: string;
}

const headers = {
  'X-Api-Key': window.Generral.apiKey,
  'X-Generral-Client': 'Generral',
};

export const PAGE_SIZE = 20;

const useQueue = (page: number) =>
  usePagedApiQuery<Queue>({
    path: PATH,
    page,
    pageSize: PAGE_SIZE,
    queryOptions: { refetchInterval: QUEUE_REFRESH_INTERVAL },
  });

export default useQueue;

const NO_QUEUE: Queue[] = [];

/** The whole queue, for showing download status next to episodes; polls only while something is queued. */
export const useQueueDetails = () => {
  const { data } = useApiQuery<PagedQueryResponse<Queue>>({
    path: PATH,
    queryParams: { pageSize: 1000 },
    queryOptions: {
      refetchInterval: (query) =>
        query.state.data?.totalRecords ? QUEUE_REFRESH_INTERVAL : false,
    },
  });

  return data?.records ?? NO_QUEUE;
};

export const useInvalidateQueue = () => {
  const queryClient = useQueryClient();

  return useCallback(
    () => queryClient.invalidateQueries({ queryKey: [PATH] }),
    [queryClient]
  );
};

export interface RemoveQueueItemOptions {
  removeFromClient: boolean;
  blocklist: boolean;
}

export const useRemoveQueueItem = (id: number) => {
  const invalidateQueue = useInvalidateQueue();

  const { mutate, isPending, error, reset } = useMutation<
    object,
    ApiError,
    RemoveQueueItemOptions
  >({
    mutationFn: ({ removeFromClient, blocklist }) =>
      fetchJson<object, void>({
        path: getQueryPath(
          `${PATH}/${id}?removeFromClient=${removeFromClient}&blocklist=${blocklist}`
        ),
        method: 'DELETE',
        headers,
      }),
    onSuccess: invalidateQueue,
  });

  return {
    removeQueueItem: mutate,
    isRemoving: isPending,
    removeError: error,
    resetRemoveError: reset,
  };
};
