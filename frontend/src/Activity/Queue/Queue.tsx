// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Activity/Queue/Queue.tsx
import React, { useMemo } from 'react';
import { useTrackedCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import ToolbarItem from 'Components/Page/Toolbar/ToolbarItem';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TablePager from 'Components/Table/TablePager';
import usePage from 'Helpers/Hooks/usePage';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import useActivityEpisodes from '../useActivityEpisodes';
import QueueRow from './QueueRow';
import useQueue, { useInvalidateQueue } from './useQueue';

const columns: Column[] = [
  { name: 'status', label: '', isVisible: true },
  { name: 'series', label: () => translate('Series'), isVisible: true },
  { name: 'episode', label: () => translate('Episode'), isVisible: true },
  {
    name: 'episodeTitle',
    label: () => translate('EpisodeTitle'),
    isVisible: true,
  },
  { name: 'title', label: () => translate('Release'), isVisible: true },
  { name: 'quality', label: () => translate('Quality'), isVisible: true },
  { name: 'protocol', label: () => translate('Protocol'), isVisible: true },
  {
    name: 'downloadClient',
    label: () => translate('DownloadClient'),
    isVisible: true,
  },
  { name: 'timeleft', label: () => translate('TimeLeft'), isVisible: true },
  { name: 'progress', label: () => translate('Progress'), isVisible: true },
  { name: 'size', label: () => translate('Size'), isVisible: true },
  { name: 'actions', label: '', isVisible: true },
];

function Queue() {
  const { page, goToPage } = usePage('queue');
  const { records, totalPages, totalRecords, isLoading, isFetching, error } =
    useQueue(page);
  const episodeIds = useMemo(
    () => records.flatMap((item) => item.episodeIds),
    [records]
  );
  const { getSeries, getEpisodes } = useActivityEpisodes(episodeIds);

  const invalidateQueue = useInvalidateQueue();
  const { execute: refresh, isExecuting: isRefreshing } = useTrackedCommand(
    '/command',
    invalidateQueue
  );

  return (
    <PageContent title={translate('Queue')}>
      <PageToolbar>
        <ToolbarItem
          id="refresh"
          priority={1}
          groupId="left"
          label={translate('Refresh')}
          iconName={icons.REFRESH}
          isSpinning={isRefreshing}
          onPress={() => refresh({ name: 'RefreshMonitoredDownloads' })}
        />
      </PageToolbar>

      <PageContentBody>
        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('QueueLoadError')}</Alert>
        ) : null}

        {!isLoading && !error && !records.length ? (
          <Alert kind={kinds.INFO}>{translate('QueueIsEmpty')}</Alert>
        ) : null}

        {records.length ? (
          <>
            <Table columns={columns}>
              <TableBody>
                {records.map((item) => (
                  <QueueRow
                    key={item.id}
                    item={item}
                    series={getSeries(item.seriesId)}
                    episodes={getEpisodes(item.episodeIds)}
                  />
                ))}
              </TableBody>
            </Table>

            <TablePager
              page={page}
              totalPages={totalPages}
              totalRecords={totalRecords}
              isFetching={isFetching}
              onPageSelect={goToPage}
            />
          </>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default Queue;
