import classNames from 'classnames';
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import { inputTypes, kinds } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { useImportPreset, usePresets } from './usePresets';
import styles from './AddSeries.module.css';

interface StartStepProps {
  presetId?: string;
  scratch: boolean;
  onPresetSelect: (presetId: string) => void;
  onScratchSelect: () => void;
}

/** From preset (the gallery, with Import), or from scratch. */
function StartStep({
  presetId,
  scratch,
  onPresetSelect,
  onScratchSelect,
}: StartStepProps) {
  const { data: presets, isLoading, error } = usePresets();
  const { importPreset, isImporting, importError, reset } = useImportPreset();
  const [importing, setImporting] = useState(false);
  const [url, setUrl] = useState('');
  const [yaml, setYaml] = useState('');

  const handleFileChange = useCallback(
    async (event: React.ChangeEvent<HTMLInputElement>) => {
      const file = event.target.files?.[0];

      if (file) {
        setYaml(await file.text());
      }
    },
    []
  );

  const handleImportPress = useCallback(() => {
    importPreset(url.trim() ? { url: url.trim() } : { yaml }, {
      onSuccess: (preset) => {
        setImporting(false);
        setUrl('');
        setYaml('');
        onPresetSelect(preset.id);
      },
    });
  }, [importPreset, url, yaml, onPresetSelect]);

  const importFailures = getValidationFailures(importError).errors;

  return (
    <>
      <p className={styles.stepHelp}>{translate('AddSeriesStartHelpText')}</p>

      {isLoading ? <LoadingIndicator /> : null}

      {error ? (
        <Alert kind={kinds.DANGER}>{translate('PresetsLoadError')}</Alert>
      ) : null}

      <div className={styles.sources} role="listbox">
        {presets.map((preset) => (
          <Link
            key={preset.id}
            className={classNames(
              styles.source,
              preset.id === presetId && styles.selectedSource
            )}
            role="option"
            aria-selected={preset.id === presetId}
            onPress={() => onPresetSelect(preset.id)}
          >
            <span className={styles.sourceName}>
              {preset.name}
              {preset.description ? (
                <span className={styles.presetDescription}>
                  {preset.description}
                </span>
              ) : null}
            </span>
            <span className={styles.sourceType}>{preset.sourceType}</span>
          </Link>
        ))}

        <Link
          className={classNames(styles.source, importing && styles.selectedSource)}
          role="option"
          aria-selected={importing}
          onPress={() => {
            reset();
            setImporting(!importing);
          }}
        >
          <span className={styles.sourceName}>{translate('ImportPreset')}</span>
          <span className={styles.sourceType}>
            {translate('ImportPresetHelpText')}
          </span>
        </Link>

        <Link
          className={classNames(styles.source, scratch && styles.selectedSource)}
          role="option"
          aria-selected={scratch}
          onPress={onScratchSelect}
        >
          <span className={styles.sourceName}>{translate('FromScratch')}</span>
          <span className={styles.sourceType}>
            {translate('FromScratchHelpText')}
          </span>
        </Link>
      </div>

      {importing ? (
        <div className={styles.importForm}>
        <Form>
          <FormRow>
            <FormLabel>{translate('Url')}</FormLabel>
            <FormInput
              type={inputTypes.TEXT}
              name="url"
              value={url}
              placeholder="https://"
              onChange={({ value }: InputChanged<string>) => setUrl(value)}
            />
          </FormRow>
          <FormRow>
            <FormLabel>{translate('File')}</FormLabel>
            <input
              type="file"
              name="file"
              accept=".yaml,.yml,text/yaml"
              onChange={handleFileChange}
            />
          </FormRow>
          <FormRow>
            <FormLabel>YAML</FormLabel>
            <FormInput
              type={inputTypes.TEXT_AREA}
              name="yaml"
              inputClassName={styles.definition}
              value={yaml}
              onChange={({ value }: InputChanged<string>) => setYaml(value)}
            />
          </FormRow>
          {importFailures.map((failure) => (
            <Alert key={failure.propertyName + failure.errorMessage} kind={kinds.DANGER}>
              {failure.errorMessage}
            </Alert>
          ))}
          {importError && !importFailures.length ? (
            <Alert kind={kinds.DANGER}>{translate('ImportPresetError')}</Alert>
          ) : null}
          <SpinnerButton
            kind={kinds.PRIMARY}
            isSpinning={isImporting}
            isDisabled={!url.trim() && !yaml.trim()}
            onPress={handleImportPress}
          >
            {translate('Import')}
          </SpinnerButton>
        </Form>
        </div>
      ) : null}
    </>
  );
}

export default StartStep;
