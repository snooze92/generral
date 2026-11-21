import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import ProgressBar from 'Components/ProgressBar';
import StatusIndicator from 'Components/StatusIndicator';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { sizes } from 'Helpers/Props';
import SeriesIndexTags from 'Series/Index/SeriesIndexTags';
import Series from 'Series/Series';
import { getSeriesStatusDetails } from 'Series/SeriesStatus';
import { useToggleSeriesMonitored } from 'Series/useSeries';
import getProgressBarKind from 'Utilities/Series/getProgressBarKind';
import getSeriesPath from 'Utilities/Series/getSeriesPath';
import translate from 'Utilities/String/translate';
import styles from './SeriesIndexRow.module.css';

interface SeriesIndexRowProps {
  series: Series;
}

function SeriesIndexRow({ series }: SeriesIndexRowProps) {
  const { id, title, monitored, status, seriesType, tags, statistics } =
    series;
  const {
    seasonCount = 0,
    episodeCount = 0,
    episodeFileCount = 0,
    totalEpisodeCount = 0,
  } = statistics ?? {};

  const statusDetails = getSeriesStatusDetails(status);
  const { toggleSeriesMonitored, isTogglingSeriesMonitored } =
    useToggleSeriesMonitored(id);

  const handleMonitoredPress = useCallback(
    () => toggleSeriesMonitored(!monitored),
    [monitored, toggleSeriesMonitored]
  );

  const progress = episodeCount ? (episodeFileCount / episodeCount) * 100 : 100;

  return (
    <TableRow>
      <TableRowCell className={styles.status}>
        <MonitorToggleButton
          className={styles.statusIcon}
          monitored={monitored}
          isSaving={isTogglingSeriesMonitored}
          onPress={handleMonitoredPress}
        />

        <StatusIndicator
          className={styles.statusIcon}
          label={statusDetails.message}
          title={`${statusDetails.title}: ${statusDetails.message}`}
        >
          <Icon name={statusDetails.icon} />
        </StatusIndicator>
      </TableRowCell>

      <TableRowCell className={styles.title}>
        <Link className={styles.titleLink} to={getSeriesPath(series)}>
          {title}
        </Link>
      </TableRowCell>

      <TableRowCell className={styles.seriesType}>
        {seriesType === 'daily' ? translate('Daily') : translate('Standard')}
      </TableRowCell>

      <TableRowCell className={styles.seasonCount}>{seasonCount}</TableRowCell>

      <TableRowCell className={styles.episodeProgress}>
        <ProgressBar
          progress={progress}
          kind={getProgressBarKind(status, monitored, progress, false)}
          size={sizes.MEDIUM}
          showText={true}
          text={`${episodeFileCount} / ${episodeCount}`}
          title={translate('SeriesProgressBarText', {
            episodeFileCount,
            episodeCount,
            totalEpisodeCount,
            downloadingCount: 0,
          })}
          width={125}
        />
      </TableRowCell>

      <TableRowCell className={styles.tags}>
        <SeriesIndexTags tags={tags} />
      </TableRowCell>
    </TableRow>
  );
}

export default SeriesIndexRow;
