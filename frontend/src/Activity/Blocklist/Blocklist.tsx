// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Activity/Blocklist/Blocklist.tsx and BlocklistRow.tsx
import React, { useMemo } from 'react';
import ProtocolLabel from 'Activity/Queue/ProtocolLabel';
import Alert from 'Components/Alert';
import IconButton from 'Components/Link/IconButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TablePager from 'Components/Table/TablePager';
import TableRow from 'Components/Table/TableRow';
import Episode from 'Episode/Episode';
import EpisodeQuality from 'Episode/EpisodeQuality';
import usePage from 'Helpers/Hooks/usePage';
import { icons, kinds } from 'Helpers/Props';
import Series from 'Series/Series';
import translate from 'Utilities/String/translate';
import { EpisodeCell, SeriesCell } from '../ActivityCells';
import useActivityEpisodes from '../useActivityEpisodes';
import useBlocklist, {
  Blocklist as BlocklistItem,
  useRemoveBlocklistItem,
} from './useBlocklist';
import styles from '../Activity.module.css';

const columns: Column[] = [
  { name: 'series', label: () => translate('Series'), isVisible: true },
  { name: 'episodes', label: () => translate('Episodes'), isVisible: true },
  { name: 'sourceTitle', label: () => translate('SourceTitle'), isVisible: true },
  { name: 'quality', label: () => translate('Quality'), isVisible: true },
  { name: 'protocol', label: () => translate('Protocol'), isVisible: true },
  { name: 'indexer', label: () => translate('Indexer'), isVisible: true },
  { name: 'date', label: () => translate('Date'), isVisible: true },
  { name: 'actions', label: '', isVisible: true },
];

interface BlocklistRowProps {
  item: BlocklistItem;
  series?: Series;
  episodes: Episode[];
}

function BlocklistRow({ item, series, episodes }: BlocklistRowProps) {
  const { removeBlocklistItem, isRemoving } = useRemoveBlocklistItem(item.id);
  const removeLabel = translate('RemoveFromBlocklist');

  return (
    <TableRow>
      <SeriesCell series={series} />
      <EpisodeCell episodes={episodes} />
      <TableRowCell className={styles.sourceTitle} title={item.message}>
        {item.sourceTitle}
      </TableRowCell>
      <TableRowCell className={styles.quality}>
        <EpisodeQuality quality={item.quality} />
      </TableRowCell>
      <TableRowCell className={styles.protocol}>
        <ProtocolLabel protocol={item.protocol} />
      </TableRowCell>
      <TableRowCell>{item.indexer}</TableRowCell>
      <RelativeDateCell date={item.date} includeTime={true} />
      <TableRowCell className={styles.actions}>
        <IconButton
          name={icons.REMOVE}
          title={removeLabel}
          aria-label={removeLabel}
          isSpinning={isRemoving}
          isDisabled={isRemoving}
          onPress={() => removeBlocklistItem()}
        />
      </TableRowCell>
    </TableRow>
  );
}

function Blocklist() {
  const { page, goToPage } = usePage('blocklist');
  const { records, totalPages, totalRecords, isLoading, isFetching, error } =
    useBlocklist(page);
  const episodeIds = useMemo(
    () => records.flatMap((item) => item.episodeIds),
    [records]
  );
  const { getSeries, getEpisodes } = useActivityEpisodes(episodeIds);

  return (
    <PageContent title={translate('Blocklist')}>
      <PageContentBody>
        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('BlocklistLoadError')}</Alert>
        ) : null}

        {!isLoading && !error && !records.length ? (
          <Alert kind={kinds.INFO}>{translate('NoBlocklistItems')}</Alert>
        ) : null}

        {records.length ? (
          <>
            <Table columns={columns}>
              <TableBody>
                {records.map((item) => (
                  <BlocklistRow
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

export default Blocklist;
