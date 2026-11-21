// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Indexers/Indexers/EditIndexerModal.tsx
import React, { useState } from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import AddSourceModalContent from './AddSourceModalContent';
import EditSourceModalContent, {
  EditSourceModalContentProps,
} from './EditSourceModalContent';

interface EditSourceModalProps extends Omit<
  EditSourceModalContentProps,
  'type'
> {
  isOpen: boolean;
}

/** Edits a source, or without an id, picks a type and then adds a source of that type. */
function EditSourceModal({
  isOpen,
  id,
  onModalClose,
  ...otherProps
}: EditSourceModalProps) {
  const [type, setType] = useState<string>();

  const handleModalClose = () => {
    setType(undefined);
    onModalClose();
  };

  return (
    <Modal
      size={id || type ? sizes.LARGE : sizes.MEDIUM}
      isOpen={isOpen}
      onModalClose={handleModalClose}
    >
      {id || type ? (
        <EditSourceModalContent
          {...otherProps}
          id={id}
          type={type}
          onModalClose={handleModalClose}
        />
      ) : (
        <AddSourceModalContent
          onTypeSelect={setType}
          onModalClose={handleModalClose}
        />
      )}
    </Modal>
  );
}

export default EditSourceModal;
