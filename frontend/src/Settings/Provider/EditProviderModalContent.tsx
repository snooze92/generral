// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Indexers/Indexers/EditIndexerModalContent.tsx
// The shared shell of the source, indexer and download client edit modals: the type-specific rows
// come as children, then the schema fields, then a footer with Delete, Test and Save.
import React, { ReactNode, useCallback } from 'react';
import Form from 'Components/Form/Form';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import { InputChanged } from 'typings/inputs';
import {
  PendingField,
  ValidationError,
  ValidationWarning,
} from 'typings/pending';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import translate from 'Utilities/String/translate';
import { ProviderField } from './Provider';
import ProviderFieldFormGroup from './ProviderFieldFormGroup';
import styles from './EditProviderModalContent.module.css';

interface EditProviderModalContentProps {
  title: string;
  subtitle?: string;
  providerName: string;
  fields?: PendingField<unknown>[];
  validationErrors: ValidationError[];
  validationWarnings: ValidationWarning[];
  isTesting: boolean;
  testFailure: ApiError | string | null;
  isSaving: boolean;
  saveError: ApiError | null;
  children: ReactNode;
  after?: ReactNode;
  onFieldChange: (changes: Record<string, unknown>) => void;
  onTestPress: () => void;
  onSavePress: () => void;
  onDeletePress?: () => void;
  onModalClose: () => void;
}

function EditProviderModalContent({
  title,
  subtitle,
  providerName,
  fields,
  validationErrors,
  validationWarnings,
  isTesting,
  testFailure,
  isSaving,
  saveError,
  children,
  after,
  onFieldChange,
  onTestPress,
  onSavePress,
  onDeletePress,
  onModalClose,
}: EditProviderModalContentProps) {
  const showAdvancedSettings = useShowAdvancedSettings();

  const handleFieldChange = useCallback(
    ({ name, value }: InputChanged<unknown>) => {
      onFieldChange({ [name]: value });
    },
    [onFieldChange]
  );

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader subtitle={subtitle}>{title}</ModalHeader>

      <ModalBody>
        <Form
          validationErrors={validationErrors}
          validationWarnings={validationWarnings}
        >
          {children}

          {fields?.map((field) => (
            <ProviderFieldFormGroup
              key={field.name}
              advancedSettings={showAdvancedSettings}
              {...(field as unknown as ProviderField & PendingField<unknown>)}
              onChange={handleFieldChange}
            />
          ))}
        </Form>

        {after}
      </ModalBody>

      <ModalFooter>
        {onDeletePress ? (
          <Button
            className={styles.deleteButton}
            kind={kinds.DANGER}
            onPress={onDeletePress}
          >
            {translate('Delete')}
          </Button>
        ) : null}

        <AdvancedSettingsButton showLabel={false} />

        <SpinnerErrorButton
          isSpinning={isTesting}
          error={testFailure}
          pendingLabel={translate('ProviderTestInProgress', {
            name: providerName,
          })}
          successLabel={translate('ProviderTestSuccessful', {
            name: providerName,
          })}
          warningLabel={translate('ProviderTestWarning', {
            name: providerName,
          })}
          errorLabel={translate('ProviderTestFailed', { name: providerName })}
          onPress={onTestPress}
        >
          {translate('Test')}
        </SpinnerErrorButton>

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          onPress={onSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditProviderModalContent;
