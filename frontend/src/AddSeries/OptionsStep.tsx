import React from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { inputTypes } from 'Helpers/Props';
import { SeriesType } from 'Series/Series';
import useQualityProfiles from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import { ValidationError } from 'typings/pending';
import translate from 'Utilities/String/translate';
import { NewSeries } from './useAddSeries';

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

const FIELDS = ['title', 'rootFolderPath', 'path', 'qualityProfileId', 'tags'];

interface OptionsStepProps {
  series: NewSeries;
  seriesType?: SeriesType;
  validationErrors: ValidationError[];
  onChange: (change: InputChanged) => void;
}

function OptionsStep({
  series,
  seriesType,
  validationErrors,
  onChange,
}: OptionsStepProps) {
  const { data: qualityProfiles } = useQualityProfiles();
  const qualityProfileOptions = qualityProfiles.map((profile) => ({
    key: profile.id,
    value: profile.name,
  }));

  const errorsFor = (...names: string[]) =>
    validationErrors.filter((e) => names.includes(e.propertyName));

  return (
    <Form
      validationErrors={validationErrors.filter(
        (e) => !FIELDS.includes(e.propertyName)
      )}
    >
      <FormRow>
        <FormLabel>{translate('Title')}</FormLabel>
        <FormInput
          type={inputTypes.TEXT}
          name="title"
          value={series.title}
          errors={errorsFor('title')}
          onChange={onChange}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('RootFolder')}</FormLabel>
        <FormInputHelpText text={translate('AddSeriesRootFolderHelpText')} />
        <FormInput
          type={inputTypes.TEXT}
          name="rootFolderPath"
          placeholder="/tv"
          value={series.rootFolderPath}
          errors={errorsFor('rootFolderPath', 'path')}
          onChange={onChange}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('QualityProfile')}</FormLabel>
        <FormInput
          type={inputTypes.SELECT}
          name="qualityProfileId"
          values={qualityProfileOptions}
          value={series.qualityProfileId || (qualityProfiles[0]?.id ?? 0)}
          errors={errorsFor('qualityProfileId')}
          onChange={({ name, value }: InputChanged<string | number>) =>
            onChange({ name, value: Number(value) })
          }
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('Monitored')}</FormLabel>
        <FormInput
          type={inputTypes.CHECK}
          name="monitored"
          helpText={translate('AddSeriesMonitoredHelpText')}
          value={series.monitored}
          onChange={onChange}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('MonitorNewSeasons')}</FormLabel>
        <FormInputHelpText text={translate('MonitorNewSeasonsHelpText')} />
        <FormInput
          type={inputTypes.SELECT}
          name="monitorNewItems"
          values={monitorNewItemsOptions}
          value={series.monitorNewItems}
          onChange={onChange}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('SeasonFolder')}</FormLabel>
        <FormInput
          type={inputTypes.CHECK}
          name="seasonFolder"
          helpText={translate('UseSeasonFolderHelpText')}
          value={series.seasonFolder}
          onChange={onChange}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('SeriesType')}</FormLabel>
        <FormInputHelpText text={translate('AddSeriesSeriesTypeHelpText')} />
        <FormInput
          type={inputTypes.TEXT}
          name="seriesType"
          readOnly={true}
          value={
            (seriesType ?? series.seriesType) === 'daily'
              ? translate('Daily')
              : translate('Standard')
          }
          onChange={() => {}}
        />
      </FormRow>

      <FormRow>
        <FormLabel>{translate('Tags')}</FormLabel>
        <FormInput
          type={inputTypes.TAG}
          name="tags"
          value={series.tags}
          errors={errorsFor('tags')}
          onChange={onChange}
        />
      </FormRow>
    </Form>
  );
}

export default OptionsStep;
