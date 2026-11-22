// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/System/Status/Health/useHealth.ts, Status/DiskSpace/useDiskSpace.ts,
// Tasks/useTasks.ts, Backup/useBackups.ts, Logs/useLogFiles.ts and Events/useEvents.ts
import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import ModelBase from 'App/ModelBase';
import Command from 'Commands/Command';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface Health {
  source: string;
  type: 'ok' | 'notice' | 'warning' | 'error';
  message: string;
  wikiUrl: string;
}

export interface DiskSpace {
  path: string;
  label: string;
  freeSpace: number;
  totalSpace: number;
}

export interface Task extends ModelBase {
  name: string;
  taskName: string;
  interval: number;
  lastExecution?: string;
  lastStartTime?: string;
  nextExecution?: string;
  lastDuration?: string;
}

export interface Backup extends ModelBase {
  name: string;
  path: string;
  type: 'manual' | 'scheduled';
  size: number;
  time: string;
}

export interface LogFile extends ModelBase {
  filename: string;
  lastWriteTime: string;
  contentsUrl: string;
  downloadUrl: string;
}

export interface LogEvent extends ModelBase {
  time: string;
  level: 'info' | 'warn' | 'error' | 'fatal';
  logger: string;
  message: string;
  exception?: string;
}

export interface Paged<T> {
  page: number;
  pageSize: number;
  totalRecords: number;
  records: T[];
}

const useList = <T>(path: string, refetchInterval?: number) => {
  const result = useApiQuery<T[]>({
    path,
    queryOptions: refetchInterval ? { refetchInterval } : undefined,
  });

  return { ...result, data: result.data ?? [] };
};

export const useHealth = () => useList<Health>('/health', 60 * 1000);

export const useDiskSpace = () => useList<DiskSpace>('/diskspace');

export const useTasks = () => useList<Task>('/system/task', 5 * 1000);

export const useQueuedCommands = () => useList<Command>('/command', 5 * 1000);

export const useBackups = () => useList<Backup>('/system/backup');

export const useLogFiles = () => useList<LogFile>('/log/file');

export const useEvents = (page: number, level: string) =>
  useApiQuery<Paged<LogEvent>>({
    path: '/log',
    queryParams: { page, pageSize: 50, level, sortDirection: 'descending' },
  });

export const useInvalidate = (path: string) => {
  const queryClient = useQueryClient();

  return useCallback(() => {
    queryClient.invalidateQueries({ queryKey: [path] });
  }, [queryClient, path]);
};

export const useDeleteBackup = (id: number) => {
  const invalidate = useInvalidate('/system/backup');

  return useApiMutation<object, void>({
    path: `/system/backup/${id}`,
    method: 'DELETE',
    mutationOptions: { onSuccess: invalidate },
  });
};

export const useRestoreBackup = (id: number) =>
  useApiMutation<{ restartRequired: boolean }, void>({
    path: `/system/backup/restore/${id}`,
    method: 'POST',
  });

/** The API key query parameter lets a plain link (new tab, download) authenticate like the SPA. */
export const withApiKey = (url: string) =>
  `${url}?apikey=${encodeURIComponent(window.Generral.apiKey)}`;
