import classNames from 'classnames';
import { Lock, LockOpen } from 'lucide-react';
import React, { useCallback, useState } from 'react';
import { getQueueStatus } from 'Activity/Queue/QueueStatus';
import { Queue } from 'Activity/Queue/useQueue';
import useSearchCommand from 'Commands/useSearchCommand';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import Episode from 'Episode/Episode';
import EpisodeQuality from 'Episode/EpisodeQuality';
import { useToggleEpisodeLocked } from 'Episode/useEpisodes';
import { EpisodeFile, useDeleteEpisodeFile } from 'EpisodeFile/useEpisodeFiles';
import { icons, kinds } from 'Helpers/Props';
import InteractiveSearchModal from 'InteractiveSearch/InteractiveSearchModal';
import isBefore from 'Utilities/Date/isBefore';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './EpisodeRow.module.css';

interface EpisodeRowProps {
  episode: Episode;
  episodeFile?: EpisodeFile;
  queueItem?: Queue;
  seriesMonitored: boolean;
  isSaving: boolean;
  onMonitorEpisodePress: (
    episodeId: number,
    monitored: boolean,
    options: { shiftKey: boolean }
  ) => void;
}

interface EpisodeStatusProps {
  episode: Episode;
  episodeFile?: EpisodeFile;
  queueItem?: Queue;
}

function EpisodeStatus({ episode, episodeFile, queueItem }: EpisodeStatusProps) {
  if (queueItem) {
    const { iconName, iconKind, title } = getQueueStatus(queueItem);
    const progress = queueItem.size
      ? 100 - (queueItem.sizeleft / queueItem.size) * 100
      : 0;

    return (
      <span title={`${title} (${progress.toFixed(1)}%)`}>
        <Icon name={iconName} kind={iconKind} title={title} />
      </span>
    );
  }

  if (episode.status === 'orphaned') {
    return (
      <Label kind={kinds.DEFAULT} title={translate('OrphanedEpisodeHelpText')}>
        {translate('Orphaned')}
      </Label>
    );
  }

  if (episode.hasFile) {
    return episodeFile ? (
      <span className={styles.file}>
        <EpisodeQuality
          quality={episodeFile.quality}
          size={episodeFile.size}
          isCutoffNotMet={episodeFile.qualityCutoffNotMet}
        />
        <span className={styles.size}>{formatBytes(episodeFile.size)}</span>
      </span>
    ) : null;
  }

  if (!episode.airDateUtc || !isBefore(episode.airDateUtc)) {
    return <Label kind={kinds.INFO}>{translate('Unaired')}</Label>;
  }

  return (
    <Label kind={episode.monitored ? kinds.DANGER : kinds.WARNING}>
      {translate('Missing')}
    </Label>
  );
}

function EpisodeRow({
  episode,
  episodeFile,
  queueItem,
  seriesMonitored,
  isSaving,
  onMonitorEpisodePress,
}: EpisodeRowProps) {
  const { id, episodeNumber, title, monitored, numberLocked, status } =
    episode;
  const { toggleEpisodeLocked, isTogglingLocked } = useToggleEpisodeLocked(id);
  const { search, isSearching } = useSearchCommand();
  const [isInteractiveSearchOpen, setIsInteractiveSearchOpen] = useState(false);
  const [isDeleteFileOpen, setIsDeleteFileOpen] = useState(false);
  const { deleteEpisodeFile, isDeleting } = useDeleteEpisodeFile(
    episodeFile?.id ?? 0
  );

  const handleMonitorPress = useCallback(
    (value: boolean, options: { shiftKey: boolean }) =>
      onMonitorEpisodePress(id, value, options),
    [id, onMonitorEpisodePress]
  );

  const handleLockPress = useCallback(
    () => toggleEpisodeLocked(!numberLocked),
    [numberLocked, toggleEpisodeLocked]
  );

  const lockTitle = numberLocked
    ? translate('EpisodeNumberLockedClickToUnlock')
    : translate('EpisodeNumberUnlockedClickToLock');

  return (
    <TableRow className={status === 'orphaned' ? styles.orphaned : styles.row}>
      <TableRowCell className={styles.monitored}>
        <MonitorToggleButton
          monitored={monitored}
          isDisabled={!seriesMonitored && !monitored}
          isSaving={isSaving}
          onPress={handleMonitorPress}
        />
      </TableRowCell>

      <TableRowCell className={styles.episodeNumber}>
        <span className={styles.number}>{episodeNumber}</span>

        <IconButton
          className={classNames(
            styles.lockButton,
            numberLocked && styles.isLocked
          )}
          name={numberLocked ? Lock : LockOpen}
          title={lockTitle}
          aria-label={lockTitle}
          aria-pressed={numberLocked}
          isSpinning={isTogglingLocked}
          isDisabled={isTogglingLocked}
          onPress={handleLockPress}
        />
      </TableRowCell>

      <TableRowCell className={styles.title}>{title}</TableRowCell>

      <RelativeDateCell
        className={styles.airDateUtc}
        date={episode.airDateUtc}
        includeTime={true}
      />

      <TableRowCell className={styles.status}>
        <EpisodeStatus
          episode={episode}
          episodeFile={episodeFile}
          queueItem={queueItem}
        />
      </TableRowCell>

      <TableRowCell className={styles.actions}>
        <SpinnerIconButton
          name={icons.SEARCH}
          title={translate('AutomaticSearch')}
          aria-label={translate('AutomaticSearch')}
          isSpinning={isSearching}
          onPress={() => search({ name: 'EpisodeSearch', episodeIds: [id] })}
        />

        <IconButton
          name={icons.INTERACTIVE}
          title={translate('InteractiveSearch')}
          aria-label={translate('InteractiveSearch')}
          onPress={() => setIsInteractiveSearchOpen(true)}
        />

        <InteractiveSearchModal
          isOpen={isInteractiveSearchOpen}
          episodeId={id}
          title={`${episode.seasonNumber}x${episodeNumber} - ${title ?? ''}`}
          onModalClose={() => setIsInteractiveSearchOpen(false)}
        />

        {episodeFile ? (
          <>
            <IconButton
              name={icons.DELETE}
              title={translate('DeleteEpisodeFile')}
              aria-label={translate('DeleteEpisodeFile')}
              onPress={() => setIsDeleteFileOpen(true)}
            />

            <ConfirmModal
              isOpen={isDeleteFileOpen}
              kind={kinds.DANGER}
              title={translate('DeleteEpisodeFile')}
              message={translate('DeleteEpisodeFileMessage', {
                path: episodeFile.relativePath,
              })}
              confirmLabel={translate('Delete')}
              isSpinning={isDeleting}
              onConfirm={() =>
                deleteEpisodeFile(undefined, {
                  onSuccess: () => setIsDeleteFileOpen(false),
                })
              }
              onCancel={() => setIsDeleteFileOpen(false)}
            />
          </>
        ) : null}
      </TableRowCell>
    </TableRow>
  );
}

export default EpisodeRow;
