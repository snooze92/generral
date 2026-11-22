import { useMutation, useQueryClient } from '@tanstack/react-query';
import Command from 'Commands/Command';
import { waitForCommand } from 'Commands/useCommands';
import { addOrUpdateQueryClientItem } from 'Helpers/Hooks/useApiMutation';
import Mapping from 'Mapping/Mapping';
import Series from 'Series/Series';
import { PATH as SERIES_PATH } from 'Series/useSeries';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

const headers = {
  'X-Api-Key': window.Generral.apiKey,
  'X-Generral-Client': 'Generral',
};

export type NewSeries = Pick<
  Series,
  | 'title'
  | 'rootFolderPath'
  | 'monitored'
  | 'monitorNewItems'
  | 'seasonFolder'
  | 'seriesType'
  | 'qualityProfileId'
  | 'tags'
>;

export interface AddSeriesRequest {
  series: NewSeries;
  mapping: Pick<Mapping, 'name' | 'sourceId' | 'definition'>;
}

/** Which request failed; a failed mapping rolls the series back, unless that fails too. */
export class AddSeriesError extends Error {
  public step: 'series' | 'mapping';
  public error: ApiError;
  public series?: Series;

  public constructor(
    step: 'series' | 'mapping',
    error: ApiError,
    series?: Series
  ) {
    super(error.message);
    this.step = step;
    this.error = error;
    this.series = series;
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

const post = <T, TData>(path: string, body: TData) =>
  fetchJson<T, TData>({
    path: getQueryPath(path),
    method: 'POST',
    headers,
    body,
  });

/** The `ApplyMappings` command `POST /mapping` queued, which the response doesn't include. */
const findApplyCommand = async (mappingId: number) => {
  const commands = await fetchJson<Command[], void>({
    path: getQueryPath('/command'),
    headers,
  });

  return commands.find(
    (command) =>
      command.name === 'ApplyMappings' && command.body.mappingId === mappingId
  );
};

/**
 * POST /series then POST /mapping, and waits for the first apply so the series page opens with its
 * episodes. If the mapping is rejected the new series is deleted again, so retrying starts over.
 */
const useAddSeries = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error, reset } = useMutation<
    Series,
    AddSeriesError,
    AddSeriesRequest
  >({
    mutationFn: async ({ series, mapping }) => {
      let created: Series;

      try {
        created = await post<Series, NewSeries & { seasons: [] }>(
          SERIES_PATH,
          { ...series, seasons: [] }
        );
      } catch (e) {
        throw new AddSeriesError('series', e as ApiError);
      }

      let saved: Mapping;

      try {
        saved = await post<Mapping, Partial<Mapping>>('/mapping', {
          ...mapping,
          seriesId: created.id,
          priority: 0,
          enabled: true,
        });
      } catch (e) {
        try {
          await fetchJson<void, void>({
            path: getQueryPath(`${SERIES_PATH}/${created.id}`),
            method: 'DELETE',
            headers,
          });
        } catch {
          throw new AddSeriesError('mapping', e as ApiError, created);
        }

        throw new AddSeriesError('mapping', e as ApiError);
      }

      try {
        const command = await findApplyCommand(saved.id);

        if (command) {
          await waitForCommand(command);
        }
      } catch {
        // The series exists either way; its page shows the episodes once applied.
      }

      try {
        return await fetchJson<Series, void>({
          path: getQueryPath(`${SERIES_PATH}/${created.id}`),
          headers,
        });
      } catch {
        return created;
      }
    },
    onSuccess: (series) => {
      queryClient.setQueryData<Series[]>([SERIES_PATH], (oldData) =>
        addOrUpdateQueryClientItem(oldData, series, 'id')
      );
      queryClient.invalidateQueries({ queryKey: ['/tag/detail'] });
    },
    onError: (e) => {
      if (e.series) {
        queryClient.invalidateQueries({ queryKey: [SERIES_PATH] });
      }
    },
  });

  return { addSeries: mutate, isAdding: isPending, addError: error, reset };
};

export default useAddSeries;
