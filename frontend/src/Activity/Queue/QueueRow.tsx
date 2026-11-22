// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Activity/Queue/QueueRow.tsx
import React, { useState } from 'react';
import ProtocolLabel from 'Activity/Queue/ProtocolLabel';
import TimeLeftCell from 'Activity/Queue/TimeLeftCell';
import IconButton from 'Components/Link/IconButton';
import ProgressBar from 'Components/ProgressBar';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import Episode from 'Episode/Episode';
import EpisodeQuality from 'Episode/EpisodeQuality';
import { icons } from 'Helpers/Props';
import Series from 'Series/Series';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import { EpisodeCell, EpisodeTitleCell, SeriesCell } from '../ActivityCells';
import QueueStatus from './QueueStatus';
import RemoveQueueItemModal from './RemoveQueueItemModal';
import { Queue } from './useQueue';
import styles from '../Activity.module.css';

interface QueueRowProps {
  item: Queue;
  series?: Series;
  episodes: Episode[];
}

function QueueRow({ item, series, episodes }: QueueRowProps) {
  const {
    title,
    quality,
    protocol,
    downloadClient,
    status,
    size,
    sizeleft,
    timeleft,
    estimatedCompletionTime,
  } = item;
  const { showRelativeDates, shortDateFormat, timeFormat } =
    useUiSettingsValues();
  const [isRemoveOpen, setIsRemoveOpen] = useState(false);
  const progress = size ? 100 - (sizeleft / size) * 100 : 0;
  const removeLabel = translate('RemoveFromQueue');

  return (
    <TableRow>
      <TableRowCell className={styles.narrow}>
        <QueueStatus item={item} />
      </TableRowCell>

      <SeriesCell series={series} />
      <EpisodeCell episodes={episodes} />
      <EpisodeTitleCell episodes={episodes} />

      <TableRowCell className={styles.sourceTitle}>{title}</TableRowCell>

      <TableRowCell className={styles.quality}>
        <EpisodeQuality quality={quality} />
      </TableRowCell>

      <TableRowCell className={styles.protocol}>
        <ProtocolLabel protocol={protocol} />
      </TableRowCell>

      <TableRowCell>{downloadClient}</TableRowCell>

      <TimeLeftCell
        estimatedCompletionTime={estimatedCompletionTime}
        timeLeft={timeleft}
        status={status}
        size={size}
        sizeLeft={sizeleft}
        showRelativeDates={showRelativeDates}
        shortDateFormat={shortDateFormat}
        timeFormat={timeFormat}
      />

      <TableRowCell className={styles.progress}>
        <ProgressBar
          ariaLabel={translate('DownloadProgressForTitle', { title })}
          progress={progress}
          title={`${progress.toFixed(1)}%`}
        />
      </TableRowCell>

      <TableRowCell className={styles.size}>{formatBytes(size)}</TableRowCell>

      <TableRowCell className={styles.actions}>
        <IconButton
          name={icons.REMOVE}
          title={removeLabel}
          aria-label={removeLabel}
          onPress={() => setIsRemoveOpen(true)}
        />
      </TableRowCell>

      <RemoveQueueItemModal
        isOpen={isRemoveOpen}
        item={item}
        onModalClose={() => setIsRemoveOpen(false)}
      />
    </TableRow>
  );
}

export default QueueRow;
