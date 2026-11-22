// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Activity/History/HistoryEventTypeCell.tsx
import React from 'react';
import Icon from 'Components/Icon';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { History } from './useHistory';
import styles from '../Activity.module.css';

function getIconName({ eventType, data }: History) {
  switch (eventType) {
    case 'grabbed':
    case 'downloadFailed':
      return icons.DOWNLOADING;
    case 'seriesFolderImported':
      return icons.DRIVE;
    case 'downloadFolderImported':
      return icons.DOWNLOADED;
    case 'episodeFileDeleted':
      return data.reason === 'MissingFromDisk'
        ? icons.FILE_MISSING
        : icons.DELETE;
    case 'episodeFileRenamed':
      return icons.ORGANIZE;
    case 'downloadIgnored':
      return icons.IGNORE;
    default:
      return icons.UNKNOWN;
  }
}

export function getHistoryTooltip({ eventType, data }: History) {
  switch (eventType) {
    case 'grabbed':
      return translate('EpisodeGrabbedTooltip', {
        indexer: data.indexer ?? '',
        downloadClient: data.downloadClientName ?? data.downloadClient ?? '',
      });
    case 'seriesFolderImported':
      return translate('SeriesFolderImportedTooltip');
    case 'downloadFolderImported':
      return translate('EpisodeImportedTooltip');
    case 'downloadFailed':
      return translate('DownloadFailedEpisodeTooltip');
    case 'episodeFileDeleted':
      return data.reason === 'MissingFromDisk'
        ? translate('EpisodeFileMissingTooltip')
        : translate('EpisodeFileDeletedTooltip');
    case 'episodeFileRenamed':
      return translate('EpisodeFileRenamedTooltip');
    case 'downloadIgnored':
      return translate('DownloadIgnoredEpisodeTooltip');
    default:
      return translate('UnknownEventTooltip');
  }
}

function HistoryEventTypeCell({ item }: { item: History }) {
  const tooltip = getHistoryTooltip(item);

  return (
    <TableRowCell className={styles.narrow} title={tooltip}>
      <Icon
        name={getIconName(item)}
        kind={item.eventType === 'downloadFailed' ? kinds.DANGER : kinds.DEFAULT}
        size={16}
        title={tooltip}
      />
    </TableRowCell>
  );
}

export default HistoryEventTypeCell;
