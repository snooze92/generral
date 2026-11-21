import classNames from 'classnames';
import React from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { kinds } from 'Helpers/Props';
import { useSources } from 'Sources/useSources';
import translate from 'Utilities/String/translate';
import styles from './AddSeries.module.css';

interface SourceStepProps {
  sourceId?: number;
  onSourceSelect: (sourceId: number) => void;
}

function SourceStep({ sourceId, onSourceSelect }: SourceStepProps) {
  const { data: sources, isLoading, error } = useSources();

  if (isLoading) {
    return <LoadingIndicator />;
  }

  if (error) {
    return <Alert kind={kinds.DANGER}>{translate('SourcesLoadError')}</Alert>;
  }

  if (!sources.length) {
    return (
      <div className={styles.empty}>
        <div className={styles.emptyMessage}>
          {translate('AddSeriesNoSources')}
        </div>
        <Button to="/sources" kind={kinds.PRIMARY}>
          {translate('AddSource')}
        </Button>
      </div>
    );
  }

  return (
    <>
      <p className={styles.stepHelp}>{translate('AddSeriesSourceHelpText')}</p>

      <div className={styles.sources} role="listbox">
        {sources.map((source) => (
          <Link
            key={source.id}
            className={classNames(
              styles.source,
              source.id === sourceId && styles.selectedSource
            )}
            role="option"
            aria-selected={source.id === sourceId}
            onPress={() => onSourceSelect(source.id)}
          >
            <span className={styles.sourceName}>{source.name}</span>
            <span className={styles.sourceType}>
              {source.typeName ?? source.type}
            </span>
            <span className={styles.sourceEvents}>
              {translate('CountEvents', { count: source.eventCount })}
            </span>
          </Link>
        ))}
      </div>

      <p className={styles.stepHelp}>
        <Link to="/sources">{translate('AddSeriesManageSources')}</Link>
      </p>
    </>
  );
}

export default SourceStep;
