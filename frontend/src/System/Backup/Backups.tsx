// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/System/Backup/Backups.tsx, BackupRow.tsx and RestoreBackupModalContent.tsx
import { useMutation } from '@tanstack/react-query';
import React, { useRef, useState } from 'react';
import { useTrackedCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import ToolbarItem from 'Components/Page/Toolbar/ToolbarItem';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds } from 'Helpers/Props';
import { useRestart } from 'System/useSystem';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import {
  Backup,
  useBackups,
  useDeleteBackup,
  useInvalidate,
  useRestoreBackup,
  withApiKey,
} from '../useSystemData';
import styles from './Backups.module.css';

const columns: Column[] = [
  { name: 'type', label: '', isVisible: true },
  { name: 'name', label: () => translate('Name'), isVisible: true },
  { name: 'size', label: () => translate('Size'), isVisible: true },
  { name: 'time', label: () => translate('Time'), isVisible: true },
  { name: 'actions', label: '', isVisible: true },
];

/** After a restore the database is swapped in at the next start, so offer to restart. */
function RestartModal({
  isOpen,
  onClose,
}: {
  isOpen: boolean;
  onClose: () => void;
}) {
  const { mutate: restart } = useRestart();

  return (
    <ConfirmModal
      isOpen={isOpen}
      title={translate('Restore')}
      message={translate('RestoreBackupRestartRequired')}
      confirmLabel={translate('RestartNow')}
      onConfirm={() => {
        onClose();
        restart();
      }}
      onCancel={onClose}
    />
  );
}

function BackupRow({
  backup,
  onRestored,
}: {
  backup: Backup;
  onRestored: () => void;
}) {
  const { id, name, size, time, type } = backup;
  const [confirm, setConfirm] = useState<'delete' | 'restore' | null>(null);
  const { mutate: deleteBackup, isPending: isDeleting } = useDeleteBackup(id);
  const { mutate: restoreBackup, isPending: isRestoring } =
    useRestoreBackup(id);
  const deleteLabel = translate('DeleteBackup');
  const restoreLabel = translate('RestoreBackup');

  return (
    <TableRow>
      <TableRowCell className={styles.type}>
        <Icon
          name={type === 'manual' ? icons.INTERACTIVE : icons.SCHEDULED}
          title={
            type === 'manual' ? translate('Manual') : translate('Scheduled')
          }
        />
      </TableRowCell>
      <TableRowCell>
        <Link
          to={withApiKey(
            `${window.Generral.urlBase}/api/v1/system/backup/${id}/download`
          )}
          noRouter={true}
        >
          {name}
        </Link>
      </TableRowCell>
      <TableRowCell>{formatBytes(size)}</TableRowCell>
      <RelativeDateCell date={time} />
      <TableRowCell className={styles.actions}>
        <IconButton
          title={restoreLabel}
          aria-label={restoreLabel}
          name={icons.RESTORE}
          onPress={() => setConfirm('restore')}
        />
        <IconButton
          title={deleteLabel}
          aria-label={deleteLabel}
          name={icons.DELETE}
          onPress={() => setConfirm('delete')}
        />
      </TableRowCell>

      <ConfirmModal
        isOpen={confirm === 'delete'}
        kind={kinds.DANGER}
        title={deleteLabel}
        message={translate('DeleteBackupMessageText', { name })}
        confirmLabel={translate('Delete')}
        isSpinning={isDeleting}
        onConfirm={() =>
          deleteBackup(undefined, { onSuccess: () => setConfirm(null) })
        }
        onCancel={() => setConfirm(null)}
      />

      <ConfirmModal
        isOpen={confirm === 'restore'}
        kind={kinds.WARNING}
        title={restoreLabel}
        message={translate('RestoreBackupMessageText', { name })}
        confirmLabel={translate('Restore')}
        isSpinning={isRestoring}
        onConfirm={() =>
          restoreBackup(undefined, {
            onSuccess: () => {
              setConfirm(null);
              onRestored();
            },
          })
        }
        onCancel={() => setConfirm(null)}
      />
    </TableRow>
  );
}

function Backups() {
  const { data, isLoading, isFetched, error } = useBackups();
  const invalidate = useInvalidate('/system/backup');
  const { execute: backup, isExecuting: isBackingUp } = useTrackedCommand(
    '/command',
    invalidate
  );
  const [isRestarting, setIsRestarting] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);

  const upload = useMutation<unknown, Error, File>({
    mutationFn: async (file) => {
      const body = new FormData();
      body.append('file', file);
      const response = await fetch(
        getQueryPath('/system/backup/restore/upload'),
        {
          method: 'POST',
          headers: {
            'X-Api-Key': window.Generral.apiKey,
            'X-Generral-Client': 'Generral',
          },
          body,
        }
      );

      if (!response.ok) {
        throw new Error(translate('RestoreBackupFailed'));
      }

      return response.json();
    },
    onSuccess: () => setIsRestarting(true),
  });

  return (
    <PageContent title={translate('Backups')}>
      <PageToolbar>
        <ToolbarItem
          id="backup"
          priority={1}
          groupId="left"
          label={translate('BackupNow')}
          iconName={icons.BACKUP}
          isSpinning={isBackingUp}
          onPress={() => backup({ name: 'Backup' })}
        />
        <ToolbarItem
          id="restore"
          priority={1}
          groupId="left"
          label={translate('RestoreBackup')}
          iconName={icons.RESTORE}
          isSpinning={upload.isPending}
          onPress={() => fileInput.current?.click()}
        />
      </PageToolbar>

      <PageContentBody>
        <PageHeading scope={translate('System')} title={translate('Backups')} />

        <input
          ref={fileInput}
          className={styles.fileInput}
          type="file"
          accept=".zip"
          aria-label={translate('RestoreBackup')}
          onChange={(event) => {
            const file = event.target.files?.[0];

            if (file) {
              upload.mutate(file);
            }

            event.target.value = '';
          }}
        />

        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('BackupsLoadError')}</Alert>
        ) : null}

        {upload.error ? (
          <Alert kind={kinds.DANGER}>{upload.error.message}</Alert>
        ) : null}

        {isFetched && !data.length ? (
          <Alert kind={kinds.INFO}>{translate('NoBackupsAreAvailable')}</Alert>
        ) : null}

        {data.length ? (
          <Table columns={columns}>
            <TableBody>
              {data.map((item) => (
                <BackupRow
                  key={item.id}
                  backup={item}
                  onRestored={() => setIsRestarting(true)}
                />
              ))}
            </TableBody>
          </Table>
        ) : null}

        <RestartModal
          isOpen={isRestarting}
          onClose={() => setIsRestarting(false)}
        />
      </PageContentBody>
    </PageContent>
  );
}

export default Backups;
