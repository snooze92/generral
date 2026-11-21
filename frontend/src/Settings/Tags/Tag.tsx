// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Tags/Tag.tsx
// Generral tags are used by sources and series; a tag in use can be deleted.
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import SettingsCard from 'Components/SettingsCard/SettingsCard';
import settingsCardStyles from 'Components/SettingsCard/SettingsCard.module.css';
import { kinds } from 'Helpers/Props';
import { useInvalidateSources } from 'Sources/useSources';
import { useInvalidateTagDetails, useTagDetail } from 'Tags/useTagDetails';
import { useDeleteTag } from 'Tags/useTags';
import translate from 'Utilities/String/translate';
import TagDetailsModal from './Details/TagDetailsModal';
import TagInUse from './TagInUse';

interface TagProps {
  id: number;
  label: string;
}

function Tag({ id, label }: TagProps) {
  const { deleteTag, isDeletingTag, deleteTagError } = useDeleteTag(id);
  const { sourceIds, seriesIds } = useTagDetail(id);
  const invalidateTagDetails = useInvalidateTagDetails();
  const invalidateSources = useInvalidateSources();
  const [isDetailsModalOpen, setIsDetailsModalOpen] = useState(false);
  const [isDeleteTagModalOpen, setIsDeleteTagModalOpen] = useState(false);

  const isTagUsed = !!(sourceIds.length || seriesIds.length);

  const handleShowDetailsPress = useCallback(() => {
    setIsDetailsModalOpen(true);
  }, []);

  const handleDetailsModalClose = useCallback(() => {
    setIsDetailsModalOpen(false);
  }, []);

  const handleDeleteTagPress = useCallback(() => {
    setIsDetailsModalOpen(false);
    setIsDeleteTagModalOpen(true);
  }, []);

  const handleConfirmDeleteTag = useCallback(() => {
    deleteTag(undefined, {
      onSuccess: () => {
        setIsDeleteTagModalOpen(false);
        invalidateTagDetails();

        if (sourceIds.length) {
          invalidateSources();
        }
      },
    });
  }, [deleteTag, invalidateTagDetails, invalidateSources, sourceIds]);

  const handleDeleteTagModalClose = useCallback(() => {
    setIsDeleteTagModalOpen(false);
  }, []);

  return (
    <SettingsCard
      name={label}
      aria-label={translate('TagDetails', { label })}
      onPress={handleShowDetailsPress}
    >
      {isTagUsed ? (
        <div className={settingsCardStyles.labels}>
          <TagInUse
            label={translate('Source')}
            labelPlural={translate('Sources')}
            count={sourceIds.length}
          />

          <TagInUse label={translate('Series')} count={seriesIds.length} />
        </div>
      ) : (
        <div>{translate('NoLinks')}</div>
      )}

      <TagDetailsModal
        id={id}
        label={label}
        sourceIds={sourceIds}
        seriesIds={seriesIds}
        isOpen={isDetailsModalOpen}
        onModalClose={handleDetailsModalClose}
        onDeleteTagPress={handleDeleteTagPress}
      />

      <ConfirmModal
        isOpen={isDeleteTagModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteTag')}
        message={
          <>
            {isTagUsed
              ? translate('DeleteTagInUseMessageText', {
                  label,
                  sources: sourceIds.length,
                  series: seriesIds.length,
                })
              : translate('DeleteTagMessageText', { label })}
            {deleteTagError ? (
              <Alert kind={kinds.DANGER}>{deleteTagError}</Alert>
            ) : null}
          </>
        }
        confirmLabel={translate('Delete')}
        isSpinning={isDeletingTag}
        onConfirm={handleConfirmDeleteTag}
        onCancel={handleDeleteTagModalClose}
      />
    </SettingsCard>
  );
}

export default Tag;
