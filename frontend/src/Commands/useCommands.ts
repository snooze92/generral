import { useMutation } from '@tanstack/react-query';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import Command from './Command';

export const POLL_INTERVAL = 1000;

const headers = {
  'X-Api-Key': window.Generral.apiKey,
  'X-Generral-Client': 'Generral',
};

const isFinished = (command: Command) =>
  command.status === 'completed' || command.status === 'failed';

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * POSTs to an endpoint that enqueues a command, then polls `GET /command/{id}` until the command
 * has finished (there is no SignalR to push command updates yet).
 */
export const useTrackedCommand = (
  path: string,
  onFinished?: (command: Command) => void
) => {
  const { mutate, isPending } = useMutation<Command, ApiError, object | void>({
    mutationFn: async (body) => {
      let command = await fetchJson<Command, object>({
        path: getQueryPath(path),
        method: 'POST',
        headers,
        body: body ?? undefined,
      });

      while (!isFinished(command)) {
        await delay(POLL_INTERVAL);
        command = await fetchJson<Command, void>({
          path: getQueryPath(`/command/${command.id}`),
          headers,
        });
      }

      return command;
    },
    onSuccess: onFinished,
  });

  return { execute: mutate, isExecuting: isPending };
};
