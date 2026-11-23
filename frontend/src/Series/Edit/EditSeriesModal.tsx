// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Series/Edit/EditSeriesModalContent.tsx
// Generral can't move files yet, so the path is only editable while the series has no files.
import React, { useCallback, useState } from 'react';
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
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import { inputTypes } from 'Helpers/Props';
import Series from 'Series/Series';
import { useSaveSeries } from 'Series/useSeries';
import useQualityProfiles from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';

type Editable = Pick<
  Series,
  | 'title'
  | 'monitored'
  | 'monitorNewItems'
  | 'seasonFolder'
  | 'qualityProfileId'
  | 'path'
  | 'tags'
>;

const FIELDS: (keyof Editable)[] = [
  'title',
  'monitored',
  'monitorNewItems',
  'seasonFolder',
  'qualityProfileId',
  'path',
  'tags',
];

const monitorNewItemsOptions = [
  {
    key: 'all',
    get value() {
      return translate('MonitorAllSeasons');
    },
  },
  {
    key: 'none',
    get value() {
      return translate('MonitorNoNewSeasons');
    },
  },
];

interface EditSeriesModalProps {
  isOpen: boolean;
  series: Series;
  onModalClose: () => void;
}

function EditSeriesModal({ isOpen, series, onModalClose }: EditSeriesModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      {isOpen ? (
        <EditSeriesModalContent series={series} onModalClose={onModalClose} />
      ) : null}
    </Modal>
  );
}

function EditSeriesModalContent({
  series,
  onModalClose,
}: Omit<EditSeriesModalProps, 'isOpen'>) {
  const { saveSeries, isSaving, saveError } = useSaveSeries(series.id);
  const { data: qualityProfiles } = useQualityProfiles();
  // Mounted on open: starts from the stored series every time.
  const [values, setValues] = useState<Editable>(() => ({
    title: series.title,
    monitored: series.monitored,
    monitorNewItems: series.monitorNewItems,
    seasonFolder: series.seasonFolder,
    qualityProfileId: series.qualityProfileId,
    path: series.path,
    tags: series.tags,
  }));

  const hasFiles = (series.statistics?.episodeFileCount ?? 0) > 0;
  const errors = getValidationFailures(saveError).errors;
  const errorsFor = (name: string) =>
    errors.filter((e) => e.propertyName === name);

  const handleChange = useCallback(({ name, value }: InputChanged) => {
    setValues((current) => ({
      ...current,
      [name]: name === 'qualityProfileId' ? Number(value) : value,
    }));
  }, []);

  const handleSave = useCallback(() => {
    saveSeries(values, { onSuccess: onModalClose });
  }, [values, saveSeries, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('EditSeriesModalHeader', { title: series.title })}
      </ModalHeader>

      <ModalBody>
        <Form
          validationErrors={errors.filter(
            (e) => !(FIELDS as string[]).includes(e.propertyName)
          )}
        >
          <FormRow>
            <FormLabel>{translate('Title')}</FormLabel>
            <FormInput
              type={inputTypes.TEXT}
              name="title"
              value={values.title}
              errors={errorsFor('title')}
              onChange={handleChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('Monitored')}</FormLabel>
            <FormInput
              type={inputTypes.CHECK}
              name="monitored"
              helpText={translate('EditSeriesMonitoredHelpText')}
              value={values.monitored}
              onChange={handleChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('MonitorNewSeasons')}</FormLabel>
            <FormInputHelpText text={translate('MonitorNewSeasonsHelpText')} />
            <FormInput
              type={inputTypes.SELECT}
              name="monitorNewItems"
              values={monitorNewItemsOptions}
              value={values.monitorNewItems}
              onChange={handleChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('SeasonFolder')}</FormLabel>
            <FormInput
              type={inputTypes.CHECK}
              name="seasonFolder"
              helpText={translate('UseSeasonFolderHelpText')}
              value={values.seasonFolder}
              onChange={handleChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('QualityProfile')}</FormLabel>
            <FormInput
              type={inputTypes.SELECT}
              name="qualityProfileId"
              values={qualityProfiles.map((profile) => ({
                key: profile.id,
                value: profile.name,
              }))}
              value={values.qualityProfileId}
              errors={errorsFor('qualityProfileId')}
              onChange={handleChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('Path')}</FormLabel>
            <FormInputHelpText
              text={
                hasFiles
                  ? translate('EditSeriesPathHasFilesHelpText')
                  : translate('EditSeriesPathHelpText')
              }
            />
            <FormInput
              type={inputTypes.TEXT}
              name="path"
              readOnly={hasFiles}
              value={values.path}
              errors={errorsFor('path')}
              onChange={handleChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('Tags')}</FormLabel>
            <FormInput
              type={inputTypes.TAG}
              name="tags"
              value={values.tags}
              errors={errorsFor('tags')}
              onChange={handleChange}
            />
          </FormRow>
        </Form>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          onPress={handleSave}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditSeriesModal;
