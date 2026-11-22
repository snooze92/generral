// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Activity/Queue/RemoveQueueItemModal.tsx
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import CheckInput from 'Components/Form/CheckInput';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { Queue, useRemoveQueueItem } from './useQueue';
import styles from '../Activity.module.css';

interface RemoveQueueItemModalProps {
  isOpen: boolean;
  item: Queue;
  onModalClose: () => void;
}

/** Sonarr's removal/blocklist methods, simplified to the two flags the API takes. */
function RemoveQueueItemModal({
  isOpen,
  item,
  onModalClose,
}: RemoveQueueItemModalProps) {
  const { id, title: sourceTitle } = item;
  const [removeFromClient, setRemoveFromClient] = useState(true);
  const [blocklist, setBlocklist] = useState(false);
  const { removeQueueItem, isRemoving, removeError, resetRemoveError } =
    useRemoveQueueItem(id);

  const handleCancel = useCallback(() => {
    resetRemoveError();
    onModalClose();
  }, [resetRemoveError, onModalClose]);

  const handleConfirm = useCallback(
    () =>
      removeQueueItem(
        { removeFromClient, blocklist },
        { onSuccess: onModalClose }
      ),
    [removeQueueItem, removeFromClient, blocklist, onModalClose]
  );

  return (
    <ConfirmModal
      isOpen={isOpen}
      kind={kinds.DANGER}
      title={translate('RemoveQueueItem', { sourceTitle })}
      message={
        <>
          <p>{translate('RemoveQueueItemConfirmation', { sourceTitle })}</p>

          <label className={styles.option}>
            <CheckInput
              name="removeFromClient"
              value={removeFromClient}
              helpText={translate('RemoveFromDownloadClientHint')}
              onChange={({ value }) => setRemoveFromClient(value)}
            />
            {translate('RemoveFromDownloadClient')}
          </label>

          <label className={styles.option}>
            <CheckInput
              name="blocklist"
              value={blocklist}
              helpText={translate('BlocklistReleaseHelpText')}
              onChange={({ value }) => setBlocklist(value)}
            />
            {translate('BlocklistRelease')}
          </label>

          {removeError ? (
            <Alert kind={kinds.DANGER}>
              {removeError.statusBody?.message ?? removeError.message}
            </Alert>
          ) : null}
        </>
      }
      confirmLabel={translate('Remove')}
      isSpinning={isRemoving}
      onConfirm={handleConfirm}
      onCancel={handleCancel}
    />
  );
}

export default RemoveQueueItemModal;
