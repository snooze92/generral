// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Activity/Queue/getQueueStatus.ts, QueueStatus.tsx and QueueStatusMessages.tsx
import React from 'react';
import Icon, { IconKind } from 'Components/Icon';
import Popover from 'Components/Tooltip/Popover';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { Queue } from './useQueue';

export function getQueueStatus({
  title: sourceTitle,
  status,
  trackedDownloadStatus,
  trackedDownloadState,
  errorMessage,
}: Queue) {
  const hasWarning = trackedDownloadStatus === 'warning';
  const hasError = trackedDownloadStatus === 'error';

  let iconName = icons.DOWNLOADING;
  let iconKind: IconKind = kinds.DEFAULT;
  let title = translate('Downloading');

  if (status === 'paused') {
    iconName = icons.PAUSED;
    title = translate('Paused');
  }

  if (status === 'queued') {
    iconName = icons.QUEUED;
    title = translate('Queued');
  }

  if (status === 'downloadingMetadata') {
    iconName = icons.QUEUED;
    title = translate('DownloadingMetadata');
  }

  if (status === 'completed') {
    iconName = icons.DOWNLOADED;
    title = translate('Downloaded');

    if (trackedDownloadState === 'importBlocked') {
      title += ` - ${translate('UnableToImportAutomatically')}`;
      iconKind = kinds.WARNING;
    }

    if (trackedDownloadState === 'importPending') {
      title += ` - ${translate('WaitingToImport')}`;
      iconKind = kinds.PURPLE;
    }

    if (trackedDownloadState === 'importing') {
      title += ` - ${translate('Importing')}`;
      iconKind = kinds.PRIMARY;
    }

    if (trackedDownloadState === 'failedPending') {
      title += ` - ${translate('WaitingToProcess')}`;
      iconKind = kinds.DANGER;
    }
  }

  if (hasWarning) {
    iconKind = kinds.WARNING;
  }

  if (status === 'delay') {
    iconName = icons.PENDING;
    title = translate('Pending');
  }

  if (status === 'downloadClientUnavailable') {
    iconName = icons.PENDING;
    iconKind = kinds.WARNING;
    title = translate('PendingDownloadClientUnavailable');
  }

  if (status === 'failed') {
    iconKind = kinds.DANGER;
    title = translate('DownloadFailed');
  }

  if (status === 'warning') {
    iconKind = kinds.WARNING;
    title = translate('DownloadWarning', {
      warningMessage: errorMessage || translate('CheckDownloadClientForDetails'),
    });
  }

  if (hasError) {
    iconKind = kinds.DANGER;

    if (status === 'completed') {
      iconName = icons.DOWNLOAD;
      title = translate('ImportFailed', { sourceTitle });
    } else {
      iconName = icons.DOWNLOADING;
      title = translate('DownloadFailed');
    }
  }

  return { iconName, iconKind, title, hasWarning, hasError };
}

function QueueStatus({ item }: { item: Queue }) {
  const { iconName, iconKind, title, hasWarning, hasError } =
    getQueueStatus(item);

  return (
    <Popover
      anchor={<Icon name={iconName} kind={iconKind} />}
      accessibleLabel={title}
      title={title}
      body={
        hasWarning || hasError ? (
          <div>
            {item.statusMessages.map(({ title: messageTitle, messages }) => (
              <div key={messageTitle}>
                {messageTitle}
                <ul>
                  {messages.map((message) => (
                    <li key={message}>{message}</li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        ) : (
          item.title
        )
      }
      position={tooltipPositions.RIGHT}
    />
  );
}

export default QueueStatus;
