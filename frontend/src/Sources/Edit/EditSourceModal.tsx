import React from 'react';
import ProviderModal from 'Settings/Provider/ProviderModal';
import Source from 'Sources/Source';
import { PATH } from 'Sources/useSources';
import translate from 'Utilities/String/translate';
import EditSourceModalContent, {
  EditSourceModalContentProps,
} from './EditSourceModalContent';

interface EditSourceModalProps extends Omit<
  EditSourceModalContentProps,
  'type'
> {
  isOpen: boolean;
}

const getTypes = (schema: Source[]) =>
  schema.map((s) => ({ key: s.type, name: s.typeName ?? s.type, hint: s.type }));

/** Edits a source, or without an id, picks a type and then adds a source of that type. */
function EditSourceModal({
  isOpen,
  id,
  onModalClose,
  ...otherProps
}: EditSourceModalProps) {
  return (
    <ProviderModal<Source>
      isOpen={isOpen}
      id={id}
      path={PATH}
      title={translate('AddSource')}
      errorMessage={translate('AddSourceError')}
      getTypes={getTypes}
      renderEdit={(type, onClose) => (
        <EditSourceModalContent
          {...otherProps}
          id={id}
          type={type}
          onModalClose={onClose}
        />
      )}
      onModalClose={onModalClose}
    />
  );
}

export default EditSourceModal;
