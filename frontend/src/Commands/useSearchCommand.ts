import { useInvalidateQueue } from 'Activity/Queue/useQueue';
import { useTrackedCommand } from './useCommands';

export type SearchCommand =
  | { name: 'EpisodeSearch'; episodeIds: number[] }
  | { name: 'SeasonSearch'; seriesId: number; seasonNumber: number }
  | { name: 'SeriesSearch'; seriesId: number };

/** Runs an automatic search command; grabs show up in the queue once it has finished. */
const useSearchCommand = () => {
  const invalidateQueue = useInvalidateQueue();
  const { execute, isExecuting } = useTrackedCommand(
    '/command',
    invalidateQueue
  );

  return {
    search: execute as (command: SearchCommand) => void,
    isSearching: isExecuting,
  };
};

export default useSearchCommand;
