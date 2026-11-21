import React, { useCallback, useState } from 'react';
import { useTrackedCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import Tooltip from 'Components/Tooltip/Tooltip';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import { icons, kinds } from 'Helpers/Props';
import EditSourceModal from 'Sources/Edit/EditSourceModal';
import SourceEventsModal from 'Sources/Events/SourceEventsModal';
import Source from 'Sources/Source';
import { useDeleteSource, useInvalidateSources } from 'Sources/useSources';
import translate from 'Utilities/String/translate';
import styles from './SourceRow.module.css';

interface SourceRowProps {
  source: Source;
}

function LastResult({ source }: SourceRowProps) {
  if (!source.lastRefresh) {
    return <span className={styles.never}>{translate('Never')}</span>;
  }

  if (source.lastError) {
    return (
      <Tooltip
        anchor={
          <Label kind={kinds.DANGER} icon={icons.DANGER}>
            {translate('Error')}
          </Label>
        }
        tooltip={source.lastError}
        accessibleLabel={source.lastError}
        isAnchorFocusable={true}
      />
    );
  }

  return <Label kind={kinds.SUCCESS}>{translate('Ok')}</Label>;
}

function SourceRow({ source }: SourceRowProps) {
  const { id, name, typeName, type, eventCount, enabled } = source;

  const [isEditOpen, setIsEditOpen] = useState(false);
  const [isEventsOpen, setIsEventsOpen] = useState(false);
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);

  const invalidateSources = useInvalidateSources();
  const { execute: refresh, isExecuting: isRefreshing } = useTrackedCommand(
    `/source/${id}/refresh`,
    invalidateSources
  );

  const { deleteSource, isDeleting, deleteError, resetDeleteError } =
    useDeleteSource(id);

  const handleRefreshPress = useCallback(() => refresh(), [refresh]);

  const handleDeletePress = useCallback(() => {
    setIsEditOpen(false);
    setIsDeleteOpen(true);
  }, []);

  const handleDeleteCancel = useCallback(() => {
    resetDeleteError();
    setIsDeleteOpen(false);
  }, [resetDeleteError]);

  const handleDeleteConfirm = useCallback(() => {
    deleteSource(undefined, { onSuccess: () => setIsDeleteOpen(false) });
  }, [deleteSource]);

  const deleteErrors = getValidationFailures(deleteError).errors;

  return (
    <TableRow>
      <TableRowCell>
        <Link className={styles.name} onPress={() => setIsEventsOpen(true)}>
          {name}
        </Link>
      </TableRowCell>

      <TableRowCell>{typeName ?? type}</TableRowCell>

      <TableRowCell>{eventCount}</TableRowCell>

      <RelativeDateCell date={source.lastRefresh} includeTime={true} />

      <TableRowCell>
        <LastResult source={source} />
      </TableRowCell>

      <RelativeDateCell date={source.nextRefresh} includeTime={true} />

      <TableRowCell>
        <Icon
          name={enabled ? icons.CHECK : icons.CHECK_INDETERMINATE}
          title={enabled ? translate('Enabled') : translate('Disabled')}
        />
      </TableRowCell>

      <TableRowCell className={styles.actions}>
        <SpinnerIconButton
          name={icons.REFRESH}
          title={translate('RefreshSource')}
          isSpinning={isRefreshing}
          onPress={handleRefreshPress}
        />

        <IconButton
          name={icons.VIEW}
          title={translate('ViewEvents')}
          onPress={() => setIsEventsOpen(true)}
        />

        <IconButton
          name={icons.EDIT}
          title={translate('EditSource')}
          onPress={() => setIsEditOpen(true)}
        />

        <IconButton
          name={icons.DELETE}
          title={translate('DeleteSource')}
          onPress={handleDeletePress}
        />

        <EditSourceModal
          isOpen={isEditOpen}
          id={id}
          onModalClose={() => setIsEditOpen(false)}
          onDeletePress={handleDeletePress}
        />

        <SourceEventsModal
          isOpen={isEventsOpen}
          source={source}
          onModalClose={() => setIsEventsOpen(false)}
        />

        <ConfirmModal
          isOpen={isDeleteOpen}
          kind={kinds.DANGER}
          title={translate('DeleteSource')}
          message={
            <>
              {translate('DeleteSourceMessageText', { name })}
              {deleteErrors.map((error, index) => (
                <Alert key={index} kind={kinds.DANGER}>
                  {error.errorMessage}
                </Alert>
              ))}
              {deleteError && !deleteErrors.length ? (
                <Alert kind={kinds.DANGER}>{deleteError.message}</Alert>
              ) : null}
            </>
          }
          confirmLabel={translate('Delete')}
          isSpinning={isDeleting}
          onConfirm={handleDeleteConfirm}
          onCancel={handleDeleteCancel}
        />
      </TableRowCell>
    </TableRow>
  );
}

export default SourceRow;
