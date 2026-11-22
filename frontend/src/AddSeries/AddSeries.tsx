import classNames from 'classnames';
import React, { useCallback, useMemo, useState } from 'react';
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
import { useRootFolders } from 'Settings/MediaManagement/MediaManagement';
import { useSources } from 'Sources/useSources';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import defaultDefinition from './defaultDefinition';
import MappingStep from './MappingStep';
import OptionsStep from './OptionsStep';
import PresetStep from './PresetStep';
import SourceStep from './SourceStep';
import StartStep from './StartStep';
import useAddSeries, { NewSeries } from './useAddSeries';
import { useInstantiatePreset, usePresets } from './usePresets';
import styles from './AddSeries.module.css';

const SCRATCH_STEPS = ['start', 'source', 'mapping', 'options'] as const;
const PRESET_STEPS = ['start', 'options'] as const;

type Step = (typeof SCRATCH_STEPS)[number];

const stepLabels: Record<Step, () => string> = {
  start: () => translate('Start'),
  source: () => translate('Source'),
  mapping: () => translate('Mapping'),
  options: () => translate('Options'),
};

/** The Add Series wizard, from a preset or from scratch (docs/specs/10-ui.md § Add series). */
function AddSeries() {
  const navigate = useNavigate();
  const { data: sources } = useSources();
  const { data: allSeries } = useSeries();
  const { data: rootFolders } = useRootFolders();
  // An existing series' root folder if it is still configured, else the first one.
  const defaultRootFolder =
    rootFolders.find((f) => f.path === allSeries[0]?.rootFolderPath)?.path ??
    rootFolders[0]?.path ??
    '';

  const { data: presets } = usePresets();
  const [step, setStep] = useState<Step>('start');
  const [presetId, setPresetId] = useState<string>();
  const [scratch, setScratch] = useState(false);
  const [presetValues, setPresetValues] = useState<Record<string, string>>({});
  const [sourceId, setSourceId] = useState<number>();
  const [definition, setDefinition] = useState(defaultDefinition);
  const [seriesType, setSeriesType] = useState<MappingPreview['seriesType']>();
  const [titleEdited, setTitleEdited] = useState(false);
  const [options, setSeries] = useState<NewSeries>({
    title: '',
    rootFolderPath: '',
    monitored: true,
    monitorNewItems: 'all',
    seasonFolder: true,
    seriesType: 'standard',
    qualityProfileId: 0,
    tags: [],
  });
  const series = useMemo(
    () => ({
      ...options,
      rootFolderPath: options.rootFolderPath || defaultRootFolder,
    }),
    [options, defaultRootFolder]
  );

  const { addSeries, isAdding, addError, reset } = useAddSeries();
  const { instantiate, isInstantiating, instantiateError, reset: resetInstantiate } =
    useInstantiatePreset();
  const source = sources.find((s) => s.id === sourceId);
  const preset = scratch ? undefined : presets.find((p) => p.id === presetId);
  const STEPS: readonly Step[] = preset ? PRESET_STEPS : SCRATCH_STEPS;
  const stepIndex = STEPS.indexOf(step);

  const handlePresetSelect = useCallback(
    (id: string) => {
      setPresetId(id);
      setScratch(false);
      setPresetValues({});
      setSeries((current) => ({
        ...current,
        title: titleEdited
          ? current.title
          : (presets.find((p) => p.id === id)?.seriesTitle ?? ''),
      }));
    },
    [presets, titleEdited]
  );

  const handleScratchSelect = useCallback(() => {
    setScratch(true);
    setPresetId(undefined);
  }, []);

  const handlePresetValueChange = useCallback(
    ({ name, value }: InputChanged<string>) =>
      setPresetValues((current) => ({ ...current, [name]: value })),
    []
  );

  const handleSourceSelect = useCallback(
    (id: number) => {
      setSourceId(id);

      // Defaults to the source's name.
      setSeries((current) => ({
        ...current,
        title: titleEdited
          ? current.title
          : (sources.find((s) => s.id === id)?.name ?? ''),
      }));
    },
    [sources, titleEdited]
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
    if (preset) {
      const secrets = new Set(
        preset.parameters.filter((p) => p.secret).map((p) => p.name)
      );
      const pick = (secret: boolean) =>
        Object.fromEntries(
          Object.entries(presetValues).filter(
            ([name, value]) => value.trim() && secrets.has(name) === secret
          )
        );

      resetInstantiate();
      instantiate(
        {
          id: preset.id,
          request: {
            title: series.title,
            rootFolderPath: series.rootFolderPath,
            monitored: series.monitored,
            monitorNewItems: series.monitorNewItems,
            seasonFolder: series.seasonFolder,
            qualityProfileId: series.qualityProfileId,
            tags: series.tags,
            parameters: pick(false),
            secrets: pick(true),
          },
        },
        { onSuccess: (added) => navigate(`/series/${added.titleSlug}`) }
      );
      return;
    }

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
    preset,
    presetValues,
    instantiate,
    resetInstantiate,
    source,
    series,
    seriesType,
    definition,
    addSeries,
    reset,
    navigate,
  ]);

  const presetFailures = instantiateError
    ? getValidationFailures(instantiateError).errors
    : [];
  const seriesFailures = preset
    ? presetFailures
    : addError?.step === 'series'
      ? getValidationFailures(addError.error).errors
      : [];
  const mappingFailures =
    addError?.step === 'mapping'
      ? getValidationFailures(addError.error).errors
      : [];

  const canContinue =
    step === 'start'
      ? !!preset || scratch
      : step === 'source'
        ? !!source
        : step === 'mapping'
          ? !!definition.trim()
          : true;

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

        {step === 'start' ? (
          <StartStep
            presetId={presetId}
            scratch={scratch}
            onPresetSelect={handlePresetSelect}
            onScratchSelect={handleScratchSelect}
          />
        ) : null}

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

            {(preset ? !!instantiateError : addError?.step === 'series') &&
            !seriesFailures.length ? (
              <Alert kind={kinds.DANGER}>{translate('AddSeriesFailed')}</Alert>
            ) : null}

            {preset ? (
              <PresetStep
                preset={preset}
                values={presetValues}
                onChange={handlePresetValueChange}
              />
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
              isDisabled={isAdding || isInstantiating}
              onPress={() => setStep(STEPS[stepIndex - 1])}
            >
              {translate('Back')}
            </Button>
          ) : null}

          {step === 'options' ? (
            <SpinnerButton
              kind={kinds.PRIMARY}
              isSpinning={isAdding || isInstantiating}
              isDisabled={
                !series.title.trim() ||
                !series.rootFolderPath.trim() ||
                !!preset?.requiredSecrets.some((s) => !presetValues[s]?.trim())
              }
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
