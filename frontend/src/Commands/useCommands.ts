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

/** Polls `GET /command/{id}` until the command has finished (no SignalR yet). */
export const waitForCommand = async (command: Command) => {
  let current = command;

  while (!isFinished(current)) {
    await delay(POLL_INTERVAL);
    current = await fetchJson<Command, void>({
      path: getQueryPath(`/command/${current.id}`),
      headers,
    });
  }

  return current;
};

/**
 * POSTs to an endpoint that enqueues a command, then waits until the command has finished.
 */
export const useTrackedCommand = (
  path: string,
  onFinished?: (command: Command) => void
) => {
  const { mutate, isPending } = useMutation<Command, ApiError, object | void>({
    mutationFn: async (body) => {
      const command = await fetchJson<Command, object>({
        path: getQueryPath(path),
        method: 'POST',
        headers,
        body: body ?? undefined,
      });

      return waitForCommand(command);
    },
    onSuccess: onFinished,
  });

  return { execute: mutate, isExecuting: isPending };
};
