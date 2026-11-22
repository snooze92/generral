// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Profiles/Release/EditReleaseProfileModalContent.tsx
// Generral release profiles have a single indexer (0 = any) and no air date restrictions.
import React, { useCallback, useMemo } from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import ModalSection from 'Components/ModalSection';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import Indexer, { PATH as INDEXER_PATH } from 'Settings/Indexers/Indexer';
import useManageItem from 'Settings/Profiles/useManageItem';
import editProviderStyles from 'Settings/Provider/EditProviderModalContent.module.css';
import { useProviders } from 'Settings/Provider/useProviders';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import useReleaseProfiles, { PATH, ReleaseProfile } from './useReleaseProfiles';

const NEW_PROFILE: ReleaseProfile = {
  id: 0,
  name: '',
  enabled: true,
  required: [],
  ignored: [],
  indexerId: 0,
  tags: [],
  excludedTags: [],
};

interface EditReleaseProfileModalContentProps {
  profile: ReleaseProfile;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditReleaseProfileModalContent({
  profile,
  onModalClose,
  onDeletePress,
}: EditReleaseProfileModalContentProps) {
  const { data: indexers } = useProviders<Indexer>(INDEXER_PATH);
  const {
    settings,
    validationErrors,
    validationWarnings,
    updateValue,
    save,
    isSaving,
    saveError,
  } = useManageItem<ReleaseProfile>(PATH, profile, onModalClose);

  const indexerValues = useMemo(
    () => [
      { key: 0, value: translate('Any') },
      ...indexers.map((indexer) => ({ key: indexer.id, value: indexer.name })),
    ],
    [indexers]
  );

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      updateValue(name as keyof ReleaseProfile, value as never);
    },
    [updateValue]
  );

  const { name, enabled, required, ignored, indexerId, tags, excludedTags } =
    settings;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {profile.id
          ? translate('EditReleaseProfile')
          : translate('AddReleaseProfile')}
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
              placeholder={translate('OptionalName')}
              {...name}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('EnableProfile')}</FormLabel>
            <FormInputHelpText text={translate('EnableProfileHelpText')} />
            <FormInput
              type={inputTypes.CHECK}
              name="enabled"
              {...enabled}
              onChange={handleInputChange}
            />
          </FormRow>

          <ModalSection title={translate('ProfileSectionPatterns')}>
            <FormRow>
              <FormLabel>{translate('MustContain')}</FormLabel>
              <FormInputHelpText text={translate('MustContainHelpText')} />
              <FormInput
                type={inputTypes.TEXT_TAG}
                name="required"
                placeholder={translate('AddNewRestriction')}
                {...required}
                onChange={handleInputChange}
              />
            </FormRow>

            <FormRow>
              <FormLabel>{translate('MustNotContain')}</FormLabel>
              <FormInputHelpText text={translate('MustNotContainHelpText')} />
              <FormInput
                type={inputTypes.TEXT_TAG}
                name="ignored"
                placeholder={translate('AddNewRestriction')}
                {...ignored}
                onChange={handleInputChange}
              />
            </FormRow>
          </ModalSection>

          <ModalSection title={translate('ProfileSectionScope')}>
            <FormRow>
              <FormLabel>{translate('Indexer')}</FormLabel>
              <FormInputHelpText
                text={translate('ReleaseProfileIndexerHelpText')}
              />
              <FormInputHelpText
                text={translate('ReleaseProfileIndexerHelpTextWarning')}
                isWarning={true}
              />
              <FormInput
                type={inputTypes.SELECT}
                name="indexerId"
                values={indexerValues}
                {...indexerId}
                onChange={handleInputChange}
              />
            </FormRow>

            <FormRow>
              <FormLabel>{translate('Tags')}</FormLabel>
              <FormInputHelpText
                text={translate('ReleaseProfileTagSeriesHelpText')}
              />
              <FormInput
                type={inputTypes.TAG}
                name="tags"
                {...tags}
                onChange={handleInputChange}
              />
            </FormRow>

            <FormRow>
              <FormLabel>{translate('ExcludedTags')}</FormLabel>
              <FormInputHelpText
                text={translate('ReleaseProfileExcludedTagSeriesHelpText')}
              />
              <FormInput
                type={inputTypes.TAG}
                name="excludedTags"
                kind={kinds.DANGER}
                {...excludedTags}
                onChange={handleInputChange}
              />
            </FormRow>
          </ModalSection>
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

interface EditReleaseProfileModalProps {
  isOpen: boolean;
  id?: number;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditReleaseProfileModal({
  isOpen,
  id,
  onModalClose,
  onDeletePress,
}: EditReleaseProfileModalProps) {
  const { data: profiles } = useReleaseProfiles();
  const profile = id ? profiles.find((p) => p.id === id) : NEW_PROFILE;

  return (
    <Modal size={sizes.MEDIUM} isOpen={isOpen} onModalClose={onModalClose}>
      {profile ? (
        <EditReleaseProfileModalContent
          profile={profile}
          onModalClose={onModalClose}
          onDeletePress={id ? onDeletePress : undefined}
        />
      ) : null}
    </Modal>
  );
}

export default EditReleaseProfileModal;
