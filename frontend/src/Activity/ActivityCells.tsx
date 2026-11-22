import React from 'react';
import Link from 'Components/Link/Link';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Episode from 'Episode/Episode';
import Series from 'Series/Series';
import getSeriesPath from 'Utilities/Series/getSeriesPath';
import { formatEpisodeNumber } from './useActivityEpisodes';
import styles from './Activity.module.css';

export function SeriesCell({ series }: { series?: Series }) {
  return (
    <TableRowCell className={styles.series}>
      {series ? <Link to={getSeriesPath(series)}>{series.title}</Link> : null}
    </TableRowCell>
  );
}

export function EpisodeCell({ episodes }: { episodes: Episode[] }) {
  return (
    <TableRowCell className={styles.episode}>
      {episodes.map(formatEpisodeNumber).join(', ')}
    </TableRowCell>
  );
}

export function EpisodeTitleCell({ episodes }: { episodes: Episode[] }) {
  return (
    <TableRowCell className={styles.episodeTitle}>
      {episodes.length === 1 ? episodes[0].title : null}
    </TableRowCell>
  );
}
