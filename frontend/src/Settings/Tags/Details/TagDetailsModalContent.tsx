// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/Tags/Details/TagDetailsModalContent.tsx
// Also adds and renames tags, and a tag in use can be deleted (it's removed from its sources and series).
import React, { useCallback, useMemo, useState } from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import ModalSection from 'Components/ModalSection';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import { inputTypes, kinds } from 'Helpers/Props';
import { useSources } from 'Sources/useSources';
import useSaveTag from 'Tags/useSaveTag';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import TagInUse from '../TagInUse';
import styles from './TagDetailsModalContent.module.css';

export interface TagDetailsModalContentProps {
  id?: number;
  label?: string;
  sourceIds?: number[];
  seriesIds?: number[];
  onModalClose: () => void;
  onDeleteTagPress?: () => void;
}

function TagDetailsModalContent({
  id,
  label = '',
  sourceIds = [],
  seriesIds = [],
  onModalClose,
  onDeleteTagPress,
}: TagDetailsModalContentProps) {
  const [value, setValue] = useState(label);
  const { data: allSources } = useSources();
  const { saveTag, isSaving, saveError } = useSaveTag(id, onModalClose);

  const sources = useMemo(
    () =>
      allSources
        .filter((source) => sourceIds.includes(source.id))
        .sort((a, b) => a.name.localeCompare(b.name)),
    [allSources, sourceIds]
  );

  const { errors } = getValidationFailures(saveError);
  const labelErrors = errors.filter(
    (error) => error.propertyName.toLowerCase() === 'label'
  );
  const otherErrors = errors.filter((error) => !labelErrors.includes(error));

  const handleInputChange = useCallback(
    ({ value }: InputChanged<string>) => setValue(value),
    []
  );

  const isTagUsed = !!(sourceIds.length || seriesIds.length);
  const trimmed = value.trim();

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {id ? translate('TagDetails', { label }) : translate('AddTag')}
      </ModalHeader>

      <ModalBody>
        <Form validationErrors={otherErrors}>
          <FormRow>
            <FormLabel>{translate('Label')}</FormLabel>
            <FormInputHelpText text={translate('TagLabelHelpText')} />
            <FormInput
              type={inputTypes.TEXT}
              name="label"
              value={value}
              errors={labelErrors}
              autoFocus={!id}
              onChange={handleInputChange}
            />
          </FormRow>
        </Form>

        {id && !isTagUsed ? (
          <div>{translate('TagIsNotUsedAndCanBeDeleted')}</div>
        ) : null}

        {sources.length ? (
          <ModalSection title={translate('Sources')}>
            {sources.map((source) => (
              <div key={source.id}>{source.name}</div>
            ))}
          </ModalSection>
        ) : null}

        {seriesIds.length ? (
          <ModalSection title={translate('Series')}>
            <TagInUse label={translate('Series')} count={seriesIds.length} />
          </ModalSection>
        ) : null}
      </ModalBody>

      <ModalFooter>
        {id ? (
          <Button
            className={styles.deleteButton}
            kind={kinds.DANGER}
            onPress={onDeleteTagPress}
          >
            {translate('Delete')}
          </Button>
        ) : null}

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          isDisabled={!trimmed || trimmed === label}
          onPress={() => saveTag(trimmed)}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default TagDetailsModalContent;
