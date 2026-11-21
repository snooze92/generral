import React, { useEffect, useState } from 'react';
import Alert from 'Components/Alert';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import { inputTypes, kinds } from 'Helpers/Props';
import { MappingPreview } from 'Mapping/Mapping';
import MappingPreviewTable from 'Mapping/MappingPreviewTable';
import useMappingPreview from 'Mapping/useMappingPreview';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './AddSeries.module.css';

interface MappingStepProps {
  sourceId: number;
  definition: string;
  onDefinitionChange: (definition: string) => void;
  onPreview: (preview: MappingPreview) => void;
}

function MappingStep({
  sourceId,
  definition,
  onDefinitionChange,
  onPreview,
}: MappingStepProps) {
  // Previews the definition as of the last Preview press.
  const [previewed, setPreviewed] = useState<string>();

  const { data, error, isFetching, refetch } = useMappingPreview(
    previewed === undefined ? undefined : { sourceId, definition: previewed }
  );

  const handlePreviewPress = () => {
    if (previewed === definition) {
      refetch();
    } else {
      setPreviewed(definition);
    }
  };

  useEffect(() => {
    if (data) {
      onPreview(data);
    }
  }, [data, onPreview]);

  const { errors } = getValidationFailures(error);
  const definitionErrors = errors.filter(
    (e) => e.propertyName === 'definition'
  );
  const otherErrors = errors.filter((e) => e.propertyName !== 'definition');
  const preview = error ? undefined : data;

  return (
    <>
      <FormRow layout="stacked">
        <FormLabel>{translate('Mapping')}</FormLabel>
        <FormInputHelpText text={translate('AddSeriesMappingHelpText')} />
        <FormInput
          type={inputTypes.TEXT_AREA}
          name="definition"
          inputClassName={styles.definition}
          value={definition}
          errors={definitionErrors}
          onChange={({ value }: InputChanged<string>) =>
            onDefinitionChange(value)
          }
        />
      </FormRow>

      <div className={styles.previewButton}>
        <SpinnerButton
          isSpinning={isFetching}
          isDisabled={!definition.trim()}
          onPress={handlePreviewPress}
        >
          {translate('Preview')}
        </SpinnerButton>
      </div>

      {otherErrors.map((e, index) => (
        <Alert key={index} kind={kinds.DANGER}>
          {e.errorMessage}
        </Alert>
      ))}

      {error && !errors.length ? (
        <Alert kind={kinds.DANGER}>{translate('MappingPreviewFailed')}</Alert>
      ) : null}

      {preview ? <MappingPreviewTable preview={preview} /> : null}
    </>
  );
}

export default MappingStep;
