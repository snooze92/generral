// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Indexers/Indexers/EditIndexerModal.tsx
import React, { ReactNode, useState } from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import AddProviderModalContent, { ProviderType } from './AddProviderModalContent';
import { ProviderBase } from './Provider';
import { useProviderSchema } from './useProviders';

interface SchemaPickerProps<T extends ProviderBase> {
  path: string;
  title: string;
  errorMessage: string;
  getTypes: (schema: T[]) => ProviderType[];
  onTypeSelect: (type: string) => void;
  onModalClose: () => void;
}

function SchemaPicker<T extends ProviderBase>({
  path,
  title,
  errorMessage,
  getTypes,
  onTypeSelect,
  onModalClose,
}: SchemaPickerProps<T>) {
  const { data, isLoading, error } = useProviderSchema<T>(path);

  return (
    <AddProviderModalContent
      title={title}
      types={getTypes(data)}
      isLoading={isLoading}
      errorMessage={error ? errorMessage : undefined}
      onTypeSelect={onTypeSelect}
      onModalClose={onModalClose}
    />
  );
}

interface ProviderModalProps<T extends ProviderBase> extends Omit<
  SchemaPickerProps<T>,
  'onTypeSelect' | 'onModalClose'
> {
  isOpen: boolean;
  id?: number;
  renderEdit: (type: string | undefined, onModalClose: () => void) => ReactNode;
  onModalClose: () => void;
}

/** Edits a provider, or without an id, picks a type from the schema and then adds one of that type. */
function ProviderModal<T extends ProviderBase>({
  isOpen,
  id,
  renderEdit,
  onModalClose,
  ...pickerProps
}: ProviderModalProps<T>) {
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
        renderEdit(type, handleModalClose)
      ) : (
        <SchemaPicker<T>
          {...pickerProps}
          onTypeSelect={setType}
          onModalClose={handleModalClose}
        />
      )}
    </Modal>
  );
}

export default ProviderModal;
