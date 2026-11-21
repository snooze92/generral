import classNames from 'classnames';
import React, { useCallback, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import { kinds } from 'Helpers/Props';
import { MappingPreview } from 'Mapping/Mapping';
import useSeries from 'Series/useSeries';
import { useSources } from 'Sources/useSources';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import defaultDefinition from './defaultDefinition';
import MappingStep from './MappingStep';
import OptionsStep from './OptionsStep';
import SourceStep from './SourceStep';
import useAddSeries, { NewSeries } from './useAddSeries';
import styles from './AddSeries.module.css';

const STEPS = ['source', 'mapping', 'options'] as const;

type Step = (typeof STEPS)[number];

const stepLabels: Record<Step, () => string> = {
  source: () => translate('Source'),
  mapping: () => translate('Mapping'),
  options: () => translate('Options'),
};

/** The from-scratch Add Series wizard (docs/specs/10-ui.md § Add series). */
function AddSeries() {
  const navigate = useNavigate();
  const { data: sources } = useSources();
  const { data: allSeries } = useSeries();

  const [step, setStep] = useState<Step>('source');
  const [sourceId, setSourceId] = useState<number>();
  const [definition, setDefinition] = useState(defaultDefinition);
  const [seriesType, setSeriesType] = useState<MappingPreview['seriesType']>();
  const [titleEdited, setTitleEdited] = useState(false);
  const [series, setSeries] = useState<NewSeries>({
    title: '',
    rootFolderPath: '',
    monitored: true,
    monitorNewItems: 'all',
    seasonFolder: true,
    seriesType: 'standard',
    tags: [],
  });

  const { addSeries, isAdding, addError, reset } = useAddSeries();
  const source = sources.find((s) => s.id === sourceId);
  const stepIndex = STEPS.indexOf(step);

  const handleSourceSelect = useCallback(
    (id: number) => {
      setSourceId(id);

      // Defaults: the source's name, and the root folder of an existing series.
      setSeries((current) => ({
        ...current,
        title: titleEdited
          ? current.title
          : (sources.find((s) => s.id === id)?.name ?? ''),
        rootFolderPath:
          current.rootFolderPath || (allSeries[0]?.rootFolderPath ?? ''),
      }));
    },
    [sources, allSeries, titleEdited]
  );

  const handlePreview = useCallback(
    (preview: MappingPreview) => setSeriesType(preview.seriesType),
    []
  );

  const handleOptionChange = useCallback(({ name, value }: InputChanged) => {
    if (name === 'title') {
      setTitleEdited(true);
    }

    setSeries((current) => ({ ...current, [name]: value }));
  }, []);

  const handleAddPress = useCallback(() => {
    if (!source) {
      return;
    }

    reset();
    addSeries(
      {
        series: { ...series, seriesType: seriesType ?? series.seriesType },
        mapping: { name: source.name, sourceId: source.id, definition },
      },
      { onSuccess: (added) => navigate(`/series/${added.titleSlug}`) }
    );
  }, [
    source,
    series,
    seriesType,
    definition,
    addSeries,
    reset,
    navigate,
  ]);

  const seriesFailures =
    addError?.step === 'series'
      ? getValidationFailures(addError.error).errors
      : [];
  const mappingFailures =
    addError?.step === 'mapping'
      ? getValidationFailures(addError.error).errors
      : [];

  const canContinue =
    step === 'source' ? !!source : step === 'mapping' ? !!definition.trim() : true;

  return (
    <PageContent title={translate('AddNewSeries')}>
      <PageContentBody>
        <PageHeading scope={translate('Series')} title={translate('AddNewSeries')} />

        <ol className={styles.steps}>
          {STEPS.map((s, index) => (
            <li
              key={s}
              className={classNames(
                styles.step,
                s === step && styles.currentStep,
                index < stepIndex && styles.doneStep
              )}
              aria-current={s === step ? 'step' : undefined}
            >
              {index < stepIndex ? (
                <Link onPress={() => setStep(s)}>{stepLabels[s]()}</Link>
              ) : (
                stepLabels[s]()
              )}
            </li>
          ))}
        </ol>

        {step === 'source' ? (
          <SourceStep sourceId={sourceId} onSourceSelect={handleSourceSelect} />
        ) : null}

        {step === 'mapping' && source ? (
          <MappingStep
            sourceId={source.id}
            definition={definition}
            onDefinitionChange={setDefinition}
            onPreview={handlePreview}
          />
        ) : null}

        {step === 'options' ? (
          <>
            {addError?.step === 'mapping' ? (
              <Alert kind={kinds.DANGER}>
                {addError.series ? (
                  <>
                    {translate('AddSeriesMappingFailedSeriesKept')}{' '}
                    <Link to={`/series/${addError.series.titleSlug}`}>
                      {addError.series.title}
                    </Link>
                  </>
                ) : (
                  translate('AddSeriesMappingFailed')
                )}
                {mappingFailures.map((failure, index) => (
                  <div key={index}>{failure.errorMessage}</div>
                ))}
              </Alert>
            ) : null}

            {addError?.step === 'series' && !seriesFailures.length ? (
              <Alert kind={kinds.DANGER}>{translate('AddSeriesFailed')}</Alert>
            ) : null}

            <OptionsStep
              series={series}
              seriesType={seriesType}
              validationErrors={seriesFailures}
              onChange={handleOptionChange}
            />
          </>
        ) : null}

        <div className={styles.buttons}>
          {stepIndex > 0 ? (
            <Button
              isDisabled={isAdding}
              onPress={() => setStep(STEPS[stepIndex - 1])}
            >
              {translate('Back')}
            </Button>
          ) : null}

          {step === 'options' ? (
            <SpinnerButton
              kind={kinds.PRIMARY}
              isSpinning={isAdding}
              isDisabled={!series.title.trim() || !series.rootFolderPath.trim()}
              onPress={handleAddPress}
            >
              {translate('AddSeriesWithTitle', { title: series.title })}
            </SpinnerButton>
          ) : (
            <Button
              kind={kinds.PRIMARY}
              isDisabled={!canContinue}
              onPress={() => setStep(STEPS[stepIndex + 1])}
            >
              {translate('Next')}
            </Button>
          )}
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default AddSeries;
