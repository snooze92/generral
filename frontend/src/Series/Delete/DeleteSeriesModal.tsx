// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Series/Delete/DeleteSeriesModalContent.tsx
import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { icons, kinds } from 'Helpers/Props';
import Series from 'Series/Series';
import { useDeleteSeries } from 'Series/useSeries';
import translate from 'Utilities/String/translate';
import styles from './DeleteSeriesModal.module.css';

interface DeleteSeriesModalProps {
  isOpen: boolean;
  series: Series;
  onModalClose: () => void;
}

/** Files aren't managed yet (M2), so unlike Sonarr there is no *Delete Series Folder* option. */
function DeleteSeriesModal({
  isOpen,
  series,
  onModalClose,
}: DeleteSeriesModalProps) {
  const { id, title, path } = series;
  const { deleteSeries, isDeleting, deleteError, resetDeleteError } =
    useDeleteSeries(id);

  const handleCancel = useCallback(() => {
    resetDeleteError();
    onModalClose();
  }, [resetDeleteError, onModalClose]);

  // The details page goes back to the index once the series is gone.
  const handleConfirm = useCallback(() => deleteSeries(), [deleteSeries]);

  return (
    <ConfirmModal
      isOpen={isOpen}
      kind={kinds.DANGER}
      title={translate('DeleteSeriesModalHeader', { title })}
      message={
        <>
          <div className={styles.pathContainer}>
            <Icon className={styles.pathIcon} name={icons.FOLDER} />
            {path}
          </div>

          <div>{translate('DeleteSeriesMessageText', { title })}</div>

          {deleteError ? (
            <Alert kind={kinds.DANGER}>{deleteError.message}</Alert>
          ) : null}
        </>
      }
      confirmLabel={translate('Delete')}
      isSpinning={isDeleting}
      onConfirm={handleConfirm}
      onCancel={handleCancel}
    />
  );
}

export default DeleteSeriesModal;
