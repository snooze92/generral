import React, { useCallback, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import ToolbarItem from 'Components/Page/Toolbar/ToolbarItem';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { icons, kinds, sortDirections } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import NoSeries from 'Series/NoSeries';
import Series from 'Series/Series';
import useSeries from 'Series/useSeries';
import translate from 'Utilities/String/translate';
import SeriesIndexRow from './SeriesIndexRow';

const columns: Column[] = [
  { name: 'status', label: '', isVisible: true },
  {
    name: 'sortTitle',
    label: () => translate('SeriesTitle'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'seriesType',
    label: () => translate('Type'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'seasonCount',
    label: () => translate('Seasons'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'episodeProgress',
    label: () => translate('Episodes'),
    isSortable: true,
    isVisible: true,
  },
  { name: 'tags', label: () => translate('Tags'), isVisible: true },
];

type SortKey = 'sortTitle' | 'seriesType' | 'seasonCount' | 'episodeProgress';

function sortValue(series: Series, sortKey: SortKey): string | number {
  const { seasonCount = 0, episodeCount = 0, episodeFileCount = 0 } =
    series.statistics ?? {};

  switch (sortKey) {
    case 'seriesType':
      return series.seriesType;
    case 'seasonCount':
      return seasonCount;
    case 'episodeProgress':
      return episodeCount ? episodeFileCount / episodeCount : 1;
    default:
      return series.sortTitle;
  }
}

function SeriesIndex() {
  const { data: allSeries, isLoading, error } = useSeries();
  const [sortKey, setSortKey] = useState<SortKey>('sortTitle');
  const [sortDirection, setSortDirection] = useState<SortDirection>(
    sortDirections.ASCENDING
  );

  const items = useMemo(() => {
    const direction = sortDirection === sortDirections.ASCENDING ? 1 : -1;

    return [...allSeries].sort((a, b) => {
      const left = sortValue(a, sortKey);
      const right = sortValue(b, sortKey);
      const result =
        typeof left === 'number' && typeof right === 'number'
          ? left - right
          : String(left).localeCompare(String(right), undefined, {
              numeric: true,
            });

      return (
        result * direction || a.sortTitle.localeCompare(b.sortTitle)
      );
    });
  }, [allSeries, sortKey, sortDirection]);

  const handleSortPress = useCallback(
    (name: string, direction?: SortDirection) => {
      if (direction) {
        setSortDirection(direction);
      } else if (name === sortKey) {
        setSortDirection((current) =>
          current === sortDirections.ASCENDING
            ? sortDirections.DESCENDING
            : sortDirections.ASCENDING
        );
      } else {
        setSortDirection(sortDirections.ASCENDING);
      }

      setSortKey(name as SortKey);
    },
    [sortKey]
  );

  return (
    <PageContent title={translate('Series')}>
      <PageToolbar>
        <ToolbarItem
          id="add"
          priority={1}
          groupId="left"
          label={translate('AddNewSeries')}
          iconName={icons.ADD}
          to="/add/new"
        />
      </PageToolbar>

      <PageContentBody>
        <PageHeading title={translate('Series')} />

        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('SeriesLoadError')}</Alert>
        ) : null}

        {!isLoading && !error && !allSeries.length ? <NoSeries /> : null}

        {items.length ? (
          <Table
            columns={columns}
            sortKey={sortKey}
            sortDirection={sortDirection}
            onSortPress={handleSortPress}
          >
            <TableBody>
              {items.map((series) => (
                <SeriesIndexRow key={series.id} series={series} />
              ))}
            </TableBody>
          </Table>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default SeriesIndex;
