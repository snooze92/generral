// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Activity/History/History.tsx
import React, { useCallback, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarSpacer from 'Components/Page/Toolbar/PageToolbarSpacer';
import ToolbarItem from 'Components/Page/Toolbar/ToolbarItem';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TablePager from 'Components/Table/TablePager';
import TableRow from 'Components/Table/TableRow';
import EpisodeQuality from 'Episode/EpisodeQuality';
import { Filter } from 'Filters/Filter';
import usePage from 'Helpers/Hooks/usePage';
import { align, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { EpisodeCell, EpisodeTitleCell, SeriesCell } from '../ActivityCells';
import useActivityEpisodes from '../useActivityEpisodes';
import HistoryEventTypeCell from './HistoryEventTypeCell';
import useHistory, { HistoryEventType } from './useHistory';
import styles from '../Activity.module.css';

const columns: Column[] = [
  { name: 'eventType', label: '', isVisible: true },
  { name: 'series', label: () => translate('Series'), isVisible: true },
  { name: 'episode', label: () => translate('Episode'), isVisible: true },
  {
    name: 'episodeTitle',
    label: () => translate('EpisodeTitle'),
    isVisible: true,
  },
  { name: 'sourceTitle', label: () => translate('SourceTitle'), isVisible: true },
  { name: 'quality', label: () => translate('Quality'), isVisible: true },
  { name: 'date', label: () => translate('Date'), isVisible: true },
];

// The API filters on one event type; the filter keys are the event types.
const FILTERS: Filter[] = [
  { key: 'all', label: () => translate('All'), filters: [] },
  { key: 'grabbed', label: () => translate('Grabbed'), filters: [] },
  {
    key: 'downloadFolderImported',
    label: () => translate('Imported'),
    filters: [],
  },
  { key: 'downloadFailed', label: () => translate('Failed'), filters: [] },
  { key: 'downloadIgnored', label: () => translate('Ignored'), filters: [] },
  {
    key: 'episodeFileRenamed',
    label: () => translate('Renamed'),
    filters: [],
  },
  {
    key: 'episodeFileDeleted',
    label: () => translate('Deleted'),
    filters: [],
  },
];

const NO_CUSTOM_FILTERS: never[] = [];

function History() {
  const { page, goToPage } = usePage('history');
  const [selectedFilterKey, setSelectedFilterKey] = useState<string>('all');
  const { records, totalPages, totalRecords, isLoading, isFetching, error } =
    useHistory(
      page,
      selectedFilterKey === 'all'
        ? undefined
        : (selectedFilterKey as HistoryEventType)
    );
  const episodeIds = useMemo(
    () => records.map((item) => item.episodeId),
    [records]
  );
  const { getSeries, getEpisodes } = useActivityEpisodes(episodeIds);

  const handleFilterSelect = useCallback(
    (key: string | number) => {
      setSelectedFilterKey(String(key));
      goToPage(1);
    },
    [goToPage]
  );

  return (
    <PageContent title={translate('History')}>
      <PageToolbar>
        <PageToolbarSpacer />

        <ToolbarItem id="filter" pinned={true}>
          <FilterMenu
            alignMenu={align.RIGHT}
            selectedFilterKey={selectedFilterKey}
            filters={FILTERS}
            customFilters={NO_CUSTOM_FILTERS}
            onFilterSelect={handleFilterSelect}
          />
        </ToolbarItem>
      </PageToolbar>

      <PageContentBody>
        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('HistoryLoadError')}</Alert>
        ) : null}

        {!isLoading && !error && !records.length ? (
          <Alert kind={kinds.INFO}>{translate('NoHistoryFound')}</Alert>
        ) : null}

        {records.length ? (
          <>
            <Table columns={columns}>
              <TableBody>
                {records.map((item) => {
                  const episodes = getEpisodes([item.episodeId]);

                  return (
                    <TableRow key={item.id}>
                      <HistoryEventTypeCell item={item} />
                      <SeriesCell series={getSeries(item.seriesId)} />
                      <EpisodeCell episodes={episodes} />
                      <EpisodeTitleCell episodes={episodes} />
                      <TableRowCell className={styles.sourceTitle}>
                        {item.sourceTitle}
                      </TableRowCell>
                      <TableRowCell className={styles.quality}>
                        <EpisodeQuality
                          quality={item.quality}
                          isCutoffNotMet={item.qualityCutoffNotMet}
                        />
                      </TableRowCell>
                      <RelativeDateCell date={item.date} includeTime={true} />
                    </TableRow>
                  );
                })}
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

export default History;
