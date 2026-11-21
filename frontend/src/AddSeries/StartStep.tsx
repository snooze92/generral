import classNames from 'classnames';
import React from 'react';
import Alert from 'Components/Alert';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { usePresets } from './usePresets';
import styles from './AddSeries.module.css';

interface StartStepProps {
  presetId?: string;
  scratch: boolean;
  onPresetSelect: (presetId: string) => void;
  onScratchSelect: () => void;
}

/** From preset (the gallery) or from scratch. */
function StartStep({
  presetId,
  scratch,
  onPresetSelect,
  onScratchSelect,
}: StartStepProps) {
  const { data: presets, isLoading, error } = usePresets();

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
    </>
  );
}

export default StartStep;
