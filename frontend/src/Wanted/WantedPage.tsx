// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Wanted/Missing/Missing.tsx and CutoffUnmet/CutoffUnmet.tsx
// Without custom filters, table options, monitor toggling and manual import.
import React, { useCallback, useMemo, useState } from 'react';
import { EpisodeCell, EpisodeTitleCell, SeriesCell } from 'Activity/ActivityCells';
import { useTrackedCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import PageMessage from 'Components/Page/PageMessage';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarSpacer from 'Components/Page/Toolbar/PageToolbarSpacer';
import ToolbarItem from 'Components/Page/Toolbar/ToolbarItem';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TablePager from 'Components/Table/TablePager';
import TableRow from 'Components/Table/TableRow';
import { Filter } from 'Filters/Filter';
import usePage from 'Helpers/Hooks/usePage';
import { align, icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import { CheckInputChanged } from 'typings/inputs';
import { SelectStateInputProps } from 'typings/props';
import translate from 'Utilities/String/translate';
import useWanted, { useHiddenUnmonitoredCount, WantedKind } from './useWanted';

const columns: Column[] = [
  {
    name: 'series.sortTitle',
    label: () => translate('Series'),
    isSortable: true,
    isVisible: true,
  },
  { name: 'episode', label: () => translate('Episode'), isVisible: true },
  {
    name: 'episodeTitle',
    label: () => translate('EpisodeTitle'),
    isVisible: true,
  },
  {
    name: 'episodes.airDateUtc',
    label: () => translate('AirDate'),
    isSortable: true,
    isVisible: true,
  },
];

const FILTERS: Filter[] = [
  { key: 'monitored', label: () => translate('Monitored'), filters: [] },
  { key: 'unmonitored', label: () => translate('Unmonitored'), filters: [] },
];

const NO_CUSTOM_FILTERS: never[] = [];

const KINDS = {
  missing: {
    title: () => translate('Missing'),
    loadError: () => translate('MissingLoadError'),
    noItems: () => translate('MissingNoItems'),
    searchAllCommand: 'MissingEpisodeSearch',
    searchAllTitle: () => translate('SearchForAllMissingEpisodes'),
    searchAllMessage: (totalRecords: number) =>
      translate('SearchForAllMissingEpisodesConfirmationCount', {
        totalRecords,
      }),
  },
  cutoffUnmet: {
    title: () => translate('CutoffUnmet'),
    loadError: () => translate('CutoffUnmetLoadError'),
    noItems: () => translate('CutoffUnmetNoItems'),
    searchAllCommand: 'CutoffUnmetEpisodeSearch',
    searchAllTitle: () => translate('SearchForCutoffUnmetEpisodes'),
    searchAllMessage: (totalRecords: number) =>
      translate('SearchForCutoffUnmetEpisodesConfirmationCount', {
        totalRecords,
      }),
  },
};

function WantedPage({ kind }: { kind: WantedKind }) {
  const strings = KINDS[kind];
  const { page, goToPage } = usePage(kind);
  const [selectedFilterKey, setSelectedFilterKey] = useState('monitored');
  const [sortKey, setSortKey] = useState('episodes.airDateUtc');
  const [sortDirection, setSortDirection] =
    useState<SortDirection>('ascending');
  const [checkedIds, setSelectedIds] = useState<number[]>([]);
  const [isConfirmSearchAllOpen, setIsConfirmSearchAllOpen] = useState(false);

  const {
    records,
    totalPages,
    totalRecords,
    isLoading,
    isFetching,
    error,
    refetch,
  } = useWanted(kind, {
    page,
    sortKey,
    sortDirection,
    monitored: selectedFilterKey === 'monitored',
  });

  const hiddenCount = useHiddenUnmonitoredCount(
    kind,
    selectedFilterKey === 'monitored'
  );

  // Rows of other pages or filters don't stay selected.
  const selectedIds = useMemo(
    () => checkedIds.filter((id) => records.some((r) => r.id === id)),
    [checkedIds, records]
  );

  const { execute, isExecuting } = useTrackedCommand('/command', () => {
    refetch();
  });

  const allSelected =
    records.length > 0 && selectedIds.length === records.length;
  const anySelected = selectedIds.length > 0;

  const handleSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) => {
      setSelectedIds(value ? records.map((r) => r.id) : []);
    },
    [records]
  );

  const handleSelectedChange = useCallback(
    ({ id, value }: SelectStateInputProps) => {
      setSelectedIds((ids) =>
        value ? [...ids, id] : ids.filter((i) => i !== id)
      );
    },
    []
  );

  const handleFilterSelect = useCallback(
    (key: string | number) => {
      setSelectedFilterKey(String(key));
      goToPage(1);
    },
    [goToPage]
  );

  const handleSortPress = useCallback(
    (name: string, direction?: SortDirection) => {
      setSortDirection(
        direction ??
          (name === sortKey && sortDirection === 'ascending'
            ? 'descending'
            : 'ascending')
      );
      setSortKey(name);
    },
    [sortKey, sortDirection]
  );

  const handleSearchPress = useCallback(() => {
    if (anySelected) {
      execute({ name: 'EpisodeSearch', episodeIds: selectedIds });
      setSelectedIds([]);
    } else {
      setIsConfirmSearchAllOpen(true);
    }
  }, [anySelected, execute, selectedIds]);

  const handleSearchAllConfirmed = useCallback(() => {
    execute({ name: strings.searchAllCommand });
    setIsConfirmSearchAllOpen(false);
  }, [execute, strings]);

  const series = useMemo(
    () => new Map(records.map((r) => [r.seriesId, r.series])),
    [records]
  );

  return (
    <PageContent title={strings.title()}>
      <PageToolbar>
        <ToolbarItem
          id="search"
          priority={1}
          groupId="left-a"
          label={
            anySelected ? translate('SearchSelected') : translate('SearchAll')
          }
          iconName={icons.SEARCH}
          isDisabled={isExecuting || (!anySelected && !totalRecords)}
          isSpinning={isExecuting}
          onPress={handleSearchPress}
        />

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
        <PageHeading scope={translate('Wanted')} title={strings.title()} />

        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{strings.loadError()}</Alert>
        ) : null}

        {!isLoading && !error && !records.length ? (
          <PageMessage>{strings.noItems()}</PageMessage>
        ) : null}

        {hiddenCount ? (
          <Alert kind={kinds.INFO}>
            {translate('UnmonitoredMissingEpisodesHidden', {
              count: hiddenCount,
            })}
          </Alert>
        ) : null}

        {records.length ? (
          <>
            <Table
              selectAll={true}
              allSelected={allSelected}
              allUnselected={!anySelected}
              columns={columns}
              sortKey={sortKey}
              sortDirection={sortDirection}
              onSelectAllChange={handleSelectAllChange}
              onSortPress={handleSortPress}
            >
              <TableBody>
                {records.map((episode) => (
                  <TableRow key={episode.id}>
                    <TableSelectCell
                      id={episode.id}
                      isSelected={selectedIds.includes(episode.id)}
                      onSelectedChange={handleSelectedChange}
                    />
                    <SeriesCell series={series.get(episode.seriesId)} />
                    <EpisodeCell episodes={[episode]} />
                    <EpisodeTitleCell episodes={[episode]} />
                    <RelativeDateCell date={episode.airDateUtc} />
                  </TableRow>
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

        <ConfirmModal
          isOpen={isConfirmSearchAllOpen}
          kind={kinds.DANGER}
          title={strings.searchAllTitle()}
          message={
            <div>
              <div>{strings.searchAllMessage(totalRecords ?? 0)}</div>
              <div>{translate('MassSearchCancelWarning')}</div>
            </div>
          }
          confirmLabel={translate('Search')}
          onConfirm={handleSearchAllConfirmed}
          onCancel={() => setIsConfirmSearchAllOpen(false)}
        />
      </PageContentBody>
    </PageContent>
  );
}

export default WantedPage;
