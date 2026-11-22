// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/MediaManagement/Naming/Naming.tsx
// Without the token picker modal and the anime format: a short token list is shown instead.
import React, { useCallback, useMemo } from 'react';
import FieldSet from 'Components/FieldSet';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import useDebounce from 'Helpers/Hooks/useDebounce';
import { inputTypes, sizes } from 'Helpers/Props';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import { NAMING_PATH, NamingConfig, NamingExamples } from './MediaManagement';

const colonReplacementOptions = [
  { key: 0, value: () => translate('Delete') },
  { key: 1, value: () => translate('ReplaceWithDash') },
  { key: 2, value: () => translate('ReplaceWithSpaceDash') },
  { key: 3, value: () => translate('ReplaceWithSpaceDashSpace') },
  { key: 4, value: () => translate('SmartReplace') },
  { key: 5, value: () => translate('Custom') },
];

const multiEpisodeStyleOptions = [
  { key: 0, value: () => translate('Extend') },
  { key: 1, value: () => translate('Duplicate') },
  { key: 2, value: () => translate('Repeat') },
  { key: 3, value: () => translate('Scene') },
  { key: 4, value: () => translate('Range') },
  { key: 5, value: () => translate('PrefixedRange') },
];

const toValues = (options: { key: number; value: () => string }[]) =>
  options.map(({ key, value }) => ({ key, value: value() }));

const TOKENS =
  '{Series Title} {Series TitleYear} {season:00} {episode:00} {Episode Title} {Air-Date} {Quality Full} {Release Group} {Part Title} {Part Key} {Event Key} {Event:<field>} {Generral Id}';

interface NamingProps {
  settings: PendingSection<NamingConfig>;
  onInputChange: (change: InputChanged) => void;
}

function Example({ value }: { value?: string }) {
  return (
    <FormInputHelpText
      text={value ? `${translate('Example')}: ${value}` : translate('InvalidFormat')}
      isError={!value}
    />
  );
}

function Naming({ settings, onInputChange }: NamingProps) {
  const showAdvancedSettings = useShowAdvancedSettings();

  const values = useMemo(
    () =>
      Object.fromEntries(
        Object.entries(settings).map(([key, setting]) => [
          key,
          (setting as { value: unknown }).value,
        ])
      ) as Partial<NamingConfig>,
    [settings]
  );
  const current = useDebounce(values, 300);
  const { data: examples = {} as NamingExamples } = useApiQuery<NamingExamples>(
    {
      path: `${NAMING_PATH}/examples`,
      queryParams: current,
      queryOptions: { enabled: 'standardEpisodeFormat' in current },
    }
  );

  const handleChange = useCallback(
    ({ name, value }: InputChanged) =>
      onInputChange({
        name,
        value:
          name === 'colonReplacementFormat' || name === 'multiEpisodeStyle'
            ? Number(value)
            : value,
      }),
    [onInputChange]
  );

  const rename = settings.renameEpisodes?.value;

  return (
    <FieldSet legend={translate('EpisodeNaming')}>
      <FormRow size={sizes.MEDIUM}>
        <FormLabel>{translate('RenameEpisodes')}</FormLabel>
        <FormInputHelpText text={translate('RenameEpisodesHelpText')} />
        <FormInput
          type={inputTypes.CHECK}
          name="renameEpisodes"
          {...settings.renameEpisodes}
          onChange={handleChange}
        />
      </FormRow>

      <FormRow size={sizes.MEDIUM}>
        <FormLabel>{translate('ReplaceIllegalCharacters')}</FormLabel>
        <FormInputHelpText
          text={translate('ReplaceIllegalCharactersHelpText')}
        />
        <FormInput
          type={inputTypes.CHECK}
          name="replaceIllegalCharacters"
          {...settings.replaceIllegalCharacters}
          onChange={handleChange}
        />
      </FormRow>

      {settings.replaceIllegalCharacters?.value ? (
        <FormRow size={sizes.MEDIUM}>
          <FormLabel>{translate('ColonReplacement')}</FormLabel>
          <FormInput
            type={inputTypes.SELECT}
            name="colonReplacementFormat"
            values={toValues(colonReplacementOptions)}
            {...settings.colonReplacementFormat}
            onChange={handleChange}
          />
        </FormRow>
      ) : null}

      {settings.replaceIllegalCharacters?.value &&
      settings.colonReplacementFormat?.value === 5 ? (
        <FormRow size={sizes.MEDIUM}>
          <FormLabel>{translate('CustomColonReplacement')}</FormLabel>
          <FormInput
            type={inputTypes.TEXT}
            name="customColonReplacementFormat"
            {...settings.customColonReplacementFormat}
            onChange={handleChange}
          />
        </FormRow>
      ) : null}

      {rename ? (
        <>
          <FormRow size={sizes.LARGE}>
            <FormLabel>{translate('StandardEpisodeFormat')}</FormLabel>
            <FormInput
              type={inputTypes.TEXT}
              name="standardEpisodeFormat"
              {...settings.standardEpisodeFormat}
              onChange={handleChange}
            />
            <Example value={examples.singleEpisodeExample} />
            <FormInputHelpText
              text={`${translate('MultiEpisode')}: ${examples.multiEpisodeExample ?? ''}`}
            />
          </FormRow>

          <FormRow size={sizes.LARGE}>
            <FormLabel>{translate('DailyEpisodeFormat')}</FormLabel>
            <FormInput
              type={inputTypes.TEXT}
              name="dailyEpisodeFormat"
              {...settings.dailyEpisodeFormat}
              onChange={handleChange}
            />
            <Example value={examples.dailyEpisodeExample} />
          </FormRow>
        </>
      ) : null}

      <FormRow size={sizes.LARGE}>
        <FormLabel>{translate('SeriesFolderFormat')}</FormLabel>
        <FormInput
          type={inputTypes.TEXT}
          name="seriesFolderFormat"
          {...settings.seriesFolderFormat}
          onChange={handleChange}
        />
        <FormInputHelpText text={translate('SeriesFolderFormatHelpText')} />
        <Example value={examples.seriesFolderExample} />
      </FormRow>

      <FormRow size={sizes.LARGE}>
        <FormLabel>{translate('SeasonFolderFormat')}</FormLabel>
        <FormInput
          type={inputTypes.TEXT}
          name="seasonFolderFormat"
          {...settings.seasonFolderFormat}
          onChange={handleChange}
        />
        <Example value={examples.seasonFolderExample} />
      </FormRow>

      {showAdvancedSettings ? (
        <FormRow size={sizes.LARGE}>
          <FormLabel>{translate('SpecialsFolderFormat')}</FormLabel>
          <FormInput
            type={inputTypes.TEXT}
            name="specialsFolderFormat"
            {...settings.specialsFolderFormat}
            onChange={handleChange}
          />
          <Example value={examples.specialsFolderExample} />
        </FormRow>
      ) : null}

      {rename ? (
        <FormRow size={sizes.MEDIUM}>
          <FormLabel>{translate('MultiEpisodeStyle')}</FormLabel>
          <FormInput
            type={inputTypes.SELECT}
            name="multiEpisodeStyle"
            values={toValues(multiEpisodeStyleOptions)}
            {...settings.multiEpisodeStyle}
            onChange={handleChange}
          />
        </FormRow>
      ) : null}

      <FormRow size={sizes.LARGE}>
        <FormLabel>{translate('FileNameTokens')}</FormLabel>
        <FormInputHelpText text={TOKENS} />
      </FormRow>
    </FieldSet>
  );
}

export default Naming;
