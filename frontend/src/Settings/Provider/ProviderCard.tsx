// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Indexers/Indexers/Indexer.tsx
// Shared by the indexer, download client and profile cards: opens the edit modal and confirms the
// delete, showing the API's 400 reason (e.g. a profile in use) in the confirmation.
import React, { ReactNode, useCallback, useState } from 'react';
import ModelBase from 'App/ModelBase';
import Alert from 'Components/Alert';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import SettingsCard from 'Components/SettingsCard/SettingsCard';
import settingsCardStyles from 'Components/SettingsCard/SettingsCard.module.css';
import TagList from 'Components/TagList';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import { kinds } from 'Helpers/Props';
import { useTagList } from 'Tags/useTags';
import translate from 'Utilities/String/translate';
import { useDeleteProvider } from './useProviders';

interface ProviderCardProps {
  path: string;
  id: number;
  name: string;
  editLabel: string;
  deleteTitle: string;
  deleteMessage: string;
  labels?: ReactNode;
  tags?: number[];
  children?: ReactNode;
  renderModal: (props: {
    isOpen: boolean;
    onModalClose: () => void;
    onDeletePress: () => void;
  }) => ReactNode;
}

function ProviderCard({
  path,
  id,
  name,
  editLabel,
  deleteTitle,
  deleteMessage,
  labels,
  tags = [],
  children,
  renderModal,
}: ProviderCardProps) {
  const tagList = useTagList();
  const [isEditOpen, setIsEditOpen] = useState(false);
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);
  const { deleteProvider, isDeleting, deleteError, resetDeleteError } =
    useDeleteProvider<ModelBase>(path, id);

  const handleDeletePress = useCallback(() => {
    setIsEditOpen(false);
    setIsDeleteOpen(true);
  }, []);

  const handleDeleteCancel = useCallback(() => {
    resetDeleteError();
    setIsDeleteOpen(false);
  }, [resetDeleteError]);

  const handleDeleteConfirm = useCallback(() => {
    deleteProvider(undefined, { onSuccess: () => setIsDeleteOpen(false) });
  }, [deleteProvider]);

  const deleteErrors = getValidationFailures(deleteError).errors;

  return (
    <SettingsCard
      name={name}
      aria-label={editLabel}
      onPress={() => setIsEditOpen(true)}
    >
      {children}

      {labels ? <div className={settingsCardStyles.labels}>{labels}</div> : null}

      {tags.length ? <TagList tags={tags} tagList={tagList} /> : null}

      {renderModal({
        isOpen: isEditOpen,
        onModalClose: () => setIsEditOpen(false),
        onDeletePress: handleDeletePress,
      })}

      <ConfirmModal
        isOpen={isDeleteOpen}
        kind={kinds.DANGER}
        title={deleteTitle}
        message={
          <>
            {deleteMessage}
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
    </SettingsCard>
  );
}

export default ProviderCard;
