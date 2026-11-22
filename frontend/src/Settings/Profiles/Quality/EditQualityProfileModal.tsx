// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Profiles/Quality/EditQualityProfileModalContent.tsx
// Without custom formats and group editing; qualities are reordered with up/down buttons.
import React, { useCallback, useMemo } from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import useLanguages from 'Language/useLanguages';
import useManageItem from 'Settings/Profiles/useManageItem';
import editProviderStyles from 'Settings/Provider/EditProviderModalContent.module.css';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import QualityProfileItems, { itemId, itemName } from './QualityProfileItems';
import useQualityProfiles, {
  PATH,
  QualityProfile,
  QualityProfileItem,
  useQualityProfileSchema,
} from './useQualityProfiles';

interface EditQualityProfileModalContentProps {
  profile: QualityProfile;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditQualityProfileModalContent({
  profile,
  onModalClose,
  onDeletePress,
}: EditQualityProfileModalContentProps) {
  const {
    current,
    settings,
    validationErrors,
    validationWarnings,
    updateValue,
    save,
    isSaving,
    saveError,
  } = useManageItem<QualityProfile>(PATH, profile, onModalClose);

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      updateValue(name as keyof QualityProfile, value as never);
    },
    [updateValue]
  );

  const items = useMemo(() => current?.items ?? [], [current]);

  const cutoffValues = useMemo(
    () =>
      items
        .filter((item) => item.allowed)
        .reverse()
        .map((item) => ({ key: itemId(item), value: itemName(item) })),
    [items]
  );

  const handleItemsChange = useCallback(
    (newItems: QualityProfileItem[]) => {
      updateValue('items', newItems);

      const allowed = newItems.filter((item) => item.allowed);

      if (allowed.length && !allowed.some((i) => itemId(i) === current?.cutoff)) {
        updateValue('cutoff', itemId(allowed[0]));
      }
    },
    [current?.cutoff, updateValue]
  );

  const { data: languages } = useLanguages();

  const languageValues = useMemo(
    () => languages.map(({ id, name }) => ({ key: id, value: name })),
    [languages]
  );

  const handleLanguageChange = useCallback(
    ({ value }: InputChanged<number>) => {
      const language = languages.find((l) => l.id === value);

      if (language) {
        updateValue('language', language);
      }
    },
    [languages, updateValue]
  );

  const { name, upgradeAllowed, cutoff, language } = settings;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {profile.id
          ? translate('EditQualityProfile')
          : translate('AddQualityProfile')}
      </ModalHeader>

      <ModalBody>
        <Form
          validationErrors={validationErrors}
          validationWarnings={validationWarnings}
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

          <FormRow>
            <FormLabel>
              {translate('UpgradesAllowed')}
            </FormLabel>
            <FormInput
              type={inputTypes.CHECK}
              name="upgradeAllowed"
              helpText={translate('UpgradesAllowedHelpText')}
              {...upgradeAllowed}
              onChange={handleInputChange}
            />
          </FormRow>

          {upgradeAllowed?.value ? (
            <FormRow>
              <FormLabel>
                {translate('UpgradeUntil')}
              </FormLabel>
              <FormInputHelpText
                text={translate('UpgradeUntilQualityHelpText')}
              />
              <FormInput
                type={inputTypes.SELECT}
                name="cutoff"
                values={cutoffValues}
                {...cutoff}
                onChange={handleInputChange}
              />
            </FormRow>
          ) : null}

          <FormRow>
            <FormLabel>{translate('Language')}</FormLabel>
            <FormInputHelpText
              text={translate('QualityProfileLanguageHelpText')}
            />
            <FormInput
              type={inputTypes.SELECT}
              name="language"
              values={languageValues}
              {...language}
              value={language?.value?.id}
              onChange={handleLanguageChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('Qualities')}</FormLabel>
            <FormInputHelpText text={translate('QualitiesHelpText')} />
          </FormRow>

          <QualityProfileItems items={items} onChange={handleItemsChange} />
        </Form>
      </ModalBody>

      <ModalFooter>
        {onDeletePress ? (
          <Button
            className={editProviderStyles.deleteButton}
            kind={kinds.DANGER}
            onPress={onDeletePress}
          >
            {translate('Delete')}
          </Button>
        ) : null}

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          onPress={save}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

function NewQualityProfileModalContent({
  onModalClose,
}: {
  onModalClose: () => void;
}) {
  const { data } = useQualityProfileSchema(true);

  if (!data) {
    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalBody>
          <LoadingIndicator />
        </ModalBody>
      </ModalContent>
    );
  }

  return (
    <EditQualityProfileModalContent
      profile={{ ...data, id: 0 }}
      onModalClose={onModalClose}
    />
  );
}

interface EditQualityProfileModalProps {
  isOpen: boolean;
  id?: number;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditQualityProfileModal({
  isOpen,
  id,
  onModalClose,
  onDeletePress,
}: EditQualityProfileModalProps) {
  const { data: profiles } = useQualityProfiles();
  const profile = id ? profiles.find((p) => p.id === id) : undefined;

  return (
    <Modal size={sizes.LARGE} isOpen={isOpen} onModalClose={onModalClose}>
      {profile ? (
        <EditQualityProfileModalContent
          profile={profile}
          onModalClose={onModalClose}
          onDeletePress={onDeletePress}
        />
      ) : (
        <NewQualityProfileModalContent onModalClose={onModalClose} />
      )}
    </Modal>
  );
}

export default EditQualityProfileModal;
