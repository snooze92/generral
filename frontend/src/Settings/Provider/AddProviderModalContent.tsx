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
import translate from 'Utilities/String/translate';
import styles from './AddProviderModalContent.module.css';

export interface ProviderType {
  key: string;
  name: string;
  hint?: string;
}

interface AddProviderModalContentProps {
  title: string;
  types: ProviderType[];
  isLoading: boolean;
  errorMessage?: string;
  onTypeSelect: (type: string) => void;
  onModalClose: () => void;
}

/** Lists the implementations from a provider schema to pick the one to add. */
function AddProviderModalContent({
  title,
  types,
  isLoading,
  errorMessage,
  onTypeSelect,
  onModalClose,
}: AddProviderModalContentProps) {
  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{title}</ModalHeader>

      <ModalBody>
        {isLoading ? <LoadingIndicator /> : null}

        {errorMessage ? (
          <Alert kind={kinds.DANGER}>{errorMessage}</Alert>
        ) : null}

        <div className={styles.types}>
          {types.map((type) => (
            <Link
              key={type.key}
              className={styles.type}
              onPress={() => onTypeSelect(type.key)}
            >
              <span className={styles.name}>{type.name}</span>
              {type.hint ? (
                <span className={styles.id}>{type.hint}</span>
              ) : null}
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

export default AddProviderModalContent;
