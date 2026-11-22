// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Notifications/Notifications/
// EditNotificationModalContent.tsx and NotificationEventItems.tsx
import React, { useCallback } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { inputTypes } from 'Helpers/Props';
import EditProviderModalContent from 'Settings/Provider/EditProviderModalContent';
import ProviderModal from 'Settings/Provider/ProviderModal';
import { useManageProvider } from 'Settings/Provider/useProviders';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import Notification, { PATH, supportsKey, Trigger, TRIGGERS } from './Notification';
import styles from './Notifications.module.css';

const triggerLabels: Record<Trigger, () => string> = {
  onGrab: () => translate('OnGrab'),
  onDownload: () => translate('OnFileImport'),
  onUpgrade: () => translate('OnFileUpgrade'),
  onImportComplete: () => translate('OnImportComplete'),
  onRename: () => translate('OnRename'),
  onSeriesAdd: () => translate('OnSeriesAdd'),
  onSeriesDelete: () => translate('OnSeriesDelete'),
  onEpisodeFileDelete: () => translate('OnEpisodeFileDelete'),
  onEpisodeFileDeleteForUpgrade: () =>
    translate('OnEpisodeFileDeleteForUpgrade'),
  onHealthIssue: () => translate('OnHealthIssue'),
  onHealthRestored: () => translate('OnHealthRestored'),
  onApplicationUpdate: () => translate('OnApplicationUpdate'),
  onManualInteractionRequired: () => translate('OnManualInteractionRequired'),
};

interface TriggersProps {
  item: PendingSection<Notification>;
  onInputChange: (change: InputChanged) => void;
}

function NotificationTriggers({ item, onInputChange }: TriggersProps) {
  const supported = TRIGGERS.filter((t) => item[supportsKey(t)]?.value);

  return (
    <FormRow>
      <FormLabel>{translate('NotificationTriggers')}</FormLabel>
      <div>
        <FormInputHelpText text={translate('NotificationTriggersHelpText')} />
        <div className={styles.events}>
          {supported.map((trigger) => (
            <div key={trigger}>
              <CheckInput
                name={trigger}
                helpText={triggerLabels[trigger]()}
                {...item[trigger]}
                onChange={onInputChange}
              />
            </div>
          ))}

          {item.supportsOnHealthIssue?.value && item.onHealthIssue?.value ? (
            <div>
              <CheckInput
                name="includeHealthWarnings"
                helpText={translate('IncludeHealthWarnings')}
                {...item.includeHealthWarnings}
                onChange={onInputChange}
              />
            </div>
          ) : null}
        </div>
      </div>
    </FormRow>
  );
}

interface EditNotificationModalContentProps {
  id?: number;
  implementation?: string;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditNotificationModalContent({
  id,
  implementation,
  onModalClose,
  onDeletePress,
}: EditNotificationModalContentProps) {
  const selectSchema = useCallback(
    (schema: Notification[]) =>
      schema.find((s) => s.implementation === implementation),
    [implementation]
  );

  const {
    provider,
    item,
    updateValue,
    updateFieldValue,
    saveProvider,
    isSaving,
    saveError,
    testProvider,
    isTesting,
    testError,
    validationErrors,
    validationWarnings,
  } = useManageProvider<Notification>({
    path: PATH,
    id,
    selectSchema,
    onSaved: onModalClose,
  });

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      updateValue(name as keyof Notification, value as never);
    },
    [updateValue]
  );

  if (!provider) {
    return null;
  }

  const { name, tags, fields } = item;
  const implementationName = provider.implementationName;

  return (
    <EditProviderModalContent
      title={
        id
          ? translate('EditConnectionImplementation', { implementationName })
          : translate('AddConnectionImplementation', { implementationName })
      }
      subtitle={id ? (name.previousValue ?? name.value) : undefined}
      providerName={name.value || implementationName}
      fields={fields}
      validationErrors={validationErrors}
      validationWarnings={validationWarnings}
      isTesting={isTesting}
      testFailure={testError}
      isSaving={isSaving}
      saveError={saveError}
      after={
        <FormRow>
          <FormLabel>{translate('Tags')}</FormLabel>
          <FormInputHelpText
            text={translate('NotificationsTagsSeriesHelpText')}
          />
          <FormInput
            type={inputTypes.TAG}
            name="tags"
            {...tags}
            onChange={handleInputChange}
          />
        </FormRow>
      }
      onFieldChange={updateFieldValue}
      onTestPress={testProvider}
      onSavePress={saveProvider}
      onDeletePress={id ? onDeletePress : undefined}
      onModalClose={onModalClose}
    >
      <FormRow>
        <FormLabel>{translate('Name')}</FormLabel>
        <FormInput
          type={inputTypes.TEXT}
          name="name"
          {...name}
          onChange={handleInputChange}
        />
      </FormRow>

      <NotificationTriggers item={item} onInputChange={handleInputChange} />
    </EditProviderModalContent>
  );
}

interface EditNotificationModalProps {
  isOpen: boolean;
  id?: number;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

const getTypes = (schema: Notification[]) =>
  schema.map((s) => ({ key: s.implementation, name: s.implementationName }));

/** Edits a connection, or without an id, picks an implementation and then adds one. */
function EditNotificationModal({
  isOpen,
  id,
  onModalClose,
  onDeletePress,
}: EditNotificationModalProps) {
  return (
    <ProviderModal<Notification>
      isOpen={isOpen}
      id={id}
      path={PATH}
      title={translate('AddConnection')}
      errorMessage={translate('AddNotificationError')}
      getTypes={getTypes}
      renderEdit={(implementation, onClose) => (
        <EditNotificationModalContent
          id={id}
          implementation={implementation}
          onModalClose={onClose}
          onDeletePress={onDeletePress}
        />
      )}
      onModalClose={onModalClose}
    />
  );
}

export default EditNotificationModal;
