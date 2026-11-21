// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Indexers/Indexers/AddIndexerModalContent.tsx
import React from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import { useSourceSchema } from 'Sources/useSources';
import translate from 'Utilities/String/translate';
import styles from './AddSourceModalContent.module.css';

interface AddSourceModalContentProps {
  onTypeSelect: (type: string) => void;
  onModalClose: () => void;
}

function AddSourceModalContent({
  onTypeSelect,
  onModalClose,
}: AddSourceModalContentProps) {
  const { data: schema, isLoading, error } = useSourceSchema();

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AddSource')}</ModalHeader>

      <ModalBody>
        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('AddSourceError')}</Alert>
        ) : null}

        <div className={styles.types}>
          {schema.map((type) => (
            <Link
              key={type.type}
              className={styles.type}
              onPress={() => onTypeSelect(type.type)}
            >
              <span className={styles.name}>{type.typeName ?? type.type}</span>
              <span className={styles.id}>{type.type}</span>
            </Link>
          ))}
        </div>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddSourceModalContent;
